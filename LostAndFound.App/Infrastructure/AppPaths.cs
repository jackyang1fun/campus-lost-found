namespace LostAndFound.App.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "lost-found.db");
        ImagesDirectory = Path.Combine(DataDirectory, "images");
        BackupsDirectory = Path.Combine(DataDirectory, "backups");
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string ImagesDirectory { get; }

    public string BackupsDirectory { get; }

    public static AppPaths ForCurrentApplication()
    {
        var overridePath = Environment.GetEnvironmentVariable("LOST_FOUND_DATA_DIR");
        var dataDirectory = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : overridePath;

        return new AppPaths(dataDirectory);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
