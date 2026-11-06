namespace LostAndFound.App.Infrastructure;

public sealed class LocalImageStore
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".bmp",
            ".gif"
        };

    private readonly AppPaths _paths;

    public LocalImageStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string Save(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("所选图片不存在。", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("仅支持 JPG、PNG、BMP 或 GIF 图片。");
        }

        var fileInfo = new FileInfo(sourcePath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException("图片大小不能超过 5 MB。");
        }

        _paths.EnsureDirectories();
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(_paths.ImagesDirectory, storedFileName);
        File.Copy(sourcePath, destination);
        return Path.Combine("images", storedFileName).Replace('\\', '/');
    }

    public string? Resolve(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_paths.DataDirectory, normalized));
        return fullPath.StartsWith(
            _paths.DataDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    public void Delete(string? relativePath)
    {
        var fullPath = Resolve(relativePath);
        if (fullPath is not null && File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
