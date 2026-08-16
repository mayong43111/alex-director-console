using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Services;

public interface IRemoteComfyUiService : IDisposable
{
    int GetHttpPort(ProjectRuntimeConfiguration configuration);
    Task<string> InspectAsync(ProjectRuntimeConfiguration configuration, CancellationToken cancellationToken);
    Task<string> ExecuteActionAsync(ProjectRuntimeConfiguration configuration, string action, CancellationToken cancellationToken);
    Task<string> ReadWorkflowAsync(ProjectRuntimeConfiguration configuration, string fileName, CancellationToken cancellationToken);
    Task<GeneratedVideo> AssembleSlideshowAsync(
        ProjectRuntimeConfiguration configuration,
        IReadOnlyList<byte[]> images,
        int width,
        int height,
        int fps,
        int durationSeconds,
        CancellationToken cancellationToken);
    Task<GeneratedVideo> AssembleVideoClipsAsync(
        ProjectRuntimeConfiguration configuration,
        IReadOnlyList<byte[]> clips,
        int width,
        int height,
        int fps,
        CancellationToken cancellationToken);
}

public sealed class RemoteComfyUiService(IHttpClientFactory httpClientFactory) : IRemoteComfyUiService
{
    private readonly ConcurrentDictionary<Guid, TunnelRegistration> tunnels = new();
    private readonly ConcurrentDictionary<int, Guid> portOwners = new();
    private readonly Dictionary<RemoteEndpointKey, Guid> remoteEndpointOwners = [];
    private readonly Dictionary<RemotePathKey, Guid> remotePathOwners = [];
    private readonly Dictionary<Guid, RemoteRegistration> projectRemoteRegistrations = [];
    private readonly object remoteOwnershipGate = new();
    private readonly SemaphoreSlim tunnelGate = new(1, 1);

    public int GetHttpPort(ProjectRuntimeConfiguration configuration) =>
        IsLocalHost(configuration.VmHost) ? configuration.ComfyUiPort : configuration.LocalProxyPort;

    public async Task<string> InspectAsync(
        ProjectRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsLocalHost(configuration.VmHost))
        {
            EnsureTunnelOwnership(configuration);
            EnsureRemoteOwnership(configuration);
        }
        var client = httpClientFactory.CreateClient("ComfyUiProxy");
        var baseUri = GetProxyBaseUri(configuration);
        try
        {
            var systemStats = await client.GetStringAsync(new Uri(baseUri, "/system_stats"), cancellationToken);
            var queue = await client.GetStringAsync(new Uri(baseUri, "/queue"), cancellationToken);
            var workflows = await client.GetStringAsync(
                new Uri(baseUri, "/userdata?dir=workflows&recurse=true"), cancellationToken);
            var unetLoader = await client.GetStringAsync(new Uri(baseUri, "/object_info/UNETLoader"), cancellationToken);
            var clipLoader = await client.GetStringAsync(new Uri(baseUri, "/object_info/CLIPLoader"), cancellationToken);
            var vaeLoader = await client.GetStringAsync(new Uri(baseUri, "/object_info/VAELoader"), cancellationToken);
            var h3Node = await client.GetStringAsync(new Uri(baseUri, "/object_info/MiniMaxH3ImageToVideo"), cancellationToken);
            return JsonSerializer.Serialize(new
            {
                proxy = baseUri.ToString(),
                systemStats = JsonSerializer.Deserialize<JsonElement>(systemStats),
                queue = JsonSerializer.Deserialize<JsonElement>(queue),
                workflows = JsonSerializer.Deserialize<JsonElement>(workflows),
                nodes = new
                {
                    unetLoader = JsonSerializer.Deserialize<JsonElement>(unetLoader),
                    clipLoader = JsonSerializer.Deserialize<JsonElement>(clipLoader),
                    vaeLoader = JsonSerializer.Deserialize<JsonElement>(vaeLoader),
                    minimaxH3ImageToVideo = JsonSerializer.Deserialize<JsonElement>(h3Node)
                }
            });
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                IsLocalHost(configuration.VmHost)
                    ? $"本机 ComfyUI 不可用（{baseUri}）。请确认本机 ComfyUI 已启动并监听配置端口。"
                    : $"ComfyUI HTTP 代理不可用（{baseUri}）。请先调用 manage_remote_comfyui(action=start-tunnel)，不要重复建立隧道。",
                exception);
        }
    }

    public async Task<string> ExecuteActionAsync(
        ProjectRuntimeConfiguration configuration,
        string action,
        CancellationToken cancellationToken)
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        if (normalizedAction == "start-tunnel") return await StartTunnelAsync(configuration, cancellationToken);
        if (normalizedAction == "stop-tunnel") return await StopTunnelAsync(configuration, cancellationToken);
        ClaimRemoteOwnership(configuration);

        var comfyPath = Quote(configuration.ComfyUiPath);
        var pythonPath = Quote(configuration.ComfyUiPythonPath);
        var logPath = Quote(configuration.ComfyUiPath.TrimEnd('/') + "/comfyui-agent.log");
        var startCommand = $"cd {comfyPath} && nohup {pythonPath} main.py --listen 127.0.0.1 --port {configuration.ComfyUiPort} > {logPath} 2>&1 < /dev/null & echo $!";
        var command = normalizedAction switch
        {
            "start" => startCommand,
            "stop" => $"pids=$(pgrep -f '[m]ain.py.*--port[ =]{configuration.ComfyUiPort}'); test -z \"$pids\" || kill $pids",
            "restart" => $"pids=$(pgrep -f '[m]ain.py.*--port[ =]{configuration.ComfyUiPort}'); test -z \"$pids\" || kill $pids; {startCommand}",
            "update" => $"git -C {comfyPath} pull --ff-only",
            _ => throw new ArgumentException("不支持的远程动作。可用动作：start、stop、restart、update、start-tunnel、stop-tunnel。", nameof(action))
        };
        return await RunSshAsync(configuration, command, cancellationToken);
    }

    public async Task<string> ReadWorkflowAsync(
        ProjectRuntimeConfiguration configuration,
        string fileName,
        CancellationToken cancellationToken)
    {
        var normalizedFileName = Path.GetFileName(fileName.Trim());
        if (normalizedFileName != fileName.Trim() || !normalizedFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("workflowFileName 必须是 workflow 目录下的 JSON 文件名。", nameof(fileName));
        }
        var workflowPath = Path.Combine(
            AppContext.BaseDirectory,
            "Skills",
            "minimax-h3-video",
            "workflows",
            normalizedFileName);
        if (!File.Exists(workflowPath))
            throw new FileNotFoundException($"API 内置 workflow 不存在：{normalizedFileName}", workflowPath);
        return await File.ReadAllTextAsync(workflowPath, cancellationToken);
    }

    public async Task<GeneratedVideo> AssembleSlideshowAsync(
        ProjectRuntimeConfiguration configuration,
        IReadOnlyList<byte[]> images,
        int width,
        int height,
        int fps,
        int durationSeconds,
        CancellationToken cancellationToken)
    {
        ClaimRemoteOwnership(configuration);
        var jobId = $"alex-slideshow-{Guid.NewGuid():N}";
        var localDirectory = Path.Combine(Path.GetTempPath(), jobId);
        var remoteDirectory = $"/tmp/{jobId}";
        Directory.CreateDirectory(localDirectory);
        try
        {
            var secondsPerImage = durationSeconds / (double)images.Count;
            var concat = new StringBuilder();
            for (var index = 0; index < images.Count; index++)
            {
                var fileName = $"{index:D3}.png";
                await File.WriteAllBytesAsync(Path.Combine(localDirectory, fileName), images[index], cancellationToken);
                concat.AppendLine($"file '{fileName}'");
                concat.AppendLine($"duration {secondsPerImage.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}");
            }
            concat.AppendLine($"file '{images.Count - 1:D3}.png'");
            await File.WriteAllTextAsync(Path.Combine(localDirectory, "concat.txt"), concat.ToString(), cancellationToken);

            await RunSshAsync(configuration, $"mkdir -p -- {Quote(remoteDirectory)} && command -v ffmpeg >/dev/null && command -v ffprobe >/dev/null", cancellationToken);
            await RunScpAsync(configuration, Directory.GetFiles(localDirectory), remoteDirectory + "/", cancellationToken);
            var outputPath = remoteDirectory + "/output.mp4";
            var filter = $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1,format=yuv420p";
            var assembleCommand = $"cd {Quote(remoteDirectory)} && ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i concat.txt -vf {Quote(filter)} -r {fps} -t {durationSeconds} -an -c:v libx264 -preset medium -crf 20 -movflags +faststart output.mp4";
            await RunSshAsync(configuration, assembleCommand, cancellationToken);
            var probe = await RunSshAsync(configuration,
                $"ffprobe -v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -show_entries format=duration -of default=noprint_wrappers=1 {Quote(outputPath)}", cancellationToken);
            if (!probe.Contains($"width={width}", StringComparison.Ordinal)
                || !probe.Contains($"height={height}", StringComparison.Ordinal)
                || !probe.Contains($"r_frame_rate={fps}/1", StringComparison.Ordinal))
                throw new InvalidOperationException($"静帧成片媒体参数校验失败：{probe}");
            var durationLine = probe.Split('\n').FirstOrDefault(line => line.StartsWith("duration=", StringComparison.Ordinal));
            if (durationLine is null
                || !double.TryParse(durationLine["duration=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var actualDuration)
                || Math.Abs(actualDuration - durationSeconds) > 0.1)
                throw new InvalidOperationException($"静帧成片时长校验失败：目标 {durationSeconds}s，探测结果 {probe}");

            var localOutput = Path.Combine(localDirectory, "output.mp4");
            await RunScpAsync(configuration, [$"{configuration.VmUsername}@{configuration.VmHost}:{outputPath}"], localDirectory, cancellationToken, remoteSource: true);
            var bytes = await File.ReadAllBytesAsync(localOutput, cancellationToken);
            if (bytes.Length < 1024 || bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 4, 4) != "ftyp")
                throw new InvalidOperationException("下载的静帧成片不是有效 MP4。文件为空、过小或缺少 ftyp 签名。");
            return new GeneratedVideo(bytes, "slideshow.mp4", "video/mp4");
        }
        finally
        {
            try { await RunSshAsync(configuration, $"rm -rf -- {Quote(remoteDirectory)}", CancellationToken.None); } catch { }
            try { Directory.Delete(localDirectory, recursive: true); } catch { }
        }
    }

    public async Task<GeneratedVideo> AssembleVideoClipsAsync(
        ProjectRuntimeConfiguration configuration,
        IReadOnlyList<byte[]> clips,
        int width,
        int height,
        int fps,
        CancellationToken cancellationToken)
    {
        ClaimRemoteOwnership(configuration);
        var jobId = $"alex-video-concat-{Guid.NewGuid():N}";
        var localDirectory = Path.Combine(Path.GetTempPath(), jobId);
        var remoteDirectory = $"/tmp/{jobId}";
        Directory.CreateDirectory(localDirectory);
        try
        {
            var concat = new StringBuilder();
            for (var index = 0; index < clips.Count; index++)
            {
                var fileName = $"{index:D3}.mp4";
                await File.WriteAllBytesAsync(Path.Combine(localDirectory, fileName), clips[index], cancellationToken);
                concat.AppendLine($"file '{fileName}'");
            }
            await File.WriteAllTextAsync(Path.Combine(localDirectory, "concat.txt"), concat.ToString(), cancellationToken);

            await RunSshAsync(configuration, $"mkdir -p -- {Quote(remoteDirectory)} && command -v ffmpeg >/dev/null && command -v ffprobe >/dev/null", cancellationToken);
            await RunScpAsync(configuration, Directory.GetFiles(localDirectory), remoteDirectory + "/", cancellationToken);
            var outputPath = remoteDirectory + "/output.mp4";
            var filter = $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1,fps={fps},format=yuv420p";
            var assembleCommand = $"cd {Quote(remoteDirectory)} && ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i concat.txt -vf {Quote(filter)} -an -c:v libx264 -preset medium -crf 20 -movflags +faststart output.mp4";
            await RunSshAsync(configuration, assembleCommand, cancellationToken);
            var probe = await RunSshAsync(configuration,
                $"ffprobe -v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -show_entries format=duration -of default=noprint_wrappers=1 {Quote(outputPath)}", cancellationToken);
            if (!probe.Contains($"width={width}", StringComparison.Ordinal)
                || !probe.Contains($"height={height}", StringComparison.Ordinal)
                || !probe.Contains($"r_frame_rate={fps}/1", StringComparison.Ordinal))
                throw new InvalidOperationException($"视频成片媒体参数校验失败：{probe}");
            var durationLine = probe.Split('\n').FirstOrDefault(line => line.StartsWith("duration=", StringComparison.Ordinal));
            if (durationLine is null
                || !double.TryParse(durationLine["duration=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var actualDuration)
                || actualDuration <= 0)
                throw new InvalidOperationException($"视频成片时长校验失败：{probe}");

            var localOutput = Path.Combine(localDirectory, "output.mp4");
            await RunScpAsync(configuration, [$"{configuration.VmUsername}@{configuration.VmHost}:{outputPath}"], localDirectory, cancellationToken, remoteSource: true);
            var bytes = await File.ReadAllBytesAsync(localOutput, cancellationToken);
            if (bytes.Length < 1024 || Encoding.ASCII.GetString(bytes, 4, 4) != "ftyp")
                throw new InvalidOperationException("下载的视频成片不是有效 MP4。文件为空、过小或缺少 ftyp 签名。");
            return new GeneratedVideo(bytes, "assembled-video.mp4", "video/mp4");
        }
        finally
        {
            try { await RunSshAsync(configuration, $"rm -rf -- {Quote(remoteDirectory)}", CancellationToken.None); } catch { }
            try { Directory.Delete(localDirectory, recursive: true); } catch { }
        }
    }

    private async Task<string> StartTunnelAsync(
        ProjectRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (IsLocalHost(configuration.VmHost))
            return $"connection=local url={GetProxyBaseUri(configuration)}";

        await tunnelGate.WaitAsync(cancellationToken);
        try
        {
            ClaimRemoteOwnership(configuration);
            if (tunnels.TryGetValue(configuration.ProjectId, out var existing) && !existing.Process.HasExited)
            {
                if (existing.LocalPort != configuration.LocalProxyPort)
                    throw new InvalidOperationException("当前项目已有使用其他本地端口的隧道，请先停止该隧道再更新配置。");
                EnsureTunnelOwnership(configuration);
                if (await IsProxyAvailableAsync(configuration, cancellationToken))
                    return $"tunnel=running local={GetProxyBaseUri(configuration)}";
                await WaitForTunnelAsync(existing.Process, configuration.LocalProxyPort, cancellationToken);
                return $"tunnel=running local=http://127.0.0.1:{configuration.LocalProxyPort}";
            }

            RemoveTunnelRegistration(configuration.ProjectId, kill: false);
            if (portOwners.TryGetValue(configuration.LocalProxyPort, out var ownerProjectId)
                && ownerProjectId != configuration.ProjectId)
            {
                throw new InvalidOperationException("本地代理端口已由其他项目的隧道占用，请为当前项目配置独立端口。");
            }
            portOwners[configuration.LocalProxyPort] = configuration.ProjectId;

            try
            {
                var startInfo = CreateSshStartInfo(configuration);
                startInfo.ArgumentList.Add("-N");
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add("ExitOnForwardFailure=yes");
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add("ServerAliveInterval=30");
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add("ServerAliveCountMax=3");
                startInfo.ArgumentList.Add("-L");
                startInfo.ArgumentList.Add($"127.0.0.1:{configuration.LocalProxyPort}:127.0.0.1:{configuration.ComfyUiPort}");
                AddDestination(startInfo, configuration);
                var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 SSH 隧道进程。");
                tunnels[configuration.ProjectId] = new(process, configuration.LocalProxyPort);
                await WaitForTunnelAsync(process, configuration.LocalProxyPort, cancellationToken);
                return $"tunnel=started local=http://127.0.0.1:{configuration.LocalProxyPort}";
            }
            catch
            {
                RemoveTunnelRegistration(configuration.ProjectId, kill: true);
                ReleaseRemoteOwnership(configuration.ProjectId);
                throw;
            }
        }
        finally
        {
            tunnelGate.Release();
        }
    }

    private void EnsureTunnelOwnership(ProjectRuntimeConfiguration configuration)
    {
        if (!tunnels.TryGetValue(configuration.ProjectId, out var tunnel)
            || tunnel.LocalPort != configuration.LocalProxyPort
            || tunnel.Process.HasExited
            || !portOwners.TryGetValue(configuration.LocalProxyPort, out var ownerProjectId)
            || ownerProjectId != configuration.ProjectId)
        {
            throw new InvalidOperationException("当前项目没有该本地代理端口的活动隧道，请先启动当前项目的隧道。");
        }
    }

    private void ClaimRemoteOwnership(ProjectRuntimeConfiguration configuration)
    {
        var registration = RemoteRegistration.From(configuration);
        lock (remoteOwnershipGate)
        {
            if (projectRemoteRegistrations.TryGetValue(configuration.ProjectId, out var existing)
                && existing != registration)
            {
                throw new InvalidOperationException("当前项目已有其他远端 ComfyUI 配置正在使用，请先停止隧道再更新配置。");
            }
            if ((remoteEndpointOwners.TryGetValue(registration.Endpoint, out var endpointOwner)
                    && endpointOwner != configuration.ProjectId)
                || (remotePathOwners.TryGetValue(registration.Path, out var pathOwner)
                    && pathOwner != configuration.ProjectId))
            {
                throw new InvalidOperationException("该远端 ComfyUI 端口或目录正由其他项目使用。");
            }

            remoteEndpointOwners[registration.Endpoint] = configuration.ProjectId;
            remotePathOwners[registration.Path] = configuration.ProjectId;
            projectRemoteRegistrations[configuration.ProjectId] = registration;
        }
    }

    private void EnsureRemoteOwnership(ProjectRuntimeConfiguration configuration)
    {
        var registration = RemoteRegistration.From(configuration);
        lock (remoteOwnershipGate)
        {
            if (!projectRemoteRegistrations.TryGetValue(configuration.ProjectId, out var existing)
                || existing != registration
                || !remoteEndpointOwners.TryGetValue(registration.Endpoint, out var endpointOwner)
                || endpointOwner != configuration.ProjectId
                || !remotePathOwners.TryGetValue(registration.Path, out var pathOwner)
                || pathOwner != configuration.ProjectId)
            {
                throw new InvalidOperationException("当前项目没有该远端 ComfyUI 实例的使用权。");
            }
        }
    }

    private void ReleaseRemoteOwnership(Guid projectId)
    {
        lock (remoteOwnershipGate)
        {
            if (!projectRemoteRegistrations.Remove(projectId, out var registration)) return;
            if (remoteEndpointOwners.GetValueOrDefault(registration.Endpoint) == projectId)
                remoteEndpointOwners.Remove(registration.Endpoint);
            if (remotePathOwners.GetValueOrDefault(registration.Path) == projectId)
                remotePathOwners.Remove(registration.Path);
        }
    }

    private async Task<bool> IsProxyAvailableAsync(
        ProjectRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ComfyUiProxy");
        try
        {
            using var response = await client.GetAsync(
                new Uri(GetProxyBaseUri(configuration), "/system_stats"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private Uri GetProxyBaseUri(ProjectRuntimeConfiguration configuration) =>
        new($"http://127.0.0.1:{GetHttpPort(configuration)}");

    private static async Task WaitForTunnelAsync(
        Process process,
        int localPort,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (process.HasExited)
            {
                var error = (await process.StandardError.ReadToEndAsync(cancellationToken)).Trim();
                throw new InvalidOperationException($"SSH 隧道启动失败（exit {process.ExitCode}）：{error}");
            }

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", localPort, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new TimeoutException($"等待 SSH 隧道监听 127.0.0.1:{localPort} 超时。");
    }

    private async Task<string> StopTunnelAsync(
        ProjectRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (IsLocalHost(configuration.VmHost)) return "connection=local tunnel=not-required";

        await tunnelGate.WaitAsync(cancellationToken);
        try
        {
            var stopped = RemoveTunnelRegistration(configuration.ProjectId, kill: true);
            ReleaseRemoteOwnership(configuration.ProjectId);
            return stopped ? "tunnel=stopped" : "tunnel=not-running";
        }
        finally
        {
            tunnelGate.Release();
        }
    }

    private bool RemoveTunnelRegistration(Guid projectId, bool kill)
    {
        if (!tunnels.TryRemove(projectId, out var tunnel)) return false;
        portOwners.TryRemove(new KeyValuePair<int, Guid>(tunnel.LocalPort, projectId));
        if (kill && !tunnel.Process.HasExited) tunnel.Process.Kill(true);
        tunnel.Process.Dispose();
        return true;
    }

    private static async Task<string> RunSshAsync(
        ProjectRuntimeConfiguration configuration,
        string command,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateSshStartInfo(configuration);
        AddDestination(startInfo, configuration);
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 ssh。请确认系统已安装 OpenSSH Client。");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForProcessAsync(process, "SSH 命令", cancellationToken);
        var output = (await standardOutput).Trim();
        var error = (await standardError).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"SSH 命令失败（exit {process.ExitCode}）：{error}");
        }
        return string.IsNullOrWhiteSpace(error) ? output : $"{output}\n[stderr]\n{error}";
    }

    private static async Task RunScpAsync(
        ProjectRuntimeConfiguration configuration,
        IReadOnlyList<string> sources,
        string destination,
        CancellationToken cancellationToken,
        bool remoteSource = false)
    {
        var startInfo = new ProcessStartInfo("scp")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(configuration.VmPort.ToString());
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(Environment.ExpandEnvironmentVariables(configuration.SshPrivateKeyPath));
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ConnectTimeout=15");
        foreach (var source in sources) startInfo.ArgumentList.Add(source);
        startInfo.ArgumentList.Add(remoteSource ? destination : $"{configuration.VmUsername}@{configuration.VmHost}:{destination}");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 scp。");
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForProcessAsync(process, "SCP", cancellationToken);
        var error = (await standardError).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException($"SCP 失败（exit {process.ExitCode}）：{error}");
    }

    private static async Task WaitForProcessAsync(
        Process process,
        string operation,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{operation}执行超过 90 秒，已终止子进程。");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static ProcessStartInfo CreateSshStartInfo(ProjectRuntimeConfiguration configuration)
    {
        var startInfo = new ProcessStartInfo("ssh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(configuration.VmPort.ToString());
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(Environment.ExpandEnvironmentVariables(configuration.SshPrivateKeyPath));
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ConnectTimeout=15");
        return startInfo;
    }

    private static void AddDestination(ProcessStartInfo startInfo, ProjectRuntimeConfiguration configuration) =>
        startInfo.ArgumentList.Add($"{configuration.VmUsername}@{configuration.VmHost}");

    private static bool IsLocalHost(string host)
    {
        var normalizedHost = host.Trim().Trim('[', ']');
        return normalizedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(normalizedHost, out var address) && IPAddress.IsLoopback(address));
    }

    private static string Quote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    public void Dispose()
    {
        foreach (var projectId in tunnels.Keys) RemoveTunnelRegistration(projectId, kill: true);
        lock (remoteOwnershipGate)
        {
            remoteEndpointOwners.Clear();
            remotePathOwners.Clear();
            projectRemoteRegistrations.Clear();
        }
        tunnelGate.Dispose();
    }

    private sealed record TunnelRegistration(Process Process, int LocalPort);
    private sealed record RemoteEndpointKey(string VmHost, int VmPort, int ComfyUiPort);
    private sealed record RemotePathKey(string VmHost, int VmPort, string ComfyUiPath);
    private sealed record RemoteRegistration(RemoteEndpointKey Endpoint, RemotePathKey Path)
    {
        public static RemoteRegistration From(ProjectRuntimeConfiguration configuration)
        {
            var host = configuration.VmHost.Trim().ToLowerInvariant();
            return new(
                new(host, configuration.VmPort, configuration.ComfyUiPort),
                new(host, configuration.VmPort, RemoteUnixPath.Normalize(configuration.ComfyUiPath)));
        }
    }
}

public static class RemoteUnixPath
{
    public static string Normalize(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("ComfyUI 目录必须是远端 Linux 主机上的绝对路径，不能使用 ~ 或相对路径。");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("ComfyUI 目录不能是根目录，也不能包含 . 或 .. 路径段。");
        return "/" + string.Join('/', segments);
    }
}