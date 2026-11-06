using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Listings;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class ShellView : UserControl
{
    private readonly ServiceContext _services;
    private UserAccount? _currentUser;
    private readonly Dictionary<string, Button> _navigationButtons = [];
    private string _activeNavigation = string.Empty;

    public ShellView(ServiceContext services, UserAccount? currentUser)
    {
        _services = services;
        _currentUser = currentUser;
        InitializeComponent();

        SidebarUserName.Text = currentUser?.DisplayName ?? "访客模式";
        SidebarUserRole.Text = currentUser is null
            ? "仅可浏览公开信息"
            : currentUser.Role == UserRole.Administrator
                ? "系统管理员"
                : "普通用户";
        HeaderStatus.Text = currentUser is null
            ? "游客"
            : currentUser.Role == UserRole.Administrator ? "管理员" : "普通用户";

        BuildNavigation();
    }

    public event EventHandler? LogoutRequested;

    public event EventHandler? LoginRequested;

    public event EventHandler<AppMessageEventArgs>? MessageRequested;

    public event EventHandler? DataRestored;

    public void ShowDashboard()
    {
        if (_currentUser is null)
        {
            ShowGuestBrowse();
            return;
        }

        var view = new DashboardView(
            _services,
            _currentUser,
            Navigate,
            Notify);
        ShowPage(
            "dashboard",
            "工作台",
            "查看当前进度并快速进入常用功能",
            view);
    }

    public void ShowGuestBrowse()
    {
        var view = new BrowseView(
            _services,
            currentUser: null,
            Notify,
            () => LoginRequested?.Invoke(this, EventArgs.Empty),
            OpenChatFromBrowse);
        ShowPage(
            "browse",
            "失物广场",
            "浏览已审核通过的失物与招领信息",
            view);
    }

    private void BuildNavigation()
    {
        if (_currentUser is null)
        {
            AddNavigation("browse", "浏览公开信息", ShowGuestBrowse);
            AddNavigation("login", "登录 / 注册", () => LoginRequested?.Invoke(this, EventArgs.Empty));
            return;
        }

        AddNavigation("dashboard", "工作台", ShowDashboard);
        AddNavigation("browse", "失物广场", ShowBrowser);

        if (_currentUser.Role == UserRole.User)
        {
            AddNavigation("publish", "发布信息", () => ShowPublish());
            AddNavigation("my-postings", "我的发布", ShowMyPostings);
        }
        else
        {
            AddNavigation("review", "审核中心", ShowReview);
            AddNavigation("disputes", "争议处理", ShowDisputes);
            AddNavigation("management", "用户与日志", ShowAdminManagement);
            AddNavigation("backups", "备份恢复", ShowBackups);
        }

        AddNavigation("claims", "认领管理", ShowClaims);
        AddNavigation("messages", "消息中心", ShowMessages);
        AddNavigation("account", "个人中心", ShowAccount);

        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(8, 12, 8, 10),
            Background = (Brush)FindResource("SidebarSelectedBrush")
        };
        NavItems.Children.Add(divider);
        AddNavigation("logout", "退出登录", () => LogoutRequested?.Invoke(this, EventArgs.Empty));
    }

    private void AddNavigation(string key, string label, Action action)
    {
        var button = new Button
        {
            Style = (Style)FindResource("NavButtonStyle"),
            Tag = key,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        var text = new TextBlock { Text = label };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SidebarTextBrush");
        button.Content = text;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            SetActiveNavigation(key);
            action();
        };

        _navigationButtons[key] = button;
        NavItems.Children.Add(button);
    }

    private void Navigate(string destination)
    {
        SetActiveNavigation(destination);
        switch (destination)
        {
            case "dashboard":
                ShowDashboard();
                break;
            case "browse":
                ShowBrowser();
                break;
            case "publish":
                ShowPublish();
                break;
            case "my-postings":
                ShowMyPostings();
                break;
            case "review":
                ShowReview();
                break;
            case "disputes":
                ShowDisputes();
                break;
            case "management":
                ShowAdminManagement();
                break;
            case "backups":
                ShowBackups();
                break;
            case "claims":
                ShowClaims();
                break;
            case "messages":
                ShowMessages();
                break;
            case "account":
                ShowAccount();
                break;
        }
    }

    private void ShowBrowser()
    {
        var view = new BrowseView(
            _services,
            _currentUser,
            Notify,
            () => LoginRequested?.Invoke(this, EventArgs.Empty),
            OpenChatFromBrowse);
        ShowPage(
            "browse",
            "失物广场",
            "搜索公开信息，查看关联候选并发起认领",
            view);
    }

    private void ShowPublish(PostingSummary? existingPosting = null)
    {
        if (_currentUser is null)
        {
            LoginRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var view = new PublishView(
            _services,
            _currentUser,
            Notify,
            () => Navigate("my-postings"),
            () => Navigate("dashboard"),
            existingPosting);
        ShowPage(
            "publish",
            existingPosting is null ? "发布信息" : "编辑信息",
            existingPosting is null
                ? "填写失物或招领信息并提交审核"
                : "修改后将重新进入审核队列",
            view);
    }

    private void ShowMyPostings()
    {
        if (_currentUser is null)
        {
            LoginRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var view = new MyPostingsView(
            _services,
            _currentUser,
            Notify,
            () => Navigate("publish"),
            posting => ShowPublish(posting),
            Confirm);
        ShowPage(
            "my-postings",
            "我的发布",
            "查看本人发布记录与审核结果",
            view);
    }

    private void ShowReview()
    {
        if (_currentUser?.Role != UserRole.Administrator)
        {
            return;
        }

        var view = new ReviewView(
            _services,
            _currentUser,
            Notify,
            Confirm);
        ShowPage(
            "review",
            "审核中心",
            "检查待发布内容并完成审核",
            view);
    }

    private void ShowDisputes()
    {
        if (_currentUser?.Role != UserRole.Administrator)
        {
            return;
        }

        var view = new DisputeAdminView(
            _services,
            _currentUser,
            Notify,
            Confirm);
        ShowPage(
            "disputes",
            "争议处理",
            "核对真实身份与聊天记录并形成处理结论",
            view);
    }

    private void ShowAdminManagement()
    {
        if (_currentUser?.Role != UserRole.Administrator)
        {
            return;
        }

        var view = new AdminManagementView(
            _services,
            _currentUser,
            Notify,
            Confirm);
        ShowPage(
            "management",
            "用户与日志",
            "管理账号状态并查看关键操作记录",
            view);
    }

    private void ShowBackups()
    {
        if (_currentUser?.Role != UserRole.Administrator)
        {
            return;
        }

        var view = new BackupView(
            _services,
            _currentUser,
            Notify,
            Confirm,
            () => DataRestored?.Invoke(this, EventArgs.Empty));
        ShowPage(
            "backups",
            "备份恢复",
            "创建数据备份，验证完整性并安全恢复",
            view);
    }

    private void ShowClaims()
    {
        if (_currentUser is null)
        {
            LoginRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var view = new ClaimsView(
            _services,
            _currentUser,
            Notify,
            Confirm);
        ShowPage(
            "claims",
            "认领管理",
            "管理认领状态、取消和交接确认",
            view);
    }

    private void ShowMessages()
    {
        if (_currentUser is null)
        {
            LoginRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var view = new MessagesView(
            _services,
            _currentUser,
            Notify,
            OpenChat);
        ShowPage(
            "messages",
            "消息中心",
            "集中查看认领双方的匿名会话",
            view);
    }

    private void ShowAccount()
    {
        if (_currentUser is null)
        {
            LoginRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var view = new AccountView(
            _services,
            _currentUser,
            updatedUser =>
            {
                _currentUser = updatedUser;
                SidebarUserName.Text = updatedUser.DisplayName;
            },
            () => LogoutRequested?.Invoke(this, EventArgs.Empty),
            Notify,
            Confirm);
        ShowPage(
            "account",
            "个人中心",
            "管理个人资料、密码和账号状态",
            view);
    }

    private void OpenChatFromBrowse(long claimId)
    {
        if (_currentUser is null)
        {
            return;
        }

        OpenChat(claimId);
    }

    private void OpenChat(long claimId)
    {
        if (_currentUser is null)
        {
            return;
        }

        var view = new ChatView(
            _services,
            _currentUser,
            claimId,
            Notify,
            Confirm,
            () => Navigate("messages"));
        ShowPage(
            "messages",
            "匿名会话",
            "仅认领双方可见，管理员只为争议处理进行审计",
            view);
    }

    private void ShowPage(string key, string title, string subtitle, UIElement content)
    {
        SetActiveNavigation(key);
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        PageHost.Content = content;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                var first = FindFirstFocusable(content);
                if (first is not null)
                {
                    first.Focus();
                    Keyboard.Focus(first);
                }
            });
    }

    private void SetActiveNavigation(string key)
    {
        _activeNavigation = key;
        foreach (var pair in _navigationButtons)
        {
            var selected = string.Equals(pair.Key, key, StringComparison.Ordinal);
            pair.Value.Background = selected
                ? (Brush)FindResource("SidebarSelectedBrush")
                : Brushes.Transparent;
            pair.Value.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void Notify(string message, bool isError) =>
        MessageRequested?.Invoke(this, new AppMessageEventArgs(message, isError));

    private Task<bool> Confirm(string title, string message)
    {
        var window = Window.GetWindow(this) as MainWindow;
        return window?.ConfirmAsync(title, message)
               ?? Task.FromResult(false);
    }

    private static Control? FindFirstFocusable(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Control control &&
                control.Focusable &&
                control.IsEnabled &&
                control.Visibility == Visibility.Visible)
            {
                return control;
            }

            var nested = FindFirstFocusable(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}

public sealed class AppMessageEventArgs(string message, bool isError) : EventArgs
{
    public string Message { get; } = message;

    public bool IsError { get; } = isError;
}
