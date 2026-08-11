namespace AlexDirectorConsole.Api.Storage;

public sealed class LocalBlobStorage : IBlobStorage
{
    private readonly string rootPath;

    public LocalBlobStorage(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configuredPath = configuration["BlobStorage:RootPath"] ?? "App_Data/blobs";
        rootPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        Directory.CreateDirectory(rootPath);
    }

    public async Task SaveAsync(
        string blobKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var filePath = ResolvePath(blobKey);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var output = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);
        await content.CopyToAsync(output, cancellationToken);
    }

    public async Task ReplaceAsync(
        string blobKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var filePath = ResolvePath(blobKey);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Blob does not exist.", blobKey);
        }

        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await content.CopyToAsync(output, cancellationToken);
            }
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(
        string blobKey,
        CancellationToken cancellationToken = default)
    {
        var filePath = ResolvePath(blobKey);
        Stream? stream = File.Exists(filePath)
            ? new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string blobKey, CancellationToken cancellationToken = default)
    {
        var filePath = ResolvePath(blobKey);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string blobKey)
    {
        var relativePath = blobKey.Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        if (!filePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Blob key resolves outside the storage root.");
        }

        return filePath;
    }
}