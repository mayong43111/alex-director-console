using System.Net;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class CosyVoiceDialogueClientTests
{
    [Fact]
    public async Task Zero_shot_request_uses_official_multipart_contract_and_wraps_pcm_as_wav()
    {
        var handler = new CaptureHandler();
        var client = new CosyVoiceDialogueClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:50000/")
        });

        var result = await client.GenerateAsync(
            new(
                Guid.NewGuid(),
                "今儿天气真得劲。",
                "FunAudioLLM/Fun-CosyVoice3-0.5B-2512",
                CreateWave(),
                "henan.wav",
                "恁吃过饭了没有？"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/inference_zero_shot", handler.RequestUri?.AbsolutePath);
        Assert.Contains("name=tts_text", handler.Content);
        Assert.Contains("name=prompt_text", handler.Content);
        Assert.Contains("name=prompt_wav", handler.Content);
        Assert.Equal("RIFF"u8.ToArray(), result.Bytes[..4]);
        Assert.Equal(22050, result.SampleRate);
    }

    [Fact]
    public async Task Dispatcher_routes_CosyVoice_package_without_calling_GptSoVits()
    {
        var gptSoVits = new RecordingGptSoVitsClient();
        var cosyVoice = new RecordingCosyVoiceClient();
        var generator = new VoicePackageDialogueGenerator(gptSoVits, cosyVoice);

        await generator.GenerateAsync(
            new(
                Guid.NewGuid(),
                "cosyvoice",
                "测试对白",
                "FunAudioLLM/Fun-CosyVoice3-0.5B-2512",
                string.Empty,
                string.Empty,
                CreateWave(),
                "voice.wav",
                "参考文本",
                "zh",
                "zh",
                1),
            CancellationToken.None);

        Assert.Null(gptSoVits.LastRequest);
        Assert.Equal("FunAudioLLM/Fun-CosyVoice3-0.5B-2512", cosyVoice.LastRequest?.Model);
    }

    private static byte[] CreateWave()
    {
        var bytes = new byte[44];
        "RIFF"u8.CopyTo(bytes);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string Content { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Content = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0, 0, 0, 0])
            };
        }
    }

    private sealed class RecordingGptSoVitsClient : IGptSoVitsDialogueClient
    {
        public GptSoVitsDialogueRequest? LastRequest { get; private set; }

        public Task<GeneratedDialogueAudio> GenerateAsync(
            GptSoVitsDialogueRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new GeneratedDialogueAudio(CreateWave(), 22050, 0));
        }
    }

    private sealed class RecordingCosyVoiceClient : ICosyVoiceDialogueClient
    {
        public CosyVoiceDialogueRequest? LastRequest { get; private set; }

        public Task<GeneratedDialogueAudio> GenerateAsync(
            CosyVoiceDialogueRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new GeneratedDialogueAudio(CreateWave(), 22050, 0));
        }
    }
}