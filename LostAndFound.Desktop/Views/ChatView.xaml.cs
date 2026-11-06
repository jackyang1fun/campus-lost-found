using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Claims;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class ChatView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _user;
    private readonly long _claimId;
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly Action _back;
    private ClaimConversation? _conversation;

    public ChatView(
        ServiceContext services,
        UserAccount user,
        long claimId,
        Action<string, bool> notify,
        Func<string, string, Task<bool>> confirm,
        Action back)
    {
        _services = services;
        _user = user;
        _claimId = claimId;
        _notify = notify;
        _confirm = confirm;
        _back = back;
        InitializeComponent();
        LoadConversation();
        Loaded += (_, _) => MessageBox.Focus();
    }

    private void LoadConversation()
    {
        _conversation = _services.Claims.GetConversation(_user, _claimId);
        if (_conversation is null)
        {
            _notify("无法访问该认领会话。", true);
            _back();
            return;
        }

        ChatTitle.Text = _conversation.PostingTitle;
        AliasText.Text =
            $"你：{_conversation.MyAlias}  ·  对方：{_conversation.CounterpartyAlias}  ·  " +
            UiFormat.ClaimStatusLabel(_conversation.Status) +
            (_conversation.ClosureReason is null
                ? string.Empty
                : $"  ·  {_conversation.ClosureReason}");
        ClaimStatementText.Text = string.IsNullOrWhiteSpace(_conversation.ClaimStatement)
            ? "认领说明：历史记录未填写。"
            : $"认领说明：{_conversation.ClaimStatement}";

        var surface = (Brush)FindResource("SurfaceBrush");
        var surfaceMuted = (Brush)FindResource("SurfaceMutedBrush");
        var text = (Brush)FindResource("TextBrush");
        var muted = (Brush)FindResource("MutedTextBrush");
        var messages = _conversation.Messages.Select(message => new MessageRow(
            message.IsSystem ? string.Empty : message.SenderAlias,
            message.Content,
            message.SentAt.ToLocalTime().ToString("MM-dd HH:mm"),
            message.IsSystem
                ? new Thickness(0, 4, 0, 8)
                : message.SenderAlias == _conversation.MyAlias
                    ? new Thickness(120, 4, 0, 8)
                    : new Thickness(0, 4, 120, 8),
            message.IsSystem
                ? HorizontalAlignment.Center
                : message.SenderAlias == _conversation.MyAlias
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,
            message.IsSystem ? surfaceMuted : message.SenderAlias == _conversation.MyAlias
                ? (Brush)FindResource("AccentSoftBrush")
                : surface,
            message.IsSystem ? muted : text,
            muted,
            muted)).ToArray();

        MessagesList.ItemsSource = messages;
        Dispatcher.BeginInvoke(() => MessagesScroll.ScrollToEnd());

        MessageBox.IsEnabled = _conversation.CanSendMessage;
        CompleteButton.Visibility = _conversation.IsPublisher
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompleteButton.IsEnabled = _conversation.CanComplete;
        CancelClaimButton.IsEnabled = _conversation.CanCancel;
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) =>
        SendMessage();

    private void MessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SendMessage();
            e.Handled = true;
        }
    }

    private void SendMessage()
    {
        if (_conversation?.CanSendMessage != true)
        {
            return;
        }

        var result = _services.Claims.SendMessage(_user, _claimId, MessageBox.Text);
        if (!result.Succeeded)
        {
            _notify(result.Message, true);
            return;
        }

        MessageBox.Clear();
        LoadConversation();
        MessageBox.Focus();
    }

    private async void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await _confirm(
            "确认交接完成",
            "确认物品已交接完成吗？该认领将结束，关联信息也会标记为已解决。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Claims.CompleteClaim(_user, _claimId);
        _notify(result.Message, !result.Succeeded);
        LoadConversation();
    }

    private async void CancelClaimButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await _confirm(
            "取消认领",
            "确定取消当前认领吗？聊天记录会保留为只读状态。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Claims.CancelClaim(_user, _claimId);
        _notify(result.Message, !result.Succeeded);
        LoadConversation();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _back();

    private sealed record MessageRow(
        string Sender,
        string Content,
        string TimeText,
        Thickness Margin,
        HorizontalAlignment Alignment,
        Brush Background,
        Brush TextColor,
        Brush SenderColor,
        Brush TimeColor);
}
