using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Listings;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class ReviewView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _administrator;
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;

    public ReviewView(
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
        LoadPending();
    }

    private void LoadPending()
    {
        var postings = _services.Listings.GetPendingPostings(_administrator);
        PendingGrid.ItemsSource = postings.Select(posting => new ReviewRow(
            posting,
            UiFormat.PostingTypeLabel(posting.Type),
            posting.Title,
            posting.PublisherName,
            posting.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"))).ToArray();
        EmptyText.Visibility = postings.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyDetail.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void PendingGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PendingGrid.SelectedItem is not ReviewRow row)
        {
            return;
        }

        var posting = row.Source;
        EmptyDetail.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailType.Text = UiFormat.PostingTypeLabel(posting.Type);
        DetailTitle.Text = posting.Title;
        DetailMeta.Text =
            $"{posting.CategoryName} · {posting.Location}\n" +
            $"发生时间：{posting.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
            $"发布者：{posting.PublisherName} · 联系方式：{posting.Contact}";
        DetailDescription.Text = posting.Description;
        DetailImageAlt.Text = posting.ImageAltText ?? "未上传图片。";
        var imagePath = _services.Images.Resolve(posting.ImageRelativePath);
        if (imagePath is not null && File.Exists(imagePath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            ReviewImage.Source = bitmap;
            ReviewImage.Visibility = Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetName(
                ReviewImage,
                posting.ImageAltText ?? "待审核物品图片");
        }
        else
        {
            ReviewImage.Source = null;
            ReviewImage.Visibility = Visibility.Collapsed;
        }

        ReasonBox.Clear();
    }

    private async void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PendingGrid.SelectedItem is not ReviewRow row)
        {
            return;
        }

        var confirmed = await _confirm(
            "审核通过",
            $"确认公开“{row.Source.Title}”吗？审核通过后，游客可以立即浏览该信息。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Listings.ReviewPosting(
            _administrator,
            row.Source.Id,
            approve: true,
            reason: null);
        _notify(result.Message, !result.Succeeded);
        LoadPending();
    }

    private void RejectButton_Click(object sender, RoutedEventArgs e)
    {
        if (PendingGrid.SelectedItem is not ReviewRow row)
        {
            return;
        }

        var result = _services.Listings.ReviewPosting(
            _administrator,
            row.Source.Id,
            approve: false,
            reason: ReasonBox.Text);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadPending();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        LoadPending();

    private sealed record ReviewRow(
        PostingSummary Source,
        string TypeText,
        string Title,
        string Publisher,
        string CreatedText);
}
