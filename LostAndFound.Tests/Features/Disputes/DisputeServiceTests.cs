using LostAndFound.App.Domain;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Disputes;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.Tests.Features.Disputes;

public sealed class DisputeServiceTests : IDisposable
{
    private const string Password = "Campus#2026";
    private readonly string _tempDirectory;
    private readonly AuthService _auth;
    private readonly ListingService _listings;
    private readonly ClaimService _claims;
    private readonly DisputeService _disputes;

    public DisputeServiceTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "lost-and-found-dispute-tests",
            Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_tempDirectory);
        paths.EnsureDirectories();
        var database = new SqliteDatabase(paths.DatabasePath);
        new DatabaseInitializer(database).Initialize();
        var audit = new AuditService();
        _auth = new AuthService(database, new LocalCampusCardVerifier(), audit);
        _listings = new ListingService(database, new LocalImageStore(paths), audit);
        _claims = new ClaimService(
            database,
            audit,
            new ClaimCompletionCoordinator(database, audit));
        _disputes = new DisputeService(database, audit);
    }

    [Fact]
    public void ParticipantCanOpenDisputeAndAdministratorCanCloseIt()
    {
        var publisher = RegisterUser("CARD-7001");
        var claimant = RegisterUser("CARD-7002");
        var admin = RegisterAdministrator("ADMIN-7001");
        var posting = CreateAndApprove(publisher, admin, "雨伞");
        var claim = _claims.StartClaim(claimant, posting.Id);
        Assert.True(claim.Succeeded, claim.Message);

        var opened = _disputes.OpenDispute(
            claimant,
            claim.ClaimId!.Value,
            "物品与描述不符",
            "实际物品与发布描述存在明显差异。");
        Assert.True(opened.Succeeded, opened.Message);

        var claimState = Assert.Single(_claims.GetClaimsForUser(claimant.Id));
        Assert.Equal(ClaimStatus.Disputed, claimState.Status);

        var summary = Assert.Single(_disputes.GetDisputes(admin));
        var details = Assert.IsType<DisputeDetails>(
            _disputes.GetDisputeDetails(admin, summary.Id));
        Assert.Equal("CARD-7001", details.Publisher.CampusCardNumber);
        Assert.Equal("CARD-7002", details.Initiator.CampusCardNumber);

        var resolved = _disputes.ResolveDispute(
            admin,
            summary.Id,
            needMoreInformation: false,
            "双方描述不一致，争议记录保留。");
        Assert.True(resolved.Succeeded, resolved.Message);

        var closedState = Assert.Single(_claims.GetClaimsForUser(claimant.Id));
        Assert.Equal(ClaimStatus.DisputeClosed, closedState.Status);
    }

    [Fact]
    public void NonParticipantCannotOpenDispute()
    {
        var publisher = RegisterUser("CARD-7011");
        var claimant = RegisterUser("CARD-7012");
        var outsider = RegisterUser("CARD-7013");
        var admin = RegisterAdministrator("ADMIN-7011");
        var posting = CreateAndApprove(publisher, admin, "水杯");
        var claim = _claims.StartClaim(claimant, posting.Id);

        var result = _disputes.OpenDispute(
            outsider,
            claim.ClaimId!.Value,
            "其他问题",
            "这不是该认领的参与者。");

        Assert.False(result.Succeeded);
        Assert.Contains("参与者", result.Message);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private PostingSummary CreateAndApprove(
        UserAccount publisher,
        UserAccount admin,
        string title)
    {
        var category = _listings.GetCategories().First();
        var created = _listings.CreatePosting(
            publisher,
            new CreatePostingRequest(
                PostingType.Lost,
                title,
                category.Id,
                DateTimeOffset.Now.AddHours(-1),
                "图书馆",
                "用于争议流程测试的物品描述。",
                "test@example.edu",
                null,
                null));
        Assert.True(created.Succeeded, created.Message);
        Assert.True(
            _listings.ReviewPosting(admin, created.PostingId!.Value, true, null).Succeeded);
        return _listings.GetPublicPostings(title, null).Single();
    }

    private UserAccount RegisterUser(string card) =>
        Assert.IsType<UserAccount>(
            _auth.RegisterUser(CreateRegistration(card)).User);

    private UserAccount RegisterAdministrator(string card) =>
        Assert.IsType<UserAccount>(
            _auth.RegisterAdministrator(CreateRegistration(card)).User);

    private static RegistrationRequest CreateRegistration(string card) =>
        new(card, card, "test@example.edu", Password, PrivacyAccepted: true);
}
