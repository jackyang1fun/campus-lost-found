using System.Windows;
using System.Windows.Controls;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Disputes;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class DisputeAdminView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _administrator;
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;
    private DisputeSummary? _selected;

    public DisputeAdminView(
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
        LoadDisputes();
    }

    private void LoadDisputes()
    {
        var disputes = _services.Disputes.GetDisputes(_administrator);
        DisputesGrid.ItemsSource = disputes.Select(dispute => new DisputeRow(
            dispute,
            dispute.PostingTitle,
            dispute.ReasonCategory,
            DisputeText.Status(dispute.Status),
            dispute.OpenedByDisplayName)).ToArray();
        EmptyText.Visibility = disputes.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        _selected = null;
        EmptyDetail.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void DisputesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DisputesGrid.SelectedItem is not DisputeRow row)
        {
            return;
        }

        _selected = row.Source;
        var details = _services.Disputes.GetDisputeDetails(_administrator, row.Source.Id);
        if (details is null)
        {
            _notify("无法读取争议详情。", true);
            return;
        }

        EmptyDetail.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = details.Summary.PostingTitle;
        DetailMeta.Text =
            $"原因：{details.Summary.ReasonCategory}  ·  " +
            $"状态：{DisputeText.Status(details.Summary.Status)}  ·  " +
            $"提交人：{details.Summary.OpenedByDisplayName}";
        DisputeDescription.Text = details.Description;
        PublisherIdentity.Text =
            $"{details.Publisher.DisplayName}\n{details.Publisher.CampusCardNumber}\n{details.Publisher.Contact}";
        InitiatorIdentity.Text =
            $"{details.Initiator.DisplayName}\n{details.Initiator.CampusCardNumber}\n{details.Initiator.Contact}";
        MessagesList.ItemsSource = details.Messages
            .Select(message =>
                $"[{message.SentAt.ToLocalTime():MM-dd HH:mm}] {message.SenderAlias}：{message.Content}")
            .ToArray();
        ConclusionBox.Text = details.AdminConclusion ?? string.Empty;
    }

    private async void CloseDisputeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await _confirm(
            "关闭争议",
            "确认提交处理结论并关闭该争议吗？双方将看到处理结果。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Disputes.ResolveDispute(
            _administrator,
            _selected.Id,
            needMoreInformation: false,
            ConclusionBox.Text);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadDisputes();
        }
    }

    private void NeedMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var result = _services.Disputes.ResolveDispute(
            _administrator,
            _selected.Id,
            needMoreInformation: true,
            ConclusionBox.Text);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadDisputes();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        LoadDisputes();

    private sealed record DisputeRow(
        DisputeSummary Source,
        string Title,
        string Reason,
        string StatusText,
        string OpenedBy);
}
