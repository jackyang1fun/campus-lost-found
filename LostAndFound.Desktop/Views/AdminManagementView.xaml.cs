using System.Windows;
using System.Windows.Controls;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Admin;
using LostAndFound.App.Features.Listings;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class AdminManagementView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _administrator;
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;
    private AdminUserRow? _selectedUser;
    private PostingSummary? _selectedPosting;

    public AdminManagementView(
        ServiceContext services,
        UserAccount administrator,
        Action<string, bool> notify,
        Func<string, string, Task<bool>> confirm)
    {
        _services = services;
        _administrator = administrator;
        _notify = notify;
        _confirm = confirm;
        InitializeComponent();
        LoadData();
    }

    private void LoadData()
    {
        var users = _services.Admin.GetUsers(_administrator);
        UsersGrid.ItemsSource = users.Select(user => new UserRow(
            user,
            user.DisplayName,
            user.CampusCardNumber,
            user.Contact,
            user.Role == UserRole.Administrator ? "管理员" : "普通用户",
            FormatStatus(user.Status))).ToArray();
        AuditGrid.ItemsSource = _services.Admin
            .GetRecentAuditLogs(_administrator)
            .Select(log => new AuditRow(
                log.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                log.ActorName,
                UiFormat.AuditAction(log.Action),
                $"{log.TargetType} #{log.TargetId}",
                log.Reason ?? string.Empty))
            .ToArray();
        ContentGrid.ItemsSource = _services.Listings
            .GetAllPostings(_administrator)
            .Select(posting => new ContentRow(
                posting,
                UiFormat.PostingTypeLabel(posting.Type),
                posting.Title,
                posting.PublisherName,
                UiFormat.PostingStatusLabel(posting.Status),
                posting.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))
            .ToArray();

        var stats = _services.Admin.GetStats(_administrator);
        UserCountText.Text = stats.TotalUsers.ToString();
        PublicCountText.Text = stats.PublicPostings.ToString();
        DisputeCountText.Text = stats.OpenDisputes.ToString();
        _selectedUser = null;
        _selectedPosting = null;
    }

    private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedUser = (UsersGrid.SelectedItem as UserRow)?.Source;
        ReasonBox.Clear();
    }

    private async void BanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser is null)
        {
            _notify("请先选择一个用户。", true);
            return;
        }

        var confirmed = await _confirm(
            "封禁用户",
            $"确认封禁“{_selectedUser.DisplayName}”吗？该用户将无法继续登录。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Admin.SetUserStatus(
            _administrator,
            _selectedUser.Id,
            UserStatus.Banned,
            ReasonBox.Text);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadData();
        }
    }

    private void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser is null)
        {
            _notify("请先选择一个用户。", true);
            return;
        }

        var result = _services.Admin.SetUserStatus(
            _administrator,
            _selectedUser.Id,
            UserStatus.Active,
            "管理员恢复账号");
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadData();
        }
    }

    private void ContentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPosting = (ContentGrid.SelectedItem as ContentRow)?.Source;
        DeleteReasonBox.Clear();
    }

    private async void DeletePostingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPosting is null)
        {
            _notify("请先选择一条信息。", true);
            return;
        }

        var confirmed = await _confirm(
            "删除信息",
            $"确认删除“{_selectedPosting.Title}”吗？公开列表将立即隐藏该信息，操作会保留审计记录。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Listings.DeletePosting(
            _administrator,
            _selectedPosting.Id,
            DeleteReasonBox.Text);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadData();
        }
    }

    private static string FormatStatus(UserStatus status) =>
        status switch
        {
            UserStatus.Active => "正常",
            UserStatus.Deactivated => "已停用",
            UserStatus.Banned => "已封禁",
            _ => status.ToString()
        };

    private sealed record UserRow(
        AdminUserRow Source,
        string DisplayName,
        string CampusCardNumber,
        string Contact,
        string RoleText,
        string StatusText);

    private sealed record AuditRow(
        string TimeText,
        string Actor,
        string Action,
        string Target,
        string Reason);

    private sealed record ContentRow(
        PostingSummary Source,
        string TypeText,
        string Title,
        string Publisher,
        string StatusText,
        string UpdatedText);
}
