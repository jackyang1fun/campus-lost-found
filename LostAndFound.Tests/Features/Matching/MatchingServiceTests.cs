using LostAndFound.App.Domain;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Features.Matching;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.Tests.Features.Matching;

public sealed class MatchingServiceTests : IDisposable
{
    private const string Password = "Campus#2026";
    private readonly string _tempDirectory;
    private readonly AppPaths _paths;
    private readonly AuthService _auth;
    private readonly ListingService _listings;
    private readonly MatchingService _matching;

    public MatchingServiceTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "lost-and-found-matching-tests",
            Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_tempDirectory);
        _paths.EnsureDirectories();
        var database = new SqliteDatabase(_paths.DatabasePath);
        new DatabaseInitializer(database).Initialize();
        var audit = new AuditService();
        _auth = new AuthService(database, new LocalCampusCardVerifier(), audit);
        _listings = new ListingService(database, new LocalImageStore(_paths), audit);
        _matching = new MatchingService(_listings, new RuleBasedMatchPolicy());
    }

    [Fact]
    public void FindsOppositeTypeCandidateWithExplainableReasons()
    {
        var user = RegisterUser("CARD-4001");
        var admin = RegisterAdministrator("ADMIN-4001");
        var category = _listings.GetCategories().First();
        var occurredAt = DateTimeOffset.Now.AddHours(-2);

        var lost = CreateAndApprove(
            user,
            admin,
            PostingType.Lost,
            "黑色雨伞",
            "图书馆一楼",
            "黑色折叠雨伞，银白色手柄",
            category.Id,
            occurredAt);
        CreateAndApprove(
            user,
            admin,
            PostingType.Found,
            "折叠雨伞",
            "图书馆一楼入口",
            "拾到一把黑色雨伞，银白色手柄",
            category.Id,
            occurredAt.AddMinutes(15));
        CreateAndApprove(
            user,
            admin,
            PostingType.Lost,
            "另一把雨伞",
            "图书馆一楼",
            "黑色雨伞",
            category.Id,
            occurredAt);

        var suggestions = _matching.GetSuggestions(lost);

        var candidate = Assert.Single(suggestions);
        Assert.Equal(PostingType.Found, candidate.Candidate.Type);
        Assert.Contains(candidate.Reasons, reason => reason.Contains("分类"));
        Assert.Contains(candidate.Reasons, reason => reason.Contains("地点"));
        Assert.True(candidate.Reasons.Count >= 2);
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
        PostingType type,
        string title,
        string location,
        string description,
        int categoryId,
        DateTimeOffset occurredAt)
    {
        var created = _listings.CreatePosting(
            publisher,
            new CreatePostingRequest(
                type,
                title,
                categoryId,
                occurredAt,
                location,
                description,
                "test@example.edu",
                ImageSourcePath: null,
                ImageAltText: null));
        Assert.True(created.Succeeded, created.Message);
        Assert.True(
            _listings.ReviewPosting(admin, created.PostingId!.Value, true, null).Succeeded);

        return _listings
            .GetPublicPostings(title, categoryId)
            .Single(posting => posting.Id == created.PostingId.Value);
    }

    private UserAccount RegisterUser(string campusCardNumber) =>
        Assert.IsType<UserAccount>(
            _auth.RegisterUser(CreateRegistration(campusCardNumber)).User);

    private UserAccount RegisterAdministrator(string campusCardNumber) =>
        Assert.IsType<UserAccount>(
            _auth.RegisterAdministrator(CreateRegistration(campusCardNumber)).User);

    private static RegistrationRequest CreateRegistration(string campusCardNumber) =>
        new(
            campusCardNumber,
            "测试用户",
            "test@example.edu",
            Password,
            PrivacyAccepted: true);
}
