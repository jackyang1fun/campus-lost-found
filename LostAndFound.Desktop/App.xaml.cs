using System.Windows;
using LostAndFound.App.Features.Admin;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Disputes;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Features.Matching;
using LostAndFound.App.Infrastructure;
using LostAndFound.Desktop.Shared;

namespace LostAndFound.Desktop;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstance = SingleInstanceGuard.TryAcquire();
            if (_singleInstance is null)
            {
                MessageBox.Show(
                    "Lost and Found System 已经在运行。请切换到现有窗口。",
                    "Lost and Found System",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            ThemeManager.Apply();
            SystemParameters.StaticPropertyChanged += (_, _) =>
                Dispatcher.Invoke(ThemeManager.Apply);

            var paths = AppPaths.ForCurrentApplication();
            paths.EnsureDirectories();

            var database = new SqliteDatabase(paths.DatabasePath);
            new DatabaseInitializer(database).Initialize();

            var audit = new AuditService();
            var auth = new AuthService(database, new LocalCampusCardVerifier(), audit);
            var images = new LocalImageStore(paths);
            var listings = new ListingService(database, images, audit);
            var matching = new MatchingService(
                listings,
                new RuleBasedMatchPolicy(),
                database);
            var claims = new ClaimService(
                database,
                audit,
                new ClaimCompletionCoordinator(database, audit));
            var disputes = new DisputeService(database, audit);
            var backups = new BackupService(paths, database, audit);
            backups.EnsureDailyBackup();

            var context = new ServiceContext(
                auth,
                new AdminService(database, audit),
                listings,
                matching,
                claims,
                disputes,
                backups,
                images,
                paths);
            var window = new MainWindow(context);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"应用启动失败：{exception.Message}\n\n请确认应用目录具有写入权限。",
                "Lost and Found System",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
