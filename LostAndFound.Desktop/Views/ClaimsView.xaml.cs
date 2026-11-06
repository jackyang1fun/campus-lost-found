using System.Windows;
using System.Windows.Controls;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Claims;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class ClaimsView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _user;
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;
    private ClaimSummary? _selected;

    public ClaimsView(
        ServiceContext services,
        UserAccount user,
        Action<string, bool> notify,
        Func<string, string, Task<bool>> confirm)
    {
        _services = services;
        _user = user;
        _notify = notify;
        _confirm = confirm;
        InitializeComponent();
        LoadClaims();
    }

    private void LoadClaims()
    {
        var claims = _services.Claims.GetClaimsForUser(_user.Id);
        ClaimsGrid.ItemsSource = claims.Select(claim => new ClaimRow(
            claim,
            claim.PostingTitle,
            UiFormat.PostingTypeLabel(claim.PostingType),
            claim.CounterpartyAlias,
            UiFormat.ClaimStatusLabel(claim.Status),
            UiFormat.RelativeDate(claim.UpdatedAt))).ToArray();
        EmptyText.Visibility = claims.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        _selected = null;
        EmptyDetail.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void ClaimsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClaimsGrid.SelectedItem is not ClaimRow row)
        {
            return;
        }

        _selected = row.Source;
        var conversation = _services.Claims.GetConversation(_user, row.Source.Id);
        if (conversation is null)
        {
            _notify("无法读取该认领记录。", true);
            return;
        }

        EmptyDetail.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = conversation.PostingTitle;
        DetailMeta.Text =
            $"{UiFormat.PostingTypeLabel(conversation.PostingType)} · " +
            $"{UiFormat.ClaimStatusLabel(conversation.Status)}";
        AliasText.Text =
            $"你的匿名编号：{conversation.MyAlias}    对方：{conversation.CounterpartyAlias}";
        ClosureText.Text = conversation.ClosureReason is null
            ? string.Empty
            : $"结束原因：{conversation.ClosureReason}";
        CompleteButton.IsEnabled = conversation.CanComplete;
        CancelButton.IsEnabled = conversation.CanCancel;
        DisputeButton.IsEnabled =
            conversation.Status is ClaimStatus.Negotiating or ClaimStatus.Completed;
        DisputePanel.Visibility = Visibility.Collapsed;
    }

    private async void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await _confirm(
            "确认完成交接",
            "确认物品已经交接完成吗？该认领将结束，关联信息会标记为已解决。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Claims.CompleteClaim(_user, _selected.Id);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadClaims();
        }
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await _confirm(
            "取消认领",
            "确定取消当前认领吗？匿名聊天记录会保留为只读状态。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Claims.CancelClaim(_user, _selected.Id);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadClaims();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        LoadClaims();

    private void DisputeButton_Click(object sender, RoutedEventArgs e)
    {
        DisputePanel.Visibility = Visibility.Visible;
        DisputeReasonBox.Focus();
    }

    private void HideDisputeButton_Click(object sender, RoutedEventArgs e) =>
        DisputePanel.Visibility = Visibility.Collapsed;

    private void SubmitDisputeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var reason = (DisputeReasonBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                     ?? string.Empty;
        var result = _services.Disputes.OpenDispute(
            _user,
            _selected.Id,
            reason,
            DisputeDescriptionBox.Text);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            DisputeDescriptionBox.Clear();
            DisputeReasonBox.SelectedIndex = -1;
            LoadClaims();
        }
    }

    private sealed record ClaimRow(
        ClaimSummary Source,
        string Title,
        string TypeText,
        string Counterparty,
        string StatusText,
        string TimeText);
}
