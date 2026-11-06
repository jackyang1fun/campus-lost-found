using LostAndFound.App.Domain;
using LostAndFound.App.Infrastructure;
using LostAndFound.App.Ports;
using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Features.Auth;

public sealed class AuthService
{
    private readonly SqliteDatabase _database;
    private readonly IIdentityVerifier _identityVerifier;
    private readonly AuditService _auditService;

    public AuthService(
        SqliteDatabase database,
        IIdentityVerifier identityVerifier,
        AuditService auditService)
    {
        _database = database;
        _identityVerifier = identityVerifier;
        _auditService = auditService;
    }

    public bool HasAdministrator()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Users WHERE Role = 'Administrator');";
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    public AuthResult RegisterUser(RegistrationRequest request) =>
        Register(request, UserRole.User);

    public AuthResult RegisterAdministrator(RegistrationRequest request) =>
        Register(request, UserRole.Administrator);

    public AuthResult Login(LoginRequest request)
    {
        if (!_identityVerifier.TryNormalize(request.CampusCardNumber, out var campusCardNumber))
        {
            return AuthResult.Failure("校园卡号格式不正确。");
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CampusCardNumber, DisplayName, Contact, PasswordHash, Role, Status
            FROM Users
            WHERE CampusCardNumber = $campusCardNumber COLLATE NOCASE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$campusCardNumber", campusCardNumber);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return AuthResult.Failure("校园卡号或密码不正确。");
        }

        var passwordHash = reader.GetString(reader.GetOrdinal("PasswordHash"));
        if (!PasswordHasher.Verify(request.Password, passwordHash))
        {
            return AuthResult.Failure("校园卡号或密码不正确。");
        }

        var status = Enum.Parse<UserStatus>(reader.GetString(reader.GetOrdinal("Status")));
        if (status != UserStatus.Active)
        {
            return AuthResult.Failure(
                status == UserStatus.Banned
                    ? "该账号已被封禁，请联系管理员。"
                    : "该账号已停用。");
        }

        var user = new UserAccount(
            reader.GetInt64(reader.GetOrdinal("Id")),
            reader.GetString(reader.GetOrdinal("CampusCardNumber")),
            reader.GetString(reader.GetOrdinal("DisplayName")),
            reader.GetString(reader.GetOrdinal("Contact")),
            Enum.Parse<UserRole>(reader.GetString(reader.GetOrdinal("Role"))),
            status);

        return AuthResult.Success(user, "登录成功。");
    }

    public AuthResult UpdateProfile(UserAccount user, string displayName, string contact)
    {
        var normalizedDisplayName = displayName.Trim();
        var normalizedContact = contact.Trim();
        if (normalizedDisplayName.Length is < 1 or > 50)
        {
            return AuthResult.Failure("显示名称长度必须为 1 至 50 个字符。");
        }

        if (normalizedContact.Length is < 1 or > 100)
        {
            return AuthResult.Failure("联系方式长度必须为 1 至 100 个字符。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Users
                SET DisplayName = $displayName, Contact = $contact
                WHERE Id = $userId AND Status = 'Active';
                """;
            update.Parameters.AddWithValue("$displayName", normalizedDisplayName);
            update.Parameters.AddWithValue("$contact", normalizedContact);
            update.Parameters.AddWithValue("$userId", user.Id);
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return AuthResult.Failure("账号不存在或当前不能修改资料。");
            }
        }

        _auditService.Write(
            connection,
            transaction,
            user.Id,
            "ProfileUpdated",
            "User",
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null,
            DateTimeOffset.UtcNow);
        transaction.Commit();

        return AuthResult.Success(
            user with
            {
                DisplayName = normalizedDisplayName,
                Contact = normalizedContact
            },
            "个人资料已更新。");
    }

    public AuthResult ChangePassword(UserAccount user, string currentPassword, string newPassword)
    {
        if (newPassword.Length < 8)
        {
            return AuthResult.Failure("新密码至少需要 8 个字符。");
        }

        using var connection = _database.OpenConnection();
        string? passwordHash;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT PasswordHash FROM Users WHERE Id = $userId LIMIT 1;";
            read.Parameters.AddWithValue("$userId", user.Id);
            passwordHash = read.ExecuteScalar() as string;
        }

        if (passwordHash is null || !PasswordHasher.Verify(currentPassword, passwordHash))
        {
            return AuthResult.Failure("当前密码不正确。");
        }

        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Users SET PasswordHash = $hash WHERE Id = $userId;";
            update.Parameters.AddWithValue("$hash", PasswordHasher.Hash(newPassword));
            update.Parameters.AddWithValue("$userId", user.Id);
            update.ExecuteNonQuery();
        }

        _auditService.Write(
            connection,
            transaction,
            user.Id,
            "PasswordChanged",
            "User",
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null,
            DateTimeOffset.UtcNow);
        transaction.Commit();
        return AuthResult.Success(user, "密码已更新。");
    }

    public AuthResult DeactivateAccount(UserAccount user)
    {
        if (user.Role == UserRole.Administrator)
        {
            return AuthResult.Failure("管理员账号不能自助注销。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Users
                SET Status = 'Deactivated'
                WHERE Id = $userId AND Status = 'Active';
                """;
            update.Parameters.AddWithValue("$userId", user.Id);
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return AuthResult.Failure("账号当前不能注销。");
            }
        }

        _auditService.Write(
            connection,
            transaction,
            user.Id,
            "AccountDeactivated",
            "User",
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null,
            DateTimeOffset.UtcNow);
        transaction.Commit();
        return AuthResult.Success(user, "账号已停用。");
    }

    private AuthResult Register(RegistrationRequest request, UserRole role)
    {
        if (!request.PrivacyAccepted)
        {
            return AuthResult.Failure("请先阅读并同意个人信息用途说明。");
        }

        if (!_identityVerifier.TryNormalize(request.CampusCardNumber, out var campusCardNumber))
        {
            return AuthResult.Failure("校园卡号需为 6 至 24 位字母、数字或连字符。");
        }

        var displayName = request.DisplayName.Trim();
        if (displayName.Length is < 1 or > 50)
        {
            return AuthResult.Failure("显示名称长度必须为 1 至 50 个字符。");
        }

        var contact = request.Contact.Trim();
        if (contact.Length is < 1 or > 100)
        {
            return AuthResult.Failure("联系方式长度必须为 1 至 100 个字符。");
        }

        if (request.Password.Length < 8)
        {
            return AuthResult.Failure("密码至少需要 8 个字符。");
        }

        using var connection = _database.OpenConnection();
        if (role == UserRole.Administrator && HasAnyAdministrator(connection))
        {
            return AuthResult.Failure("管理员已经初始化，不能再次创建。");
        }

        if (UserExists(connection, campusCardNumber))
        {
            return AuthResult.Failure("该校园卡号已绑定其他账号。");
        }

        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        try
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Users (
                    CampusCardNumber,
                    DisplayName,
                    Contact,
                    PasswordHash,
                    Role,
                    Status,
                    PrivacyAcceptedAtUtc,
                    CreatedAtUtc)
                VALUES (
                    $campusCardNumber,
                    $displayName,
                    $contact,
                    $passwordHash,
                    $role,
                    'Active',
                    $privacyAcceptedAtUtc,
                    $createdAtUtc);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$campusCardNumber", campusCardNumber);
            insert.Parameters.AddWithValue("$displayName", displayName);
            insert.Parameters.AddWithValue("$contact", contact);
            insert.Parameters.AddWithValue("$passwordHash", PasswordHasher.Hash(request.Password));
            insert.Parameters.AddWithValue("$role", role.ToString());
            insert.Parameters.AddWithValue("$privacyAcceptedAtUtc", now);
            insert.Parameters.AddWithValue("$createdAtUtc", now);

            var userId = Convert.ToInt64(insert.ExecuteScalar());
            _auditService.Write(
                connection,
                transaction,
                userId,
                role == UserRole.Administrator ? "AdministratorCreated" : "UserRegistered",
                "User",
                userId.ToString(),
                null,
                DateTimeOffset.UtcNow);

            transaction.Commit();
            var user = new UserAccount(
                userId,
                campusCardNumber,
                displayName,
                contact,
                role,
                UserStatus.Active);

            return AuthResult.Success(
                user,
                role == UserRole.Administrator ? "管理员初始化成功。" : "注册成功。");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            transaction.Rollback();
            return AuthResult.Failure("该校园卡号已绑定其他账号。");
        }
    }

    private static bool HasAnyAdministrator(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Users WHERE Role = 'Administrator');";
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static bool UserExists(SqliteConnection connection, string campusCardNumber)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM Users
                WHERE CampusCardNumber = $campusCardNumber COLLATE NOCASE);
            """;
        command.Parameters.AddWithValue("$campusCardNumber", campusCardNumber);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

}
