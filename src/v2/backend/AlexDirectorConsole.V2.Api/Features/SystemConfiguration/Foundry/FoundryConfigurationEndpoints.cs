using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;

public sealed record UpdateFoundryConfigurationRequest(
    string? Endpoint,
    string? ApiKey,
    bool ClearApiKey,
    string? ImageEndpoint,
    string? ImageApiKey,
    bool ClearImageApiKey,
    string? ImageQuality);

public static class FoundryConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapFoundryConfiguration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/system/foundry-configuration");

        group.MapGet("/", async (
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) =>
        {
            var configuration = await queryDispatcher.QueryAsync(
                new GetFoundryConfigurationQuery(),
                cancellationToken);
            return Results.Ok(configuration);
        });

        group.MapPut("/", async (
            UpdateFoundryConfigurationRequest request,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await commandDispatcher.SendAsync(
                new UpdateFoundryConfigurationCommand(
                    request.Endpoint,
                    request.ApiKey,
                    request.ClearApiKey,
                    request.ImageEndpoint,
                    request.ImageApiKey,
                    request.ClearImageApiKey,
                    request.ImageQuality),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Configuration)
                : Results.ValidationProblem(result.Errors);
        });

        group.MapPost("/test", async (
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await commandDispatcher.SendAsync(
                new TestFoundryConnectionCommand(),
                cancellationToken);
            return result.IsSuccess ? Results.Ok(result) : Results.BadRequest(result);
        });

        return app;
    }
}