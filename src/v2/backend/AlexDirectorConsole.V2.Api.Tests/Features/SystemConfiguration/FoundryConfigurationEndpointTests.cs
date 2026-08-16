using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.SystemConfiguration;

public sealed class FoundryConfigurationEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Initial_configuration_is_unconfigured_and_fixed_to_gpt_5_4()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v2/system/foundry-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configuration = await response.Content.ReadFromJsonAsync<FoundryConfigurationResponse>();
        Assert.NotNull(configuration);
        Assert.Equal("Azure AI Foundry", configuration.Provider);
        Assert.Equal("gpt-5.4", configuration.Deployment);
        Assert.False(configuration.ApiKeyConfigured);
    }

    [Fact]
    public async Task Save_encrypts_api_key_and_never_returns_it()
    {
        const string apiKey = "test-secret-key";
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/v2/system/foundry-configuration",
            new { endpoint = "https://example.openai.azure.com/", apiKey, clearApiKey = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(apiKey, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedApiKey", responseText, StringComparison.OrdinalIgnoreCase);
        var configuration = JsonSerializer.Deserialize<FoundryConfigurationResponse>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(configuration);
        Assert.True(configuration.ApiKeyConfigured);
        Assert.Equal("https://example.openai.azure.com", configuration.Endpoint);
        Assert.Equal("gpt-5.4", configuration.Deployment);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var stored = Assert.Single(dbContext.FoundryConfigurations);
        Assert.NotEqual(apiKey, stored.ProtectedApiKey);
        Assert.DoesNotContain(apiKey, stored.ProtectedApiKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_endpoint_returns_validation_problem()
    {
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/v2/system/foundry-configuration",
            new { endpoint = "not-a-url", apiKey = "key", clearApiKey = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Test_connection_uses_saved_configuration()
    {
        using var client = factory.CreateClient();
        await client.PutAsJsonAsync(
            "/api/v2/system/foundry-configuration",
            new { endpoint = "https://example.openai.azure.com", apiKey = "key", clearApiKey = false });

        var response = await client.PostAsync(
            "/api/v2/system/foundry-configuration/test",
            null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TestConnectionResponse>();
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("gpt-5.4", result.Deployment);
    }

    private sealed record FoundryConfigurationResponse(
        string Provider,
        string Endpoint,
        string Deployment,
        bool ApiKeyConfigured,
        DateTimeOffset? UpdatedAtUtc);

    private sealed record TestConnectionResponse(
        bool IsSuccess,
        string Message,
        string Deployment,
        bool IsConfigured);
}