using System.Globalization;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Backups;
using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Infrastructure;

public sealed class BackupService
{
    private const int AutomaticBackupsToKeep = 7;
    private readonly AppPaths _paths;
    private readonly SqliteDatabase _database;
    private readonly AuditService _auditService;

    public BackupService(
        AppPaths paths,
        SqliteDatabase database,
        AuditService auditService)
    {
        _paths = paths;
        _database = database;
        _auditService = auditService;
    }

    public BackupResult EnsureDailyBackup()
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);
        var dateStamp = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var expectedPath = Path.Combine(
            _paths.BackupsDirectory,
            $"lost-found-{dateStamp}-auto.db");
        if (File.Exists(expectedPath))
        {
            return BackupResult.Success("今日自动备份已存在。", expectedPath);
        }

        var result = CreateBackup(
            actorUserId: null,
            automatic: true,
            filePath: expectedPath);
        if (result.Succeeded)
        {
            PruneAutomaticBackups();
        }

        return result;
    }

    public BackupResult CreateManualBackup(UserAccount administrator)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return BackupResult.Failure("只有管理员可以手动备份。");
        }

        var fileName = $"lost-found-{DateTime.Now:yyyyMMdd-HHmmss}-manual.db";
        return CreateBackup(
            administrator.Id,
            automatic: false,
            Path.Combine(_paths.BackupsDirectory, fileName));
    }

    public IReadOnlyList<BackupFile> GetBackups()
    {
        if (!Directory.Exists(_paths.BackupsDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(_paths.BackupsDirectory, "lost-found-*.db")
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new BackupFile(
                    path,
                    info.Name,
                    info.Name.EndsWith("-auto.db", StringComparison.OrdinalIgnoreCase),
                    new DateTimeOffset(info.LastWriteTime),
                    info.Length);
            })
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();
    }

    public BackupResult RestoreBackup(UserAccount administrator, string backupPath)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return BackupResult.Failure("只有管理员可以恢复备份。");
        }

        var fullBackupPath = Path.GetFullPath(backupPath);
        var backupRoot = _paths.BackupsDirectory + Path.DirectorySeparatorChar;
        if (!fullBackupPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullBackupPath))
        {
            return BackupResult.Failure("备份文件不存在或路径无效。");
        }

        var validation = ValidateDatabase(fullBackupPath);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var safetyPath = Path.Combine(
            _paths.BackupsDirectory,
            $"lost-found-{DateTime.Now:yyyyMMdd-HHmmss}-prerestore.db");
        var safety = CreateBackup(
            administrator.Id,
            automatic: false,
            safetyPath);
        if (!safety.Succeeded)
        {
            return BackupResult.Failure($"无法创建恢复前安全备份：{safety.Message}");
        }

        var temporaryPath = _database.DatabasePath + ".restore-temp";
        try
        {
            SqliteConnection.ClearAllPools();
            File.Copy(fullBackupPath, temporaryPath, overwrite: true);
            File.Move(temporaryPath, _database.DatabasePath, overwrite: true);
            DeleteIfExists(_database.DatabasePath + "-wal");
            DeleteIfExists(_database.DatabasePath + "-shm");

            new DatabaseInitializer(_database).Initialize();
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            _auditService.Write(
                connection,
                transaction,
                administrator.Id,
                "BackupRestored",
                "Database",
                _database.DatabasePath,
                Path.GetFileName(fullBackupPath),
                DateTimeOffset.UtcNow);
            transaction.Commit();

            return BackupResult.Success("备份恢复成功。", safetyPath);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or SqliteException
            or InvalidOperationException)
        {
            DeleteIfExists(temporaryPath);
            try
            {
                SqliteConnection.ClearAllPools();
                File.Copy(safetyPath, _database.DatabasePath, overwrite: true);
                new DatabaseInitializer(_database).Initialize();
            }
            catch
            {
                return BackupResult.Failure(
                    $"恢复失败，且自动回退未完成：{exception.Message}。安全副本位于 {safetyPath}");
            }

            return BackupResult.Failure($"恢复失败，已回退到恢复前数据：{exception.Message}");
        }
    }

    private BackupResult CreateBackup(
        long? actorUserId,
        bool automatic,
        string filePath)
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            DeleteIfExists(temporaryPath);
            using (var source = _database.OpenConnection())
            using (var destination = new SqliteConnection(
                       new SqliteConnectionStringBuilder
                       {
                           DataSource = temporaryPath,
                           Mode = SqliteOpenMode.ReadWriteCreate,
                           Pooling = false
                       }.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            _auditService.Write(
                connection,
                transaction,
                actorUserId,
                automatic ? "AutomaticBackupCreated" : "ManualBackupCreated",
                "Database",
                Path.GetFileName(filePath),
                null,
                DateTimeOffset.UtcNow);
            transaction.Commit();

            return BackupResult.Success("备份创建成功。", filePath);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or SqliteException)
        {
            DeleteIfExists(temporaryPath);
            return BackupResult.Failure(exception.Message);
        }
    }

    private void PruneAutomaticBackups()
    {
        var staleFiles = GetBackups()
            .Where(backup => backup.IsAutomatic)
            .Skip(AutomaticBackupsToKeep)
            .ToArray();
        foreach (var backup in staleFiles)
        {
            try
            {
                File.Delete(backup.Path);
            }
            catch (IOException)
            {
                // A failed cleanup must not prevent the application from starting.
            }
        }
    }

    private static BackupResult ValidateDatabase(string path)
    {
        try
        {
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString());
            connection.Open();

            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(
                    integrity.ExecuteScalar() as string,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BackupResult.Failure("备份文件完整性检查未通过。");
            }

            using var version = connection.CreateCommand();
            version.CommandText = "SELECT Value FROM AppMetadata WHERE Key = 'SchemaVersion';";
            if (!int.TryParse(version.ExecuteScalar() as string, out _))
            {
                return BackupResult.Failure("备份文件缺少数据库版本信息。");
            }

            return BackupResult.Success("备份文件校验通过。");
        }
        catch (Exception exception) when (
            exception is SqliteException
            or IOException
            or UnauthorizedAccessException)
        {
            return BackupResult.Failure($"备份文件无法读取：{exception.Message}");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
