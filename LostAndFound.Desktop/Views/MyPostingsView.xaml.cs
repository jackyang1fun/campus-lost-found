using System.Windows;
using System.Windows.Controls;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Listings;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class MyPostingsView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _user;
    private readonly Action<string, bool> _notify;
    private readonly Action _create;
    private readonly Action<PostingSummary> _edit;
    private readonly Func<string, string, Task<bool>> _confirm;
    private PostingSummary? _selected;

    public MyPostingsView(
        ServiceContext services,
        UserAccount user,
        Action<string, bool> notify,
        Action create,
        Action<PostingSummary> edit,
        Func<string, string, Task<bool>> confirm)
    {
        _services = services;
        _user = user;
        _notify = notify;
        _create = create;
        _edit = edit;
        _confirm = confirm;
        InitializeComponent();
        LoadPostings();
    }

    private void LoadPostings()
    {
        var postings = _services.Listings.GetOwnPostings(_user.Id);
        PostingsGrid.ItemsSource = postings.Select(posting => new PostingRow(
            posting,
            UiFormat.PostingTypeLabel(posting.Type),
            posting.Title,
            posting.CategoryName,
            UiFormat.PostingStatusLabel(posting.Status),
            posting.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"))).ToArray();
        EmptyText.Visibility = postings.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyDetail.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void PostingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PostingsGrid.SelectedItem is not PostingRow row)
        {
            return;
        }

        var posting = row.Source;
        _selected = posting;
        EmptyDetail.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = posting.Title;
        DetailMeta.Text =
            $"{UiFormat.PostingTypeLabel(posting.Type)} · {posting.CategoryName}\n" +
            $"{posting.Location} · {posting.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        StatusText.Text = $"当前状态：{UiFormat.PostingStatusLabel(posting.Status)}";
        DetailDescription.Text = posting.Description;
        ReviewReason.Text = posting.ReviewReason ?? "暂无审核说明。";
        EditButton.IsEnabled = posting.Status is
            PostingStatus.PendingReview or PostingStatus.Approved or PostingStatus.Rejected;
        ResolveButton.IsEnabled = posting.Status == PostingStatus.Approved;
        CancelPostingButton.IsEnabled = posting.Status is
            PostingStatus.PendingReview or PostingStatus.Approved or PostingStatus.Rejected;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        LoadPostings();

    private void CreateButton_Click(object sender, RoutedEventArgs e) =>
        _create();

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
        {
            _edit(_selected);
        }
    }

    private async void ResolveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await _confirm(
            "标记为已解决",
            "确认物品已经找回或完成交接吗？该信息将不再出现在公开列表。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Listings.MarkOwnResolved(_user, _selected.Id);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadPostings();
        }
    }

    private async void CancelPostingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await _confirm(
            "撤销信息",
            "确认撤销这条信息吗？撤销后不会继续出现在公开列表中。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Listings.CancelOwnPosting(_user, _selected.Id);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadPostings();
        }
    }

    private sealed record PostingRow(
        PostingSummary Source,
        string TypeText,
        string Title,
        string Category,
        string StatusText,
        string CreatedText);
}
