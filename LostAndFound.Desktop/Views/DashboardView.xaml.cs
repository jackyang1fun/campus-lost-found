using System.Windows;
using System.Windows.Controls;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Listings;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class DashboardView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _user;
    private readonly Action<string> _navigate;
    private readonly Action<string, bool> _notify;

    public DashboardView(
        ServiceContext services,
        UserAccount user,
        Action<string> navigate,
        Action<string, bool> notify)
    {
        _services = services;
        _user = user;
        _navigate = navigate;
        _notify = notify;
        InitializeComponent();
        LoadDashboard();
    }

    private void LoadDashboard()
    {
        var postings = _services.Listings.GetOwnPostings(_user.Id);
        var claims = _services.Claims.GetClaimsForUser(_user.Id);
        var publicPostings = _services.Listings.GetPublicPostings(null, null);
        var pending = _user.Role == UserRole.Administrator
            ? _services.Listings.GetPendingPostings(_user).Count
            : postings.Count(posting => posting.Status == PostingStatus.PendingReview);

        GreetingText.Text = $"你好，{_user.DisplayName}";
        HeadlineText.Text = _user.Role == UserRole.Administrator
            ? "把好审核入口，让每次认领都有记录。"
            : "让每一次寻找，都能更清楚地抵达。";
        DescriptionText.Text = _user.Role == UserRole.Administrator
            ? "审核待发布内容、浏览公开记录，并在独立的消息中心处理协商沟通。"
            : "发布失物或招领信息，系统会帮助你发现可能的关联记录。";
        PrimaryActionButton.Content = _user.Role == UserRole.Administrator
            ? "处理待审核信息"
            : "发布新信息";

        MetricsList.ItemsSource = new[]
        {
            new MetricItem(
                _user.Role == UserRole.Administrator ? "待审核" : "我的发布",
                pending.ToString(),
                _user.Role == UserRole.Administrator ? "等待处理的信息" : "本人发布的总数"),
            new MetricItem(
                "进行中认领",
                claims.Count(claim => claim.Status == ClaimStatus.Negotiating).ToString(),
                "正在协商的认领"),
            new MetricItem(
                "消息会话",
                claims.Count.ToString(),
                "可进入消息中心查看"),
            new MetricItem(
                "公开信息",
                publicPostings.Count.ToString(),
                "当前可浏览的记录")
        };

        var recent = publicPostings
            .Take(5)
            .Select(posting => new RecentPostingRow(
                UiFormat.PostingTypeLabel(posting.Type),
                posting.Title,
                $"{posting.CategoryName} · {posting.Location}",
                UiFormat.RelativeDate(posting.CreatedAt)))
            .ToArray();
        RecentPostingsList.ItemsSource = recent;
        EmptyRecentText.Visibility = recent.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var activeClaims = claims
            .Where(claim => claim.Status == ClaimStatus.Negotiating)
            .Take(4)
            .Select(claim => new ActiveClaimRow(
                claim.PostingTitle,
                $"{claim.CounterpartyAlias} · {UiFormat.RelativeDate(claim.UpdatedAt)}"))
            .ToArray();
        ActiveClaimsList.ItemsSource = activeClaims;
        EmptyClaimsText.Visibility = activeClaims.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PrimaryAction_Click(object sender, RoutedEventArgs e) =>
        _navigate(_user.Role == UserRole.Administrator ? "review" : "publish");

    private void BrowseAction_Click(object sender, RoutedEventArgs e) =>
        _navigate("browse");

    private void MessagesAction_Click(object sender, RoutedEventArgs e) =>
        _navigate("messages");

    private sealed record MetricItem(string Caption, string Value, string Description);

    private sealed record RecentPostingRow(
        string TypeLabel,
        string Title,
        string Meta,
        string TimeText);

    private sealed record ActiveClaimRow(string Title, string Detail);
}
