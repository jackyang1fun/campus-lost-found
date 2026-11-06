using LostAndFound.App.Domain;
using LostAndFound.App.Features.Admin;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.Tests.Features.Admin;

public sealed class AdminServiceTests : IDisposable
{
    private const string Password = "Campus#2026";
    private readonly string _tempDirectory;
    private readonly AuthService _auth;
    private readonly AdminService _adminService;

    public AdminServiceTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "lost-and-found-admin-tests",
            Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_tempDirectory);
        paths.EnsureDirectories();
        var database = new SqliteDatabase(paths.DatabasePath);
        new DatabaseInitializer(database).Initialize();
        var audit = new AuditService();
        _auth = new AuthService(database, new LocalCampusCardVerifier(), audit);
        _adminService = new AdminService(database, audit);
    }

    [Fact]
    public void AdministratorCanBanAndReactivateUser()
    {
        var admin = RegisterAdministrator("ADMIN-6001");
        var user = Assert.IsType<UserAccount>(
            _auth.RegisterUser(CreateRegistration("CARD-6001")).User);

        var banned = _adminService.SetUserStatus(
            admin,
            user.Id,
            UserStatus.Banned,
            "发布违规内容");
        Assert.True(banned.Succeeded);
        Assert.False(_auth.Login(new LoginRequest("CARD-6001", Password)).Succeeded);

        var activated = _adminService.SetUserStatus(
            admin,
            user.Id,
            UserStatus.Active,
            "申诉通过");
        Assert.True(activated.Succeeded);
        Assert.True(_auth.Login(new LoginRequest("CARD-6001", Password)).Succeeded);
    }

    [Fact]
    public void AdministratorCannotBanSelf()
    {
        var admin = RegisterAdministrator("ADMIN-6002");

        var result = _adminService.SetUserStatus(
            admin,
            admin.Id,
            UserStatus.Banned,
            "测试");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AdministratorCanReadStatisticsAndAuditLogs()
    {
        var admin = RegisterAdministrator("ADMIN-6003");
        Assert.IsType<UserAccount>(
            _auth.RegisterUser(CreateRegistration("CARD-6003")).User);

        var stats = _adminService.GetStats(admin);
        var logs = _adminService.GetRecentAuditLogs(admin);

        Assert.True(stats.TotalUsers >= 2);
        Assert.NotEmpty(logs);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private UserAccount RegisterAdministrator(string campusCardNumber) =>
        Assert.IsType<UserAccount>(
            _auth.RegisterAdministrator(CreateRegistration(campusCardNumber)).User);

    private static RegistrationRequest CreateRegistration(string campusCardNumber) =>
        new(
            campusCardNumber,
            "管理员测试",
            "test@example.edu",
            Password,
            PrivacyAccepted: true);
}
