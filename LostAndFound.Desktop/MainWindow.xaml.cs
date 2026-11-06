using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LostAndFound.App.Domain;
using LostAndFound.Desktop.Shared;
using LostAndFound.Desktop.Views;

namespace LostAndFound.Desktop;

public partial class MainWindow : Window
{
    private readonly ServiceContext _services;
    private readonly DispatcherTimer _toastTimer;
    private TaskCompletionSource<bool>? _confirmation;

    public MainWindow(ServiceContext services)
    {
        _services = services;
        InitializeComponent();

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastPanel.Visibility = Visibility.Collapsed;
        };

        ShowAuthentication();
    }

    public void ShowAuthentication()
    {
        var view = new AuthView(_services.Auth);
        view.Authenticated += (_, args) =>
        {
            ShowWorkspace(args.User);
            ShowToast(args.Message, isError: false);
        };
        view.GuestRequested += (_, _) => ShowWorkspace(currentUser: null);

        RootHost.Content = view;
    }

    public void ShowWorkspace(UserAccount? currentUser)
    {
        var shell = new ShellView(_services, currentUser);
        shell.LogoutRequested += (_, _) => ShowAuthentication();
        shell.LoginRequested += (_, _) => ShowAuthentication();
        shell.MessageRequested += (_, args) => ShowToast(args.Message, args.IsError);
        shell.DataRestored += (_, _) =>
        {
            ShowAuthentication();
            ShowToast("数据已恢复，请重新登录。", isError: false);
        };
        RootHost.Content = shell;

        if (currentUser is null)
        {
            shell.ShowGuestBrowse();
        }
        else
        {
            shell.ShowDashboard();
        }
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "确认",
        string cancelText = "取消")
    {
        if (_confirmation is not null)
        {
            return false;
        }

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogConfirmButton.Content = confirmText;
        DialogCancelButton.Content = cancelText;
        DialogLayer.Visibility = Visibility.Visible;
        var previousFocus = Keyboard.FocusedElement;
        DialogCancelButton.Focus();

        _confirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            return await _confirmation.Task;
        }
        finally
        {
            DialogLayer.Visibility = Visibility.Collapsed;
            _confirmation = null;
            if (previousFocus is UIElement element && element.IsVisible)
            {
                Keyboard.Focus(element);
            }
        }
    }

    public void ShowToast(string message, bool isError)
    {
        ToastText.Text = message;
        ToastText.Foreground = (Brush)FindResource(
            isError ? "DangerBrush" : "TextBrush");
        ToastMark.Background = (Brush)FindResource(
            isError ? "DangerBrush" : "SuccessBrush");
        ToastPanel.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void DialogConfirm_Click(object sender, RoutedEventArgs e) =>
        _confirmation?.TrySetResult(true);

    private void DialogCancel_Click(object sender, RoutedEventArgs e) =>
        _confirmation?.TrySetResult(false);
}
