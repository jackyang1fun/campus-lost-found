using System.Text;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Infrastructure;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("用法：DemoAccountSeeder <应用数据目录> [--with-sample-content]");
    return 1;
}

var paths = new AppPaths(args[0]);
paths.EnsureDirectories();

var database = new SqliteDatabase(paths.DatabasePath);
new DatabaseInitializer(database).Initialize();
var audit = new AuditService();
var auth = new AuthService(
    database,
    new LocalCampusCardVerifier(),
    audit);

var accounts = new[]
{
    new DemoAccount("ADMIN-1001", "系统管理员", "admin@demo.local", "Admin#2026", UserRole.Administrator),
    new DemoAccount("STU-1001", "失主小林", "lin@demo.local", "Demo#2026", UserRole.User),
    new DemoAccount("STU-1002", "拾获人小周", "zhou@demo.local", "Demo#2026", UserRole.User),
    new DemoAccount("STU-1003", "认领人小陈", "chen@demo.local", "Demo#2026", UserRole.User),
    new DemoAccount("STU-1004", "普通同学小吴", "wu@demo.local", "Demo#2026", UserRole.User)
};

var reportLines = new List<string>
{
    "Lost and Found System 演示账号",
    $"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
    $"数据库：{paths.DatabasePath}",
    string.Empty,
    "注意：以下密码仅用于本地课程演示，请勿用于其他账号。",
    string.Empty
};

foreach (var account in accounts)
{
    var registration = new RegistrationRequest(
        account.CampusCardNumber,
        account.DisplayName,
        account.Contact,
        account.Password,
        PrivacyAccepted: true);

    var result = account.Role == UserRole.Administrator
        ? auth.RegisterAdministrator(registration)
        : auth.RegisterUser(registration);

    if (result.Succeeded)
    {
        reportLines.Add(
            $"[新增] {(account.Role == UserRole.Administrator ? "管理员" : "普通用户")}  " +
            $"{account.CampusCardNumber} / {account.Password}  ({account.DisplayName})");
        continue;
    }

    var existingLogin = auth.Login(
        new LoginRequest(account.CampusCardNumber, account.Password));
    if (existingLogin.Succeeded)
    {
        reportLines.Add(
            $"[已存在] {(account.Role == UserRole.Administrator ? "管理员" : "普通用户")}  " +
            $"{account.CampusCardNumber} / {account.Password}  ({account.DisplayName})");
        continue;
    }

    reportLines.Add(
        $"[冲突] {account.CampusCardNumber}：{result.Message} 原密码未改动，请不要使用本次密码。");
}

if (auth.HasAdministrator() &&
    reportLines.All(line => !line.Contains("ADMIN-1001 / Admin#2026", StringComparison.Ordinal)))
{
    reportLines.Add(
        "提示：数据库中已存在另一个管理员账号。请使用你原先创建的管理员账号，或告知我进行单独处理。");
}

if (args.Length == 2 &&
    string.Equals(args[1], "--with-sample-content", StringComparison.OrdinalIgnoreCase))
{
    var listings = new ListingService(database, new LocalImageStore(paths), audit);
    var claims = new ClaimService(
        database,
        audit,
        new ClaimCompletionCoordinator(database, audit));
    SeedSampleContent(auth, listings, claims, reportLines);
}

reportLines.AddRange(
[
    string.Empty,
    "推荐测试顺序：",
    "1. ADMIN-1001 登录，如有待审核信息可执行通过或驳回。",
    "2. STU-1001 登录，发布一条失物信息。",
    "3. 退出后使用 ADMIN-1001 审核通过。",
    "4. STU-1002 登录，发布分类、地点和时间相近的招领信息。",
    "5. 退出后使用 ADMIN-1001 审核通过。",
    "6. STU-1003 登录，在公开信息中查看关联候选并发起认领。",
    "7. 在“我的认领”中打开匿名会话并发送消息。",
    "8. STU-1001 登录，在“我的认领”中确认交接完成。",
    string.Empty,
    "普通用户统一密码：Demo#2026",
    "管理员密码：Admin#2026"
]);

var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
var outputPath = Path.Combine(desktop, "LostAndFound测试账号.txt");
File.WriteAllText(outputPath, string.Join(Environment.NewLine, reportLines), new UTF8Encoding(false));

Console.WriteLine(outputPath);
foreach (var line in reportLines)
{
    Console.WriteLine(line);
}

return 0;

static void SeedSampleContent(
    AuthService auth,
    ListingService listings,
    ClaimService claims,
    List<string> reportLines)
{
    var admin = auth.Login(new LoginRequest("ADMIN-1001", "Admin#2026")).User;
    var publisher = auth.Login(new LoginRequest("STU-1001", "Demo#2026")).User;
    var finder = auth.Login(new LoginRequest("STU-1002", "Demo#2026")).User;
    var claimant = auth.Login(new LoginRequest("STU-1003", "Demo#2026")).User;
    var idleUser = auth.Login(new LoginRequest("STU-1004", "Demo#2026")).User;
    if (admin is null || publisher is null || finder is null || claimant is null || idleUser is null)
    {
        reportLines.Add("演示内容未生成：示例账号登录失败。");
        return;
    }

    var category = listings.GetCategories().First(item => item.Name == "电子产品");
    var createdAny = false;
    var lost = EnsurePosting(
        listings,
        publisher,
        admin,
        category.Id,
        PostingType.Lost,
        "黑色折叠雨伞",
        "图书馆一楼",
        "一把黑色折叠雨伞，银白色手柄，伞套上有绿色挂绳。",
        DateTimeOffset.Now.AddHours(-3));
    createdAny |= lost is not null;

    EnsurePosting(
        listings,
        finder,
        admin,
        category.Id,
        PostingType.Found,
        "图书馆拾获黑色雨伞",
        "图书馆一楼入口",
        "拾到一把黑色折叠雨伞，银白色手柄。",
        DateTimeOffset.Now.AddHours(-2));

    EnsurePosting(
        listings,
        idleUser,
        admin,
        listings.GetCategories().First(item => item.Name == "证件").Id,
        PostingType.Lost,
        "校园卡待审核示例",
        "第二教学楼",
        "一张校园卡，姓名部分已遮挡。",
        DateTimeOffset.Now.AddHours(-1),
        approve: false);

    if (lost is not null &&
        !claims.GetClaimsForUser(idleUser.Id)
            .Any(item => item.PostingId == lost.Id))
    {
        var claim = claims.StartClaim(idleUser, lost.Id);
        if (claim.Succeeded && claim.ClaimId is not null)
        {
            claims.SendMessage(
                idleUser,
                claim.ClaimId.Value,
                "你好，我好像在图书馆入口看到过这把雨伞，手柄确实是银白色的。");
            createdAny = true;
        }
    }

    reportLines.Add(
        createdAny
            ? "已生成示例失物、招领、待审核信息和一条匿名会话。"
            : "示例内容已经存在，未重复生成。");
}

static PostingSummary? EnsurePosting(
    ListingService listings,
    UserAccount publisher,
    UserAccount? admin,
    int categoryId,
    PostingType type,
    string title,
    string location,
    string description,
    DateTimeOffset occurredAt,
    bool approve = true)
{
    var existing = listings
        .GetOwnPostings(publisher.Id)
        .FirstOrDefault(posting => posting.Title == title);
    if (existing is not null)
    {
        return existing.Status == PostingStatus.Approved ? existing : null;
    }

    var created = listings.CreatePosting(
        publisher,
        new CreatePostingRequest(
            type,
            title,
            categoryId,
            occurredAt,
            location,
            description,
            publisher.Contact,
            ImageSourcePath: null,
            ImageAltText: null));
    if (!created.Succeeded || created.PostingId is null)
    {
        return null;
    }

    if (approve && admin is not null)
    {
        listings.ReviewPosting(admin, created.PostingId.Value, approve: true, reason: null);
    }

    return approve
        ? listings.GetPublicPostings(title, categoryId)
            .FirstOrDefault(posting => posting.Id == created.PostingId.Value)
        : null;
}

internal sealed record DemoAccount(
    string CampusCardNumber,
    string DisplayName,
    string Contact,
    string Password,
    UserRole Role);
