using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Listings;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class BrowseView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount? _currentUser;
    private readonly Action<string, bool> _notify;
    private readonly Action _loginRequested;
    private readonly Action<long> _openChat;
    private readonly List<PostingRow> _rows = [];
    private PostingSummary? _selectedPosting;

    public BrowseView(
        ServiceContext services,
        UserAccount? currentUser,
        Action<string, bool> notify,
        Action loginRequested,
        Action<long> openChat)
    {
        _services = services;
        _currentUser = currentUser;
        _notify = notify;
        _loginRequested = loginRequested;
        _openChat = openChat;
        InitializeComponent();

        CategoryBox.Items.Add(new CategoryFilter(null, "全部分类"));
        foreach (var category in _services.Listings.GetCategories())
        {
            CategoryBox.Items.Add(new CategoryFilter(category.Id, category.Name));
        }
        CategoryBox.SelectedIndex = 0;
        LoadPostings();
    }

    private void LoadPostings()
    {
        var categoryId = CategoryBox.SelectedItem is CategoryFilter { Id: not null } selected
            ? selected.Id
            : null;
        var postings = _services.Listings.GetPublicPostings(SearchBox.Text, categoryId);

        _rows.Clear();
        _rows.AddRange(postings.Select(posting => new PostingRow(
            posting,
            UiFormat.PostingTypeLabel(posting.Type),
            posting.Title,
            posting.CategoryName,
            posting.Location,
            posting.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"))));
        PostingsGrid.ItemsSource = null;
        PostingsGrid.ItemsSource = _rows;

        ResultCountText.Text = $"{_rows.Count} 条记录";
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSelection();
    }

    private void ShowDetails(PostingSummary posting)
    {
        _selectedPosting = posting;
        EmptyDetailPanel.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;
        DetailType.Text = UiFormat.PostingTypeLabel(posting.Type);
        DetailTitle.Text = posting.Title;
        DetailMeta.Text =
            $"{posting.CategoryName}  ·  {posting.Location}\n" +
            $"发生时间：{posting.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}  ·  发布者：{posting.PublisherName}";
        DetailDescription.Text = posting.Description;
        ClaimPanel.Visibility = Visibility.Collapsed;
        MatchesPanel.Visibility = Visibility.Collapsed;

        var imagePath = _services.Images.Resolve(posting.ImageRelativePath);
        if (imagePath is not null && File.Exists(imagePath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            DetailImage.Source = bitmap;
            DetailImage.Visibility = Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetName(
                DetailImage,
                posting.ImageAltText ?? "物品图片");
        }
        else
        {
            DetailImage.Source = null;
            DetailImage.Visibility = Visibility.Collapsed;
        }

        if (_currentUser is null)
        {
            ClaimButton.Content = "登录后认领";
            ClaimButton.IsEnabled = true;
        }
        else if (_currentUser.Id == posting.PublisherUserId)
        {
            ClaimButton.Content = "这是你发布的信息";
            ClaimButton.IsEnabled = false;
        }
        else
        {
            ClaimButton.Content = "发起认领";
            ClaimButton.IsEnabled = true;
        }
    }

    private void ClearSelection()
    {
        _selectedPosting = null;
        EmptyDetailPanel.Visibility = Visibility.Visible;
        DetailScroll.Visibility = Visibility.Collapsed;
        ClaimPanel.Visibility = Visibility.Collapsed;
        MatchesPanel.Visibility = Visibility.Collapsed;
    }

    private void PostingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PostingsGrid.SelectedItem is PostingRow row)
        {
            ShowDetails(row.Source);
        }
    }

    private void PostingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PostingsGrid.SelectedItem is PostingRow row)
        {
            ShowDetails(row.Source);
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) =>
        LoadPostings();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        LoadPostings();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoadPostings();
            e.Handled = true;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            LoadPostings();
        }
    }

    private void ClaimButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPosting is null)
        {
            return;
        }

        if (_currentUser is null)
        {
            _loginRequested();
            return;
        }

        ClaimPanel.Visibility = Visibility.Visible;
        ClaimStatementBox.Clear();
        ClaimStatementBox.Focus();
    }

    private void SubmitClaimButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPosting is null || _currentUser is null)
        {
            return;
        }

        var result = _services.Claims.StartClaim(
            _currentUser,
            _selectedPosting.Id,
            ClaimStatementBox.Text);
        if (!result.Succeeded || result.ClaimId is null)
        {
            _notify(result.Message, true);
            return;
        }

        _notify("认领会话已创建。", false);
        ClaimPanel.Visibility = Visibility.Collapsed;
        _openChat(result.ClaimId.Value);
    }

    private void CancelClaimButton_Click(object sender, RoutedEventArgs e)
    {
        ClaimStatementBox.Clear();
        ClaimPanel.Visibility = Visibility.Collapsed;
        ClaimButton.Focus();
    }

    private void FindMatchesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPosting is null)
        {
            return;
        }

        var suggestions = _services.Matching.GetSuggestions(
            _selectedPosting,
            _currentUser?.Id);
        MatchesList.ItemsSource = suggestions.Select(suggestion => new MatchRow(
            _selectedPosting.Id,
            suggestion.Candidate.Id,
            suggestion.Candidate.Title,
            $"{UiFormat.PostingTypeLabel(suggestion.Candidate.Type)} · {suggestion.Candidate.Location} · " +
            $"{string.Join("、", suggestion.Reasons)}",
            _currentUser is not null)).ToArray();
        NoMatchesText.Visibility = suggestions.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        MatchesPanel.Visibility = Visibility.Visible;
    }

    private void IgnoreMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null ||
            sender is not Button { Tag: MatchRow row } ||
            MatchesList.ItemsSource is not MatchRow[] matches)
        {
            return;
        }

        _services.Matching.IgnoreSuggestion(
            _currentUser.Id,
            row.TargetPostingId,
            row.CandidatePostingId);
        var remaining = matches
            .Where(match => match.CandidatePostingId != row.CandidatePostingId)
            .ToArray();
        MatchesList.ItemsSource = remaining;
        NoMatchesText.Visibility = remaining.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private sealed record PostingRow(
        PostingSummary Source,
        string TypeText,
        string Title,
        string Category,
        string Location,
        string TimeText);

    private sealed record MatchRow(
        long TargetPostingId,
        long CandidatePostingId,
        string Title,
        string Reasons,
        bool CanIgnore);

    private sealed record CategoryFilter(int? Id, string Name);
}
