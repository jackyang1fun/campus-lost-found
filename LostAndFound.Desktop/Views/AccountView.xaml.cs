using System.Windows;
using System.Windows.Controls;
using LostAndFound.App.Domain;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class AccountView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _user;
    private readonly Action<UserAccount> _profileUpdated;
    private readonly Action _logout;
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;

    public AccountView(
        ServiceContext services,
        UserAccount user,
        Action<UserAccount> profileUpdated,
        Action logout,
        Action<string, bool> notify,
        Func<string, string, Task<bool>> confirm)
    {
        _services = services;
        _user = user;
        _profileUpdated = profileUpdated;
        _logout = logout;
        _notify = notify;
        _confirm = confirm;
        InitializeComponent();

        CampusCardText.Text = user.CampusCardNumber;
        DisplayNameBox.Text = user.DisplayName;
        ContactBox.Text = user.Contact;
        AccountNameText.Text = user.DisplayName;
        AccountRoleText.Text = user.Role == UserRole.Administrator
            ? "系统管理员"
            : "普通用户";
        AvatarLetter.Text = user.DisplayName.Length > 0
            ? user.DisplayName[..1]
            : "用";
        DangerCard.Visibility = user.Role == UserRole.Administrator
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _services.Auth.UpdateProfile(
            _user,
            DisplayNameBox.Text,
            ContactBox.Text);
        if (!result.Succeeded || result.User is null)
        {
            ProfileStatusText.Text = result.Message;
            return;
        }

        ProfileStatusText.Text = string.Empty;
        AccountNameText.Text = result.User.DisplayName;
        AvatarLetter.Text = result.User.DisplayName[..1];
        _profileUpdated(result.User);
        _notify(result.Message, false);
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(
                NewPasswordBox.Password,
                ConfirmPasswordBox.Password,
                StringComparison.Ordinal))
        {
            PasswordStatusText.Text = "两次输入的新密码不一致。";
            return;
        }

        var result = _services.Auth.ChangePassword(
            _user,
            CurrentPasswordBox.Password,
            NewPasswordBox.Password);
        PasswordStatusText.Text = result.Succeeded ? string.Empty : result.Message;
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
        }
    }

    private async void DeactivateButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await _confirm(
            "注销账号",
            "确定注销当前账号吗？注销后将不能继续登录，历史争议记录会保留。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Auth.DeactivateAccount(_user);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            _logout();
        }
    }
}
