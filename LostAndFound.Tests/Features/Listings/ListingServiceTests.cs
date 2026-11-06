using LostAndFound.App.Domain;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.Tests.Features.Listings;

public sealed class ListingServiceTests : IDisposable
{
    private const string Password = "Campus#2026";
    private readonly string _tempDirectory;
    private readonly AppPaths _paths;
    private readonly AuthService _authService;
    private readonly ListingService _listingService;

    public ListingServiceTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "lost-and-found-listing-tests",
            Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_tempDirectory);
        _paths.EnsureDirectories();

        var database = new SqliteDatabase(_paths.DatabasePath);
        new DatabaseInitializer(database).Initialize();
        var auditService = new AuditService();
        _authService = new AuthService(
            database,
            new LocalCampusCardVerifier(),
            auditService);
        _listingService = new ListingService(
            database,
            new LocalImageStore(_paths),
            auditService);
    }

    [Fact]
    public void ApprovedPostingBecomesVisibleInPublicSearch()
    {
        var user = RegisterUser("CARD-2001");
        var admin = RegisterAdministrator("ADMIN-2001");
        var category = _listingService.GetCategories().First();

        var created = _listingService.CreatePosting(
            user,
            CreateRequest(category.Id, "黑色雨伞", "图书馆一楼"));

        Assert.True(created.Succeeded);
        Assert.Empty(_listingService.GetPublicPostings(null, null));
        Assert.Single(_listingService.GetPendingPostings(admin));

        var reviewed = _listingService.ReviewPosting(
            admin,
            created.PostingId!.Value,
            approve: true,
            reason: null);

        Assert.True(reviewed.Succeeded);
        var publicResults = _listingService.GetPublicPostings("图书馆", null);
        var posting = Assert.Single(publicResults);
        Assert.Equal("黑色雨伞", posting.Title);
        Assert.Equal(PostingStatus.Approved, posting.Status);
    }

    [Fact]
    public void NonAdministratorCannotReviewPosting()
    {
        var user = RegisterUser("CARD-2002");
        var category = _listingService.GetCategories().First();
        var created = _listingService.CreatePosting(
            user,
            CreateRequest(category.Id, "校园卡", "食堂"));

        var reviewed = _listingService.ReviewPosting(
            user,
            created.PostingId!.Value,
            approve: true,
            reason: null);

        Assert.False(reviewed.Succeeded);
        Assert.Contains("管理员", reviewed.Message);
    }

    [Fact]
    public void RejectingPostingRequiresReason()
    {
        var user = RegisterUser("CARD-2003");
        var admin = RegisterAdministrator("ADMIN-2003");
        var category = _listingService.GetCategories().First();
        var created = _listingService.CreatePosting(
            user,
            CreateRequest(category.Id, "无主水杯", "教学楼"));

        var rejected = _listingService.ReviewPosting(
            admin,
            created.PostingId!.Value,
            approve: false,
            reason: null);

        Assert.False(rejected.Succeeded);
        Assert.Contains("理由", rejected.Message);
    }

    [Fact]
    public void ImageUploadRequiresAlternativeText()
    {
        var user = RegisterUser("CARD-2004");
        var category = _listingService.GetCategories().First();
        var request = CreateRequest(category.Id, "钥匙", "东门")
            with
        {
            ImageSourcePath = @"C:\not-used-in-validation.jpg",
            ImageAltText = null
        };

        var result = _listingService.CreatePosting(user, request);

        Assert.False(result.Succeeded);
        Assert.Contains("替代文本", result.Message);
    }

    [Fact]
    public void PublisherCanEditCancelAndResolvePosting()
    {
        var user = RegisterUser("CARD-2005");
        var admin = RegisterAdministrator("ADMIN-2005");
        var category = _listingService.GetCategories().First();
        var created = _listingService.CreatePosting(
            user,
            CreateRequest(category.Id, "旧标题", "旧地点"));
        var postingId = created.PostingId!.Value;

        var updated = _listingService.UpdateOwnPosting(
            user,
            postingId,
            CreateRequest(category.Id, "更新后的标题", "新地点"),
            removeImage: false);
        Assert.True(updated.Succeeded);
        Assert.Contains(
            _listingService.GetOwnPostings(user.Id),
            posting => posting.Title == "更新后的标题" &&
                       posting.Status == PostingStatus.PendingReview);

        Assert.True(_listingService.ReviewPosting(admin, postingId, true, null).Succeeded);
        Assert.True(_listingService.MarkOwnResolved(user, postingId).Succeeded);
        Assert.Equal(
            PostingStatus.Resolved,
            _listingService.GetOwnPostings(user.Id).Single().Status);

        var second = _listingService.CreatePosting(
            user,
            CreateRequest(category.Id, "准备撤销的信息", "食堂"));
        Assert.True(_listingService.CancelOwnPosting(user, second.PostingId!.Value).Succeeded);
    }

    [Fact]
    public void AdministratorCanDeletePostingWithAuditReason()
    {
        var user = RegisterUser("CARD-2006");
        var admin = RegisterAdministrator("ADMIN-2006");
        var category = _listingService.GetCategories().First();
        var created = _listingService.CreatePosting(
            user,
            CreateRequest(category.Id, "违规内容", "教学楼"));
        Assert.True(created.Succeeded);

        var deleted = _listingService.DeletePosting(
            admin,
            created.PostingId!.Value,
            "测试违规内容");

        Assert.True(deleted.Succeeded);
        Assert.Equal(
            PostingStatus.Deleted,
            _listingService.GetAllPostings(admin).Single().Status);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private UserAccount RegisterUser(string campusCardNumber)
    {
        var result = _authService.RegisterUser(CreateRegistration(campusCardNumber));
        return Assert.IsType<UserAccount>(result.User);
    }

    private UserAccount RegisterAdministrator(string campusCardNumber)
    {
        var result = _authService.RegisterAdministrator(CreateRegistration(campusCardNumber));
        return Assert.IsType<UserAccount>(result.User);
    }

    private static RegistrationRequest CreateRegistration(string campusCardNumber) =>
        new(
            campusCardNumber,
            "测试用户",
            "test@example.edu",
            Password,
            PrivacyAccepted: true);

    private static CreatePostingRequest CreateRequest(
        int categoryId,
        string title,
        string location) =>
        new(
            PostingType.Lost,
            title,
            categoryId,
            DateTimeOffset.Now.AddHours(-2),
            location,
            "用于测试的物品描述。",
            "test@example.edu",
            ImageSourcePath: null,
            ImageAltText: null);
}
