namespace AlexDirectorConsole.Api.Storage;

public interface IBlobStorage
{
    Task SaveAsync(string blobKey, Stream content, CancellationToken cancellationToken = default);

    Task ReplaceAsync(string blobKey, Stream content, CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string blobKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string blobKey, CancellationToken cancellationToken = default);
}