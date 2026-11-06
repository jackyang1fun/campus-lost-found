using LostAndFound.App.Domain;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.Tests.Features.Claims;

public sealed class ClaimServiceTests : IDisposable
{
    private const string Password = "Campus#2026";
    private readonly string _tempDirectory;
    private readonly AppPaths _paths;
    private readonly AuthService _auth;
    private readonly ListingService _listings;
    private readonly ClaimService _claims;

    public ClaimServiceTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "lost-and-found-claim-tests",
            Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_tempDirectory);
        _paths.EnsureDirectories();
        var database = new SqliteDatabase(_paths.DatabasePath);
        new DatabaseInitializer(database).Initialize();
        var audit = new AuditService();
        _auth = new AuthService(database, new LocalCampusCardVerifier(), audit);
        _listings = new ListingService(database, new LocalImageStore(_paths), audit);
        _claims = new ClaimService(
            database,
            audit,
            new ClaimCompletionCoordinator(database, audit));
    }

    [Fact]
    public void ClaimCreatesAnonymousConversationAndParticipantCanChat()
    {
        var (publisher, _) = RegisterUsers("CARD-5001", "CARD-5002");
        var claimant = LoginUser("CARD-5002");
        var admin = RegisterAdministrator("ADMIN-5001");
        var posting = CreateAndApprove(publisher, admin, "黑色雨伞");

        var started = _claims.StartClaim(claimant, posting.Id);

        Assert.True(started.Succeeded, started.Message);
        var conversation = Assert.IsType<ClaimConversation>(
            _claims.GetConversation(claimant, started.ClaimId!.Value));
        Assert.Equal("拾获人B", conversation.MyAlias);
        Assert.Equal("失主A", conversation.CounterpartyAlias);
        Assert.Contains(conversation.Messages, message => message.IsSystem);

        var sent = _claims.SendMessage(claimant, started.ClaimId.Value, "我在图书馆等你。");
        Assert.True(sent.Succeeded, sent.Message);

        var publisherConversation = Assert.IsType<ClaimConversation>(
            _claims.GetConversation(publisher, started.ClaimId.Value));
        Assert.Contains(
            publisherConversation.Messages,
            message => message.SenderAlias == "拾获人B" &&
                       message.Content == "我在图书馆等你。");
    }

    [Fact]
    public void NonParticipantCannotSendMessage()
    {
        var (publisher, _) = RegisterUsers("CARD-5011", "CARD-5012");
        var claimant = LoginUser("CARD-5012");
        var outsider = LoginUser("CARD-5013");
        var admin = RegisterAdministrator("ADMIN-5011");
        var posting = CreateAndApprove(publisher, admin, "水杯");
        var claim = _claims.StartClaim(claimant, posting.Id);

        var result = _claims.SendMessage(outsider, claim.ClaimId!.Value, "不应发送");

        Assert.False(result.Succeeded);
        Assert.Contains("参与者", result.Message);
    }

    [Fact]
    public void PublisherCompletionIsAtomicAndClosesOtherClaims()
    {
        var (publisher, _) = RegisterUsers("CARD-5021", "CARD-5022");
        var firstClaimant = LoginUser("CARD-5022");
        var secondClaimant = LoginUser("CARD-5023");
        var admin = RegisterAdministrator("ADMIN-5021");
        var posting = CreateAndApprove(publisher, admin, "校园卡");

        var first = _claims.StartClaim(firstClaimant, posting.Id);
        var second = _claims.StartClaim(secondClaimant, posting.Id);
        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);

        var duplicate = _claims.StartClaim(firstClaimant, posting.Id);
        Assert.False(duplicate.Succeeded);

        var unauthorizedCompletion = _claims.CompleteClaim(firstClaimant, first.ClaimId!.Value);
        Assert.False(unauthorizedCompletion.Succeeded);

        var completed = _claims.CompleteClaim(publisher, first.ClaimId.Value);
        Assert.True(completed.Succeeded, completed.Message);

        var firstConversation = Assert.IsType<ClaimConversation>(
            _claims.GetConversation(firstClaimant, first.ClaimId.Value));
        Assert.Equal(ClaimStatus.Completed, firstConversation.Status);
        Assert.False(firstConversation.CanSendMessage);

        var secondConversation = Assert.IsType<ClaimConversation>(
            _claims.GetConversation(secondClaimant, second.ClaimId!.Value));
        Assert.Equal(ClaimStatus.Cancelled, secondConversation.Status);
        Assert.Contains("其他认领", secondConversation.ClosureReason);

        Assert.Empty(_listings.GetPublicPostings(null, null));
        var ownPosting = Assert.Single(_listings.GetOwnPostings(publisher.Id));
        Assert.Equal(PostingStatus.Resolved, ownPosting.Status);
    }

    [Fact]
    public void UserCannotClaimOwnPosting()
    {
        var (publisher, _) = RegisterUsers("CARD-5031", "CARD-5032");
        var admin = RegisterAdministrator("ADMIN-5031");
        var posting = CreateAndApprove(publisher, admin, "钥匙");

        var result = _claims.StartClaim(publisher, posting.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("自己", result.Message);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private (UserAccount Publisher, UserAccount Claimant) RegisterUsers(
        string publisherCard,
        string claimantCard)
    {
        RegisterUser(claimantCard);
        RegisterUser("CARD-5013");
        RegisterUser("CARD-5023");
        return (
            RegisterUser(publisherCard),
            LoginUser(claimantCard));
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
                "用于认领测试的物品描述。",
                "test@example.edu",
                ImageSourcePath: null,
                ImageAltText: null));
        Assert.True(created.Succeeded, created.Message);
        Assert.True(
            _listings.ReviewPosting(admin, created.PostingId!.Value, true, null).Succeeded);
        return _listings
            .GetPublicPostings(title, null)
            .Single(posting => posting.Id == created.PostingId.Value);
    }

    private UserAccount RegisterUser(string campusCardNumber) =>
        Assert.IsType<UserAccount>(
            _auth.RegisterUser(CreateRegistration(campusCardNumber)).User);

    private UserAccount RegisterAdministrator(string campusCardNumber) =>
        Assert.IsType<UserAccount>(
            _auth.RegisterAdministrator(CreateRegistration(campusCardNumber)).User);

    private UserAccount LoginUser(string campusCardNumber) =>
        Assert.IsType<UserAccount>(
            _auth.Login(new LoginRequest(campusCardNumber, Password)).User);

    private static RegistrationRequest CreateRegistration(string campusCardNumber) =>
        new(
            campusCardNumber,
            campusCardNumber,
            "test@example.edu",
            Password,
            PrivacyAccepted: true);
}
