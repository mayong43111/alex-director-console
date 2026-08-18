using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;

namespace AlexDirectorConsole.V2.Api.Tests.Features.SystemConfiguration;

public sealed class ComfyUiConfigurationEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Initial_configuration_uses_disabled_local_defaults()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v2/system/comfyui-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configuration = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
        Assert.NotNull(configuration);
        Assert.Equal("local-http", configuration.ConnectionMode);
        Assert.Equal("http://127.0.0.1:8188", configuration.BaseUrl);
        Assert.Equal("minimax-h3-fl2va-turbo-4step", configuration.WorkflowProfile);
        Assert.Equal(1, configuration.MaxConcurrentJobs);
        Assert.False(configuration.IsEnabled);
    }

    [Fact]
    public async Task Save_normalizes_url_and_test_returns_h3_capabilities()
    {
        using var client = factory.CreateClient();
        var save = await client.PutAsJsonAsync(
            "/api/v2/system/comfyui-configuration",
            new { baseUrl = "http://127.0.0.1:8188/", isEnabled = true });

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var configuration = await save.Content.ReadFromJsonAsync<ConfigurationResponse>();
        Assert.NotNull(configuration);
        Assert.Equal("http://127.0.0.1:8188", configuration.BaseUrl);
        Assert.True(configuration.IsEnabled);

        var test = await client.PostAsync(
            "/api/v2/system/comfyui-configuration/test",
            null);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        var capabilities = await test.Content.ReadFromJsonAsync<CapabilitiesResponse>();
        Assert.NotNull(capabilities);
        Assert.True(capabilities.IsSuccess);
        Assert.Empty(capabilities.MissingNodes);
        Assert.Empty(capabilities.MissingModels);
    }

    [Fact]
    public async Task Test_requires_enabled_configuration()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v2/system/comfyui-configuration/capabilities");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Save_rejects_non_http_url()
    {
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/v2/system/comfyui-configuration",
            new { baseUrl = "file:///tmp/comfyui", isEnabled = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ConfigurationResponse(
        string ConnectionMode,
        string BaseUrl,
        string WorkflowProfile,
        int MaxConcurrentJobs,
        bool IsEnabled);

    private sealed record CapabilitiesResponse(
        bool IsSuccess,
        IReadOnlyList<string> MissingNodes,
        IReadOnlyList<string> MissingModels);
}