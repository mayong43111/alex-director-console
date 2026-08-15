using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AlexDirectorConsole.Api.Application.Production;

public interface IAzureVmLifecycleService
{
    Task<bool> EnsureStartedAsync(CancellationToken cancellationToken);
    Task DeallocateAsync(CancellationToken cancellationToken);
}

public sealed class AzureVmLifecycleService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IAzureVmLifecycleService
{
    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        var stateJson = await RunAzAsync(
            ["vm", "get-instance-view", "--subscription", Required("SubscriptionId"),
                "--resource-group", Required("ResourceGroup"), "--name", Required("Name"), "-o", "json"],
            cancellationToken);
        using var document = JsonDocument.Parse(stateJson);
        var running = document.RootElement.GetProperty("instanceView").GetProperty("statuses")
            .EnumerateArray()
            .Any(status => status.GetProperty("code").GetString() == "PowerState/running");
        var started = !running;
        try
        {
            if (started)
            {
                await RunAzAsync(
                    ["vm", "start", "--subscription", Required("SubscriptionId"),
                        "--resource-group", Required("ResourceGroup"), "--name", Required("Name"), "-o", "none"],
                    cancellationToken);
            }
            await EnsureJitSshAsync(
                document.RootElement.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("Azure VM 响应缺少资源 ID。"),
                document.RootElement.GetProperty("location").GetString()
                    ?? throw new InvalidOperationException("Azure VM 响应缺少区域。"),
                cancellationToken);
            await WaitForSshAsync(cancellationToken);
            return started;
        }
        catch
        {
            if (started)
            {
                await DeallocateAsync(CancellationToken.None);
            }
            throw;
        }
    }

    public async Task DeallocateAsync(CancellationToken cancellationToken) =>
        await RunAzAsync(
            ["vm", "deallocate", "--subscription", Required("SubscriptionId"),
                "--resource-group", Required("ResourceGroup"), "--name", Required("Name"), "-o", "none"],
            cancellationToken);

    private string Required(string key) =>
        configuration[$"AzureVm:{key}"]?.Trim() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"缺少 AzureVm:{key} 配置。");

    private async Task EnsureJitSshAsync(
        string vmId,
        string location,
        CancellationToken cancellationToken)
    {
        var publicIpText = (await httpClientFactory.CreateClient().GetStringAsync(
            "https://checkip.amazonaws.com",
            cancellationToken)).Trim();
        if (!IPAddress.TryParse(publicIpText, out var publicIp)
            || publicIp.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("无法确定用于 Azure JIT 的公网 IPv4 地址。");
        }
        var body = JsonSerializer.Serialize(new
        {
            virtualMachines = new[]
            {
                new
                {
                    id = vmId,
                    ports = new[]
                    {
                        new
                        {
                            number = 22,
                            duration = "PT3H",
                            allowedSourceAddressPrefix = publicIp.ToString()
                        }
                    }
                }
            },
            justification = "Alex Director Console one-command production"
        });
        var url = $"https://management.azure.com/subscriptions/{Required("SubscriptionId")}" +
            $"/resourceGroups/{Required("ResourceGroup")}/providers/Microsoft.Security/locations/{location}" +
            "/jitNetworkAccessPolicies/default/initiate?api-version=2020-01-01";
        var bodyFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(bodyFile, body, cancellationToken);
            await RunAzAsync(
                ["rest", "--method", "post", "--url", url, "--headers", "Content-Type=application/json",
                    "--body", $"@{bodyFile}", "-o", "none"],
                cancellationToken);
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }

    private async Task WaitForSshAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(Required("Host"), 22, cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is SocketException or TimeoutException)
            {
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        throw new TimeoutException("Azure JIT 已请求，但 SSH 端口在 5 分钟内仍不可达。");
    }

    private static async Task<string> RunAzAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "az")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("az.cmd");
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Azure CLI。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Azure CLI 执行失败：{error.Trim()}");
        }
        return output;
    }
}