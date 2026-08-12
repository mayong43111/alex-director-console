using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Application.Assets;

public interface IShotAssetBinder
{
    Task<ShotAssetLink> BindAsync(
        Guid projectId,
        Guid shotResourceId,
        Guid assetId,
        string role,
        bool exclusive,
        CancellationToken cancellationToken = default);
}