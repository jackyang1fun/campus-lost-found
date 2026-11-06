using LostAndFound.App.Domain;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.Tests.Features.Auth;

public sealed class AuthServiceTests : IDisposable
{
    private const string Password = "Campus#2026";
    private readonly string _tempDirectory;
    private readonly SqliteDatabase _database;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "lost-and-found-tests",
            Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_tempDirectory);
        paths.EnsureDirectories();
        _database = new SqliteDatabase(paths.DatabasePath);
        new DatabaseInitializer(_database).Initialize();
        _authService = new AuthService(
            _database,
            new LocalCampusCardVerifier(),
            new AuditService());
    }

    [Fact]
    public void RegisterAndLogin_WorkWithNormalizedCampusCardNumber()
    {
        var registration = _authService.RegisterUser(CreateRegistration("card-1001"));

        Assert.True(registration.Succeeded);
        Assert.NotNull(registration.User);

        var login = _authService.Login(new LoginRequest(" CARD-1001 ", Password));

        Assert.True(login.Succeeded);
        Assert.Equal("CARD-1001", login.User?.CampusCardNumber);
    }

    [Fact]
    public void Register_RejectsDuplicateCampusCardNumber()
    {
        var first = _authService.RegisterUser(CreateRegistration("CARD-1002"));
        var duplicate = _authService.RegisterUser(CreateRegistration("card-1002"));

        Assert.True(first.Succeeded);
        Assert.False(duplicate.Succeeded);
        Assert.Contains("已绑定", duplicate.Message);
    }

    [Fact]
    public void Register_StoresPasswordOnlyAsHash()
    {
        Assert.True(_authService.RegisterUser(CreateRegistration("CARD-1003")).Succeeded);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PasswordHash
            FROM Users
            WHERE CampusCardNumber = 'CARD-1003';
            """;
        var storedHash = Assert.IsType<string>(command.ExecuteScalar());

        Assert.NotEqual(Password, storedHash);
        Assert.DoesNotContain(Password, storedHash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify(Password, storedHash));
    }

    [Fact]
    public void Login_RejectsBannedAccount()
    {
        Assert.True(_authService.RegisterUser(CreateRegistration("CARD-1004")).Succeeded);

        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE Users
                SET Status = 'Banned'
                WHERE CampusCardNumber = 'CARD-1004';
                """;
            command.ExecuteNonQuery();
        }

        var login = _authService.Login(new LoginRequest("CARD-1004", Password));

        Assert.False(login.Succeeded);
        Assert.Contains("封禁", login.Message);
    }

    [Fact]
    public void AdministratorCanOnlyBeInitializedOnce()
    {
        var first = _authService.RegisterAdministrator(CreateRegistration("ADMIN-01"));
        var second = _authService.RegisterAdministrator(CreateRegistration("ADMIN-02"));

        Assert.True(first.Succeeded);
        Assert.True(_authService.HasAdministrator());
        Assert.False(second.Succeeded);
        Assert.Contains("已经初始化", second.Message);
    }

    [Fact]
    public void ProfileAndPasswordCanBeUpdated()
    {
        var registration = _authService.RegisterUser(CreateRegistration("CARD-1005"));
        var user = Assert.IsType<UserAccount>(registration.User);

        var profile = _authService.UpdateProfile(
            user,
            "新的显示名称",
            "new@example.edu");
        Assert.True(profile.Succeeded);
        Assert.Equal("新的显示名称", profile.User?.DisplayName);

        var password = _authService.ChangePassword(user, Password, "NewPassword#2026");
        Assert.True(password.Succeeded);
        Assert.False(_authService.Login(new LoginRequest("CARD-1005", Password)).Succeeded);
        Assert.True(
            _authService.Login(new LoginRequest("CARD-1005", "NewPassword#2026")).Succeeded);
    }

    [Fact]
    public void DeactivatedUserCannotLogIn()
    {
        var user = Assert.IsType<UserAccount>(
            _authService.RegisterUser(CreateRegistration("CARD-1006")).User);

        Assert.True(_authService.DeactivateAccount(user).Succeeded);

        var login = _authService.Login(new LoginRequest("CARD-1006", Password));
        Assert.False(login.Succeeded);
        Assert.Contains("停用", login.Message);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static RegistrationRequest CreateRegistration(string campusCardNumber) =>
        new(
            campusCardNumber,
            "测试用户",
            "test@example.edu",
            Password,
            PrivacyAccepted: true);
}
