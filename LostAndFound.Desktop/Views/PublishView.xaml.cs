using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Listings;
using LostAndFound.Desktop.Shared;
using Microsoft.Win32;

namespace LostAndFound.Desktop.Views;

public partial class PublishView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _user;
    private readonly Action<string, bool> _notify;
    private readonly Action _openMyPostings;
    private readonly Action _cancel;
    private readonly PostingSummary? _existingPosting;
    private string? _imagePath;
    private bool _removeImage;

    public PublishView(
        ServiceContext services,
        UserAccount user,
        Action<string, bool> notify,
        Action openMyPostings,
        Action cancel,
        PostingSummary? existingPosting = null)
    {
        _services = services;
        _user = user;
        _notify = notify;
        _openMyPostings = openMyPostings;
        _cancel = cancel;
        _existingPosting = existingPosting;
        InitializeComponent();

        TypeBox.Items.Add(new TypeOption(PostingType.Lost, "失物信息"));
        TypeBox.Items.Add(new TypeOption(PostingType.Found, "招领信息"));
        TypeBox.SelectedIndex = 0;

        foreach (var category in _services.Listings.GetCategories())
        {
            CategoryBox.Items.Add(category);
        }
        CategoryBox.SelectedIndex = 0;

        OccurredDate.SelectedDate = DateTime.Today;
        OccurredTime.Text = DateTime.Now.ToString("HH:mm");
        ContactBox.Text = user.Contact;

        if (existingPosting is not null)
        {
            TypeBox.SelectedItem = TypeBox.Items
                .Cast<TypeOption>()
                .First(option => option.Value == existingPosting.Type);
            CategoryBox.SelectedItem = CategoryBox.Items
                .Cast<Category>()
                .FirstOrDefault(category => category.Id == existingPosting.CategoryId);
            TitleBox.Text = existingPosting.Title;
            OccurredDate.SelectedDate = existingPosting.OccurredAt.LocalDateTime.Date;
            OccurredTime.Text = existingPosting.OccurredAt.LocalDateTime.ToString("HH:mm");
            LocationBox.Text = existingPosting.Location;
            DescriptionBox.Text = existingPosting.Description;
            ContactBox.Text = existingPosting.Contact;
            ImageAltBox.Text = existingPosting.ImageAltText ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(existingPosting.ImageRelativePath))
            {
                ImageNameText.Text =
                    $"保留当前图片：{Path.GetFileName(existingPosting.ImageRelativePath)}";
                ClearImageButton.Visibility = Visibility.Visible;
            }
        }

        Loaded += (_, _) => TitleBox.Focus();
    }

    private void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择物品图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        _imagePath = dialog.FileName;
        _removeImage = false;
        ImageNameText.Text = Path.GetFileName(dialog.FileName);
        ClearImageButton.Visibility = Visibility.Visible;
        ImageAltBox.Focus();
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        _imagePath = null;
        _removeImage = true;
        ImageNameText.Text = "未选择图片";
        ClearImageButton.Visibility = Visibility.Collapsed;
        ImageAltBox.Clear();
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;
        if (TypeBox.SelectedItem is not TypeOption typeOption ||
            CategoryBox.SelectedItem is not Category category)
        {
            StatusText.Text = "请选择信息类型和物品分类。";
            return;
        }

        if (OccurredDate.SelectedDate is not { } date ||
            !TimeOnly.TryParseExact(
                OccurredTime.Text.Trim(),
                ["H:mm", "HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            StatusText.Text = "请输入有效日期和二十四小时制时间，例如 18:30。";
            OccurredTime.Focus();
            return;
        }

        var occurredAt = new DateTimeOffset(date.Date.Add(time.ToTimeSpan()));
        var request = new CreatePostingRequest(
            typeOption.Value,
            TitleBox.Text,
            category.Id,
            occurredAt,
            LocationBox.Text,
            DescriptionBox.Text,
            ContactBox.Text,
            _imagePath,
            ImageAltBox.Text);
        var result = _existingPosting is null
            ? _services.Listings.CreatePosting(_user, request)
            : _services.Listings.UpdateOwnPosting(
                _user,
                _existingPosting.Id,
                request,
                _removeImage);

        if (!result.Succeeded)
        {
            StatusText.Text = result.Message;
            return;
        }

        _notify(
            _existingPosting is null
                ? "信息已提交，等待管理员审核。"
                : "信息已更新并重新提交审核。",
            false);
        _openMyPostings();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cancel();

    private sealed record TypeOption(PostingType Value, string Label);
}
