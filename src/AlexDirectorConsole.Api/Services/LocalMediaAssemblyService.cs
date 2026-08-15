using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AlexDirectorConsole.Api.Services;

public sealed record MediaAssemblyClip(
    string ShotCode,
    byte[] VideoBytes,
    byte[]? AudioBytes,
    string AudioExtension);

public sealed record MediaAssemblyResult(
    byte[] Bytes,
    double DurationSeconds,
    int Width,
    int Height,
    int Fps,
    int AudioClipCount,
    double TransitionDurationSeconds);

public interface ILocalMediaAssemblyService
{
    Task<MediaAssemblyResult> AssembleAsync(
        IReadOnlyList<MediaAssemblyClip> clips,
        int width,
        int height,
        int fps,
        CancellationToken cancellationToken);
}

public sealed class LocalMediaAssemblyService : ILocalMediaAssemblyService
{
    private const double FastFadeDurationSeconds = 0.25;

    public async Task<MediaAssemblyResult> AssembleAsync(
        IReadOnlyList<MediaAssemblyClip> clips,
        int width,
        int height,
        int fps,
        CancellationToken cancellationToken)
    {
        if (clips.Count is < 1 or > 100)
            throw new ArgumentException("组装素材数量必须为 1 到 100。", nameof(clips));

        var ffmpeg = FindExecutable("FFMPEG_PATH", "ffmpeg");
        var ffprobe = FindExecutable("FFPROBE_PATH", "ffprobe");
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"alex-final-video-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var segmentPaths = new List<string>(clips.Count);
            var segmentDurations = new List<double>(clips.Count);
            var audioClipCount = 0;
            for (var index = 0; index < clips.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clip = clips[index];
                var videoPath = Path.Combine(workingDirectory, $"{index:D3}-video.mp4");
                await File.WriteAllBytesAsync(videoPath, clip.VideoBytes, cancellationToken);
                var videoDuration = await ProbeDurationAsync(ffprobe, videoPath, cancellationToken);

                string? audioPath = null;
                var audioDuration = 0d;
                if (clip.AudioBytes is { Length: > 0 })
                {
                    audioPath = Path.Combine(workingDirectory, $"{index:D3}-audio{NormalizeExtension(clip.AudioExtension)}");
                    await File.WriteAllBytesAsync(audioPath, clip.AudioBytes, cancellationToken);
                    audioDuration = await ProbeDurationAsync(ffprobe, audioPath, cancellationToken);
                    audioClipCount++;
                }

                var duration = Math.Max(videoDuration, audioDuration);
                var extensionDuration = Math.Max(0, duration - videoDuration);
                var segmentPath = Path.Combine(workingDirectory, $"{index:D3}-segment.mp4");
                var videoFilter = FormattableString.Invariant(
                    $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},tpad=stop_mode=clone:stop_duration={extensionDuration:0.######},trim=duration={duration:0.######},setpts=PTS-STARTPTS");
                var arguments = new List<string>
                {
                    "-hide_banner", "-loglevel", "error", "-y", "-i", videoPath
                };
                if (audioPath is not null)
                {
                    arguments.AddRange(["-i", audioPath, "-filter_complex", $"[0:v]{videoFilter}[v];[1:a]apad,atrim=duration={duration.ToString("0.######", CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS[a]", "-map", "[v]", "-map", "[a]"]);
                }
                else
                {
                    arguments.AddRange(["-f", "lavfi", "-t", duration.ToString("0.######", CultureInfo.InvariantCulture), "-i", "anullsrc=channel_layout=stereo:sample_rate=48000", "-filter_complex", $"[0:v]{videoFilter}[v]", "-map", "[v]", "-map", "1:a"]);
                }
                arguments.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", "-shortest", segmentPath]);
                await RunAsync(ffmpeg, arguments, cancellationToken);
                segmentPaths.Add(segmentPath);
                segmentDurations.Add(duration);
            }

            var outputPath = Path.Combine(workingDirectory, "final.mp4");
            var transitionDuration = segmentPaths.Count > 1
                ? Math.Min(FastFadeDurationSeconds, segmentDurations.Min() / 2)
                : 0;
            if (segmentPaths.Count == 1)
            {
                await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-i", segmentPaths[0], "-c", "copy", "-movflags", "+faststart", outputPath], cancellationToken);
            }
            else
            {
                var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
                foreach (var segmentPath in segmentPaths)
                    arguments.AddRange(["-i", segmentPath]);

                var filters = new List<string>((segmentPaths.Count - 1) * 2);
                var previousVideo = "0:v";
                var previousAudio = "0:a";
                var combinedDuration = segmentDurations[0];
                for (var index = 1; index < segmentPaths.Count; index++)
                {
                    var videoOutput = $"v{index}";
                    var audioOutput = $"a{index}";
                    var offset = combinedDuration - transitionDuration;
                    filters.Add(FormattableString.Invariant(
                        $"[{previousVideo}][{index}:v]xfade=transition=fade:duration={transitionDuration:0.######}:offset={offset:0.######}[{videoOutput}]"));
                    filters.Add(FormattableString.Invariant(
                        $"[{previousAudio}][{index}:a]acrossfade=d={transitionDuration:0.######}:c1=tri:c2=tri[{audioOutput}]"));
                    previousVideo = videoOutput;
                    previousAudio = audioOutput;
                    combinedDuration += segmentDurations[index] - transitionDuration;
                }

                arguments.AddRange([
                    "-filter_complex", string.Join(';', filters),
                    "-map", $"[{previousVideo}]",
                    "-map", $"[{previousAudio}]",
                    "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
                    "-movflags", "+faststart", outputPath
                ]);
                await RunAsync(ffmpeg, arguments, cancellationToken);
            }
            var outputDuration = await ProbeDurationAsync(ffprobe, outputPath, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            if (bytes.Length < 1024 || bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 4, 4) != "ftyp")
                throw new InvalidOperationException("本地组装结果不是有效 MP4。文件为空、过小或缺少 ftyp 签名。");

            return new MediaAssemblyResult(
                bytes,
                outputDuration,
                width,
                height,
                fps,
                audioClipCount,
                transitionDuration);
        }
        finally
        {
            try { Directory.Delete(workingDirectory, recursive: true); } catch { }
        }
    }

    private static string FindExecutable(string environmentVariable, string command)
    {
        var configuredPath = Environment.GetEnvironmentVariable(environmentVariable)?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        if (OperatingSystem.IsWindows())
        {
            var packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WinGet",
                "Packages");
            if (Directory.Exists(packageRoot))
            {
                var executableName = command + ".exe";
                var wingetPath = Directory
                    .EnumerateDirectories(packageRoot, "Gyan.FFmpeg_*", SearchOption.TopDirectoryOnly)
                    .SelectMany(directory => Directory.EnumerateFiles(directory, executableName, SearchOption.AllDirectories))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (wingetPath is not null)
                    return wingetPath;
            }
        }
        return command;
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = Path.GetExtension(extension);
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 10 ? ".audio" : normalized;
    }

    private static async Task<double> ProbeDurationAsync(
        string ffprobe,
        string path,
        CancellationToken cancellationToken)
    {
        var output = await RunAsync(ffprobe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path], cancellationToken);
        if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || duration <= 0)
            throw new InvalidOperationException($"无法读取媒体时长：{Path.GetFileName(path)}；ffprobe={output.Trim()}");
        return duration;
    }

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"无法启动 {executable}。");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException($"找不到 {executable}。请安装 FFmpeg，或配置 FFMPEG_PATH/FFPROBE_PATH。", exception);
        }

        using (process)
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await standardOutput;
            var error = await standardError;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(executable)} 执行失败（{process.ExitCode}）：{error.Trim()}");
            return output;
        }
    }
}