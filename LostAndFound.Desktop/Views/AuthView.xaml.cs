using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Auth;

namespace LostAndFound.Desktop.Views;

public partial class AuthView : UserControl
{
    private readonly AuthService _auth;
    private bool _administratorSetup;

    public AuthView(AuthService auth)
    {
        _auth = auth;
        InitializeComponent();
        if (_auth.HasAdministrator())
        {
            InitializeAdminButton.Content = "管理员已初始化";
            InitializeAdminButton.IsEnabled = false;
            System.Windows.Automation.AutomationProperties.SetHelpText(
                InitializeAdminButton,
                "系统中已经存在管理员账号，请直接登录。");
        }
        Loaded += (_, _) => LoginCardNumber.Focus();
    }

    public event EventHandler<AuthenticatedEventArgs>? Authenticated;

    public event EventHandler? GuestRequested;

    private void LoginTab_Click(object sender, RoutedEventArgs e) =>
        ShowLogin();

    private void RegisterTab_Click(object sender, RoutedEventArgs e) =>
        ShowRegistration(administrator: false);

    private void InitializeAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (_auth.HasAdministrator())
        {
            ShowError("管理员已经初始化，请使用管理员账号登录。", register: false);
            return;
        }

        ShowRegistration(administrator: true);
    }

    private void GuestButton_Click(object sender, RoutedEventArgs e) =>
        GuestRequested?.Invoke(this, EventArgs.Empty);

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorMessage.Text = string.Empty;
        var result = _auth.Login(
            new LoginRequest(LoginCardNumber.Text, LoginPassword.Password));
        if (!result.Succeeded || result.User is null)
        {
            ShowError(result.Message, register: false);
            LoginPassword.SelectAll();
            LoginPassword.Focus();
            return;
        }

        Authenticated?.Invoke(this, new AuthenticatedEventArgs(result.User, result.Message));
    }

    private void LoginPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoginButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        RegisterError.Text = string.Empty;
        if (!string.Equals(
                RegisterPassword.Password,
                ConfirmPassword.Password,
                StringComparison.Ordinal))
        {
            RegisterError.Text = "两次输入的密码不一致。";
            ConfirmPassword.Focus();
            return;
        }

        var request = new RegistrationRequest(
            RegisterCardNumber.Text,
            RegisterDisplayName.Text,
            RegisterContact.Text,
            RegisterPassword.Password,
            PrivacyAccepted.IsChecked == true);
        var result = _administratorSetup
            ? _auth.RegisterAdministrator(request)
            : _auth.RegisterUser(request);

        if (!result.Succeeded || result.User is null)
        {
            RegisterError.Text = result.Message;
            return;
        }

        if (_administratorSetup)
        {
            Authenticated?.Invoke(this, new AuthenticatedEventArgs(result.User, result.Message));
            return;
        }

        ShowLogin();
        ErrorMessage.Text = result.Message;
        ErrorMessage.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        LoginCardNumber.Text = result.User.CampusCardNumber;
        LoginPassword.Focus();
    }

    private void ShowLogin()
    {
        _administratorSetup = false;
        LoginPanel.Visibility = Visibility.Visible;
        RegisterPanel.Visibility = Visibility.Collapsed;
        FormHeading.Text = "欢迎回来";
        FormSubtitle.Text = "登录后继续发布、认领或管理信息。";
        RegisterTabButton.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        RegisterTabButton.Background = System.Windows.Media.Brushes.Transparent;
        LoginTabButton.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        LoginTabButton.Background = (System.Windows.Media.Brush)FindResource("AccentSoftBrush");
        LoginButton.IsDefault = true;
        RegisterButton.IsDefault = false;
        LoginCardNumber.Focus();
    }

    private void ShowRegistration(bool administrator)
    {
        _administratorSetup = administrator;
        LoginPanel.Visibility = Visibility.Collapsed;
        RegisterPanel.Visibility = Visibility.Visible;
        RegisterButton.Content = administrator ? "初始化管理员" : "创建普通用户";
        FormHeading.Text = administrator ? "初始化管理员" : "创建账号";
        FormSubtitle.Text = administrator
            ? "该账号负责信息审核、争议处理和备份操作。"
            : "校园卡号仅用于本地演示验证，不会连接学校真实系统。";
        LoginTabButton.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        LoginTabButton.Background = System.Windows.Media.Brushes.Transparent;
        RegisterTabButton.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        RegisterTabButton.Background = (System.Windows.Media.Brush)FindResource("AccentSoftBrush");
        LoginButton.IsDefault = false;
        RegisterButton.IsDefault = true;
        RegisterError.Text = string.Empty;
        RegisterCardNumber.Focus();
    }

    private void ShowError(string message, bool register)
    {
        if (register)
        {
            RegisterError.Text = message;
        }
        else
        {
            ErrorMessage.Text = message;
            ErrorMessage.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        }
    }
}

public sealed class AuthenticatedEventArgs(UserAccount user, string message) : EventArgs
{
    public UserAccount User { get; } = user;

    public string Message { get; } = message;
}
