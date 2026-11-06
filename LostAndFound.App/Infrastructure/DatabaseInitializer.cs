using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Infrastructure;

public sealed class DatabaseInitializer
{
    private const int CurrentSchemaVersion = 4;
    private readonly SqliteDatabase _database;

    public DatabaseInitializer(SqliteDatabase database)
    {
        _database = database;
    }

    public void Initialize()
    {
        using var connection = _database.OpenConnection();

        using (var journalCommand = connection.CreateCommand())
        {
            journalCommand.CommandText = "PRAGMA journal_mode = WAL;";
            journalCommand.ExecuteScalar();
        }

        using var transaction = connection.BeginTransaction();
        CreateMetadataTable(connection, transaction);
        var version = ReadSchemaVersion(connection, transaction);

        if (version == 0)
        {
            CreateVersionOneSchema(connection, transaction);
            version = 1;
        }

        if (version == 1)
        {
            CreateVersionTwoSchema(connection, transaction);
            version = 2;
        }

        if (version == 2)
        {
            CreateVersionThreeSchema(connection, transaction);
            version = 3;
        }

        if (version == 3)
        {
            CreateVersionFourSchema(connection, transaction);
            version = 4;
        }

        if (version != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"不支持的数据库版本 {version}，当前应用仅支持版本 {CurrentSchemaVersion}。");
        }

        SetSchemaVersion(connection, transaction, CurrentSchemaVersion);
        transaction.Commit();
    }

    private static void CreateMetadataTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AppMetadata (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static int ReadSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Value FROM AppMetadata WHERE Key = 'SchemaVersion';";
        var value = command.ExecuteScalar() as string;
        return int.TryParse(value, out var version) ? version : 0;
    }

    private static void SetSchemaVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AppMetadata (Key, Value)
            VALUES ('SchemaVersion', $version)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$version", version.ToString());
        command.ExecuteNonQuery();
    }

    private static void CreateVersionOneSchema(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CampusCardNumber TEXT NOT NULL UNIQUE COLLATE NOCASE,
                DisplayName TEXT NOT NULL,
                Contact TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL CHECK (Role IN ('User', 'Administrator')),
                Status TEXT NOT NULL CHECK (Status IN ('Active', 'Deactivated', 'Banned')),
                PrivacyAcceptedAtUtc TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE INDEX IX_Users_Role_Status ON Users (Role, Status);

            CREATE TABLE AuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ActorUserId INTEGER NULL,
                Action TEXT NOT NULL,
                TargetType TEXT NOT NULL,
                TargetId TEXT NOT NULL,
                Reason TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ActorUserId) REFERENCES Users (Id)
            );

            CREATE INDEX IX_AuditLogs_CreatedAtUtc ON AuditLogs (CreatedAtUtc);
            """;
        command.ExecuteNonQuery();
    }

    private static void CreateVersionTwoSchema(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0, 1))
            );

            INSERT INTO Categories (Name) VALUES
                ('电子产品'),
                ('证件'),
                ('书籍'),
                ('衣物'),
                ('水杯'),
                ('钥匙'),
                ('其他');

            CREATE TABLE Postings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Type TEXT NOT NULL CHECK (Type IN ('Lost', 'Found')),
                Title TEXT NOT NULL,
                CategoryId INTEGER NOT NULL,
                OccurredAtUtc TEXT NOT NULL,
                Location TEXT NOT NULL,
                Description TEXT NOT NULL,
                ImagePath TEXT NULL,
                ImageAltText TEXT NULL,
                Contact TEXT NOT NULL,
                PublisherUserId INTEGER NOT NULL,
                Status TEXT NOT NULL CHECK (
                    Status IN (
                        'PendingReview',
                        'Approved',
                        'Rejected',
                        'Resolved',
                        'Cancelled',
                        'Deleted')),
                ReviewReason TEXT NULL,
                Version INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES Categories (Id),
                FOREIGN KEY (PublisherUserId) REFERENCES Users (Id)
            );

            CREATE INDEX IX_Postings_Public
                ON Postings (Status, Type, CategoryId, CreatedAtUtc);
            CREATE INDEX IX_Postings_Publisher
                ON Postings (PublisherUserId, CreatedAtUtc);

            CREATE TABLE PostingReviewLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PostingId INTEGER NOT NULL,
                AdminUserId INTEGER NOT NULL,
                Action TEXT NOT NULL CHECK (Action IN ('Approved', 'Rejected', 'Deleted')),
                Reason TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (PostingId) REFERENCES Postings (Id),
                FOREIGN KEY (AdminUserId) REFERENCES Users (Id)
            );

            CREATE INDEX IX_PostingReviewLogs_PostingId
                ON PostingReviewLogs (PostingId, CreatedAtUtc);
            """;
        command.ExecuteNonQuery();
    }

    private static void CreateVersionThreeSchema(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE Claims (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PostingId INTEGER NOT NULL,
                InitiatorUserId INTEGER NOT NULL,
                PublisherUserId INTEGER NOT NULL,
                Status TEXT NOT NULL CHECK (
                    Status IN (
                        'Negotiating',
                        'Completed',
                        'Cancelled',
                        'Disputed',
                        'DisputeClosed')),
                ClosureReason TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                ClosedAtUtc TEXT NULL,
                FOREIGN KEY (PostingId) REFERENCES Postings (Id),
                FOREIGN KEY (InitiatorUserId) REFERENCES Users (Id),
                FOREIGN KEY (PublisherUserId) REFERENCES Users (Id),
                CHECK (InitiatorUserId <> PublisherUserId)
            );

            CREATE INDEX IX_Claims_Participant
                ON Claims (InitiatorUserId, PublisherUserId, UpdatedAtUtc);
            CREATE INDEX IX_Claims_Posting
                ON Claims (PostingId, Status);
            CREATE UNIQUE INDEX UX_Claims_OpenParticipantPosting
                ON Claims (PostingId, InitiatorUserId)
                WHERE Status IN ('Negotiating', 'Disputed');

            CREATE TABLE ClaimMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClaimId INTEGER NOT NULL,
                SenderUserId INTEGER NULL,
                Content TEXT NOT NULL,
                IsSystem INTEGER NOT NULL DEFAULT 0 CHECK (IsSystem IN (0, 1)),
                SentAtUtc TEXT NOT NULL,
                FOREIGN KEY (ClaimId) REFERENCES Claims (Id),
                FOREIGN KEY (SenderUserId) REFERENCES Users (Id)
            );

            CREATE INDEX IX_ClaimMessages_Claim
                ON ClaimMessages (ClaimId, SentAtUtc);
            """;
        command.ExecuteNonQuery();
    }

    private static void CreateVersionFourSchema(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE Disputes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClaimId INTEGER NOT NULL UNIQUE,
                OpenedByUserId INTEGER NOT NULL,
                ReasonCategory TEXT NOT NULL,
                Description TEXT NOT NULL,
                Status TEXT NOT NULL CHECK (Status IN ('Open', 'NeedMoreInfo', 'Closed')),
                AdminConclusion TEXT NULL,
                ClosedByUserId INTEGER NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                ClosedAtUtc TEXT NULL,
                FOREIGN KEY (ClaimId) REFERENCES Claims (Id),
                FOREIGN KEY (OpenedByUserId) REFERENCES Users (Id),
                FOREIGN KEY (ClosedByUserId) REFERENCES Users (Id)
            );

            CREATE INDEX IX_Disputes_Status
                ON Disputes (Status, UpdatedAtUtc);
            """;
        command.ExecuteNonQuery();
    }
}
