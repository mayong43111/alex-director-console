using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Infrastructure;

internal static class VoicePackageTestData
{
    public static async Task<VoicePackage> CreateAsync(V2ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var now = DateTimeOffset.UtcNow;
        var package = new VoicePackage
        {
            Name = "测试角色普通话",
            Description = "跨项目测试语音包",
            BaseModelVersion = "v2ProPlus",
            GptWeightsPath = "GPT_weights/test.ckpt",
            SoVitsWeightsPath = "SoVITS_weights/test.pth",
            ReferenceAudioFileName = "test.wav",
            ReferenceAudioContent = CreateWave(),
            ReferenceText = "巴黎，我来了。",
            Language = "zh",
            Dialect = "普通话",
            SpeakingStyle = "清晰自然的青年普通话声线，语速中等，情绪克制。",
            DefaultSpeed = 1,
            License = "test-only",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.VoicePackages.Add(package);
        await dbContext.SaveChangesAsync();
        return package;
    }

    private static byte[] CreateWave() =>
    [
        0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00,
        0x57, 0x41, 0x56, 0x45, 0x66, 0x6d, 0x74, 0x20,
        0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
        0x80, 0xbb, 0x00, 0x00, 0x00, 0x77, 0x01, 0x00,
        0x02, 0x00, 0x10, 0x00, 0x64, 0x61, 0x74, 0x61,
        0x00, 0x00, 0x00, 0x00
    ];
}