using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Claims;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class MessagesView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _user;
    private readonly Action<string, bool> _notify;
    private readonly Action<long> _openChat;
    private ClaimSummary? _selected;

    public MessagesView(
        ServiceContext services,
        UserAccount user,
        Action<string, bool> notify,
        Action<long> openChat)
    {
        _services = services;
        _user = user;
        _notify = notify;
        _openChat = openChat;
        InitializeComponent();
        LoadConversations();
    }

    private void LoadConversations()
    {
        var claims = _services.Claims.GetClaimsForUser(_user.Id);
        ConversationsGrid.ItemsSource = claims.Select(claim => new ConversationRow(
            claim,
            claim.PostingTitle,
            claim.CounterpartyAlias,
            UiFormat.ClaimStatusLabel(claim.Status),
            claim.LastMessage ?? claim.ClosureReason ?? "暂无消息",
            UiFormat.RelativeDate(claim.UpdatedAt))).ToArray();
        _selected = null;
        EmptyDetail.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void ConversationsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConversationsGrid.SelectedItem is not ConversationRow row)
        {
            return;
        }

        _selected = row.Source;
        EmptyDetail.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = row.Title;
        DetailMeta.Text =
            $"对方：{row.Counterparty}\n状态：{row.StatusText}\n更新：{row.TimeText}";
        LastMessageText.Text = row.LastMessage;
    }

    private void OpenChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
        {
            _openChat(_selected.Id);
        }
    }

    private void ConversationsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selected is not null)
        {
            _openChat(_selected.Id);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        LoadConversations();

    private sealed record ConversationRow(
        ClaimSummary Source,
        string Title,
        string Counterparty,
        string StatusText,
        string LastMessage,
        string TimeText);
}
