using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;

public sealed record UpdateComfyUiConfigurationRequest(string? BaseUrl, bool IsEnabled);

public static class ComfyUiConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapComfyUiConfiguration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/system/comfyui-configuration");

        group.MapGet("/", async (
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) => Results.Ok(
                await queryDispatcher.QueryAsync(
                    new GetComfyUiConfigurationQuery(),
                    cancellationToken)));

        group.MapPut("/", async (
            UpdateComfyUiConfigurationRequest request,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await commandDispatcher.SendAsync(
                new UpdateComfyUiConfigurationCommand(request.BaseUrl, request.IsEnabled),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Configuration)
                : Results.ValidationProblem(result.Errors);
        });

        async Task<IResult> TestAsync(
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken)
        {
            var result = await commandDispatcher.SendAsync(
                new TestComfyUiConnectionCommand(),
                cancellationToken);
            return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
        }

        group.MapPost("/test", TestAsync);
        group.MapGet("/capabilities", TestAsync);
        return app;
    }
}