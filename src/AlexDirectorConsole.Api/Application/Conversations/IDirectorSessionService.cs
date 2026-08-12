using AlexDirectorConsole.Api.Contracts;

namespace AlexDirectorConsole.Api.Application.Conversations;

public interface IDirectorSessionStream
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask RejectAsync(int statusCode, string error, CancellationToken cancellationToken);

    ValueTask WriteAsync(object value, CancellationToken cancellationToken);
}

public interface IDirectorSessionService
{
    Task ExecuteAsync(
        Guid projectId,
        SendMessageRequest request,
        IDirectorSessionStream stream,
        CancellationToken cancellationToken);
}