using System.Windows;
using System.Windows.Controls;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Backups;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop.Views;

public partial class BackupView : UserControl
{
    private readonly ServiceContext _services;
    private readonly UserAccount _administrator;
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly Action _restoreCompleted;
    private BackupRow? _selected;

    public BackupView(
        ServiceContext services,
        UserAccount administrator,
        Action<string, bool> notify,
        Func<string, string, Task<bool>> confirm,
        Action restoreCompleted)
    {
        _services = services;
        _administrator = administrator;
        _notify = notify;
        _confirm = confirm;
        _restoreCompleted = restoreCompleted;
        InitializeComponent();
        LoadBackups();
    }

    private void LoadBackups()
    {
        var backups = _services.Backups.GetBackups();
        BackupsGrid.ItemsSource = backups.Select(backup => new BackupRow(
            backup,
            backup.FileName,
            backup.IsAutomatic ? "自动" : "手动",
            backup.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            FormatSize(backup.SizeBytes))).ToArray();
        EmptyText.Visibility = backups.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        LatestBackupText.Text = backups.Count == 0
            ? "尚未创建备份。"
            : $"最近备份：{backups[0].CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {backups[0].FileName}";
        _selected = null;
    }

    private void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _services.Backups.CreateManualBackup(_administrator);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadBackups();
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            _notify("请先选择一个备份文件。", true);
            return;
        }

        var confirmed = await _confirm(
            "恢复数据库",
            $"将使用“{_selected.Source.FileName}”替换当前数据库。\n\n" +
            "系统会先创建恢复前安全副本。恢复完成后建议关闭并重新打开应用。");
        if (!confirmed)
        {
            return;
        }

        var result = _services.Backups.RestoreBackup(
            _administrator,
            _selected.Source.Path);
        _notify(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            LoadBackups();
            _restoreCompleted();
        }
    }

    private void BackupsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = BackupsGrid.SelectedItem as BackupRow;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:F1} MB"
            : $"{Math.Max(1, bytes / 1024d):F0} KB";

    private sealed record BackupRow(
        BackupFile Source,
        string FileName,
        string KindText,
        string CreatedText,
        string SizeText);
}
