using System.Text.Json;
using AlexDirectorConsole.Api.Application.Conversations;
using AlexDirectorConsole.Api.Contracts;

namespace AlexDirectorConsole.Api.Endpoints;

public static class StreamingConversationEndpoints
{
    public static IEndpointRouteBuilder MapStreamingConversationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/projects/{projectId:guid}/messages/stream",
            async (
                Guid projectId,
                SendMessageRequest request,
                HttpResponse response,
                IDirectorSessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                var stream = new HttpDirectorSessionStream(response);
                await sessionService.ExecuteAsync(projectId, request, stream, cancellationToken);
            })
            .WithName("StreamProjectMessage");

        return app;
    }

    private sealed class HttpDirectorSessionStream(HttpResponse response) : IDirectorSessionStream
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "application/x-ndjson; charset=utf-8";
            response.Headers.CacheControl = "no-cache, no-transform";
            response.Headers.Append("X-Accel-Buffering", "no");
            return ValueTask.CompletedTask;
        }

        public async ValueTask RejectAsync(
            int statusCode,
            string error,
            CancellationToken cancellationToken)
        {
            response.StatusCode = statusCode;
            await response.WriteAsJsonAsync(new { error }, cancellationToken);
        }

        public async ValueTask WriteAsync(object value, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(value, SerializerOptions);
            await response.WriteAsync(json + "\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
}