using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.VoicePackages;

public interface IDefaultVoicePackageSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}

public sealed class DefaultVoicePackageSynchronizer(
    V2DbContext dbContext,
    IHostEnvironment hostEnvironment,
    TimeProvider timeProvider) : IDefaultVoicePackageSynchronizer
{
    private const string CosyVoice3Model = "FunAudioLLM/Fun-CosyVoice3-0.5B-2512";
    private const string GptWeightsPath =
        "GPT_SoVITS/pretrained_models/gsv-v2final-pretrained/s1bert25hz-5kh-longer-epoch=12-step=369668.ckpt";
    private const string SoVitsWeightsPath =
        "GPT_SoVITS/pretrained_models/gsv-v2final-pretrained/s2G2333k.pth";

    private static readonly DefaultVoicePackage[] Defaults =
    [
        new(
            Guid.Parse("f04cd313-e95f-4cf4-b538-0af871b5ec61"),
            Guid.Parse("3f11e847-e5a1-4d87-b3a8-b93c2b61e480"),
            "开放普通话·超文",
            "清晰自然的普通话中性声线，由 Piper Chaowen medium 生成参考音。",
            "chaowen-medium.wav",
            "今天阳光很好，我们一起去看看远方的风景吧。",
            "zh",
            "普通话",
            "自然、清晰、语速中等，适合日常对白和旁白。",
            "CC0-1.0 (OHF Voice Dataset)",
            "https://huggingface.co/rhasspy/piper-voices/tree/main/zh/zh_CN/chaowen/medium"),
        new(
            Guid.Parse("b5e423f1-82fc-42b6-bc2c-34551478911d"),
            Guid.Parse("179f4e42-af57-4569-b1de-b6465abf011e"),
            "开放女声·LJSpeech",
            "温暖清晰的美式英语女声，由 Piper LJSpeech medium 生成参考音。",
            "ljspeech-medium.wav",
            "The morning light is warm, and today feels full of possibility.",
            "en",
            "美式英语",
            "温暖、清晰、叙述感自然，适合旁白和沉稳角色。",
            "Public Domain (LJSpeech)",
            "https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_US/ljspeech/medium"),
        new(
            Guid.Parse("1dd9d4a3-1a5a-40c2-af14-29fc58617c4b"),
            Guid.Parse("9c4c0873-352a-4c7b-b500-db36ec0f267f"),
            "非商用普通话·小雅",
            "自然柔和的普通话女声，由 Piper Xiao Ya medium 生成参考音，仅限非商用项目。",
            "xiao-ya-medium.wav",
            "夜色渐渐安静下来，远处的灯光照亮了回家的路。",
            "zh",
            "普通话",
            "自然、柔和、语速中等，适合青年角色和叙事对白。",
            "Non-commercial use (BZNSYP)",
            "https://huggingface.co/rhasspy/piper-voices/tree/main/zh/zh_CN/xiao_ya/medium"),
        new(
            Guid.Parse("7c33f3fa-3c7b-4de2-a51b-046f67391652"),
            Guid.Parse("3916be0f-8da1-4730-8a54-19056f57eb12"),
            "CosyVoice·开放普通话·超文",
            "使用开放普通话参考音，通过 CosyVoice 3 0.5B 零样本复刻的中性声线。",
            "chaowen-medium.wav",
            "今天阳光很好，我们一起去看看远方的风景吧。",
            "zh",
            "普通话",
            "自然、清晰、语速中等，适合日常对白和旁白。",
            "CC0-1.0 (OHF Voice Dataset)",
            "https://huggingface.co/rhasspy/piper-voices/tree/main/zh/zh_CN/chaowen/medium",
            "cosyvoice",
            CosyVoice3Model),
        new(
            Guid.Parse("72e890dc-bb5e-4a07-a334-78d6cf72fa50"),
            Guid.Parse("873bcadb-565b-4b55-bfad-c65ef8f1196a"),
            "CosyVoice·AISHELL 南方女声 0693",
            "AISHELL-3 青年南方女声，通过 CosyVoice 3 0.5B 零样本复刻。",
            "aishell3-ssb0693.wav",
            "武术始终被看作我国的国粹",
            "zh",
            "普通话（南方口音）",
            "青年女声，南方口音，中性自然，适合日常对白。",
            "Apache-2.0 (AISHELL-3)",
            "https://www.openslr.org/93/",
            "cosyvoice",
            CosyVoice3Model),
        new(
            Guid.Parse("9c11994b-8c77-45f4-ae34-94cd10e6705f"),
            Guid.Parse("13a8f696-4476-4dce-a2d2-6a2750e92140"),
            "CosyVoice·AISHELL 南方男声 0736",
            "AISHELL-3 青年南方男声，通过 CosyVoice 3 0.5B 零样本复刻。",
            "aishell3-ssb0736.wav",
            "然而保持谨慎的态度是合宜的",
            "zh",
            "普通话（南方口音）",
            "青年男声，南方口音，沉稳克制，适合角色对白。",
            "Apache-2.0 (AISHELL-3)",
            "https://www.openslr.org/93/",
            "cosyvoice",
            CosyVoice3Model),
        new(
            Guid.Parse("3b751130-84b7-42ce-94be-9651f0734959"),
            Guid.Parse("d0515b62-c4d5-408f-ad3a-9196676bd6a9"),
            "CosyVoice·AISHELL 北方女声 0780",
            "AISHELL-3 青年北方女声，通过 CosyVoice 3 0.5B 零样本复刻。",
            "aishell3-ssb0780.wav",
            "为园区经济企业发展提供智力支持和战略支持",
            "zh",
            "普通话（北方口音）",
            "青年女声，北方口音，清晰正式，适合说明和叙事对白。",
            "Apache-2.0 (AISHELL-3)",
            "https://www.openslr.org/93/",
            "cosyvoice",
            CosyVoice3Model),
        new(
            Guid.Parse("b4cfe3e5-cbb8-44e7-b661-8108d172ef98"),
            Guid.Parse("437362bd-4719-470f-818e-25e6faf08dce"),
            "CosyVoice·AISHELL 北方男声 0590",
            "AISHELL-3 成年北方男声，通过 CosyVoice 3 0.5B 零样本复刻。",
            "aishell3-ssb0590.wav",
            "协会建议化学制药产业应从国家医药工业发展战略着眼",
            "zh",
            "普通话（北方口音）",
            "成年男声，北方口音，稳重正式，适合旁白和成熟角色。",
            "Apache-2.0 (AISHELL-3)",
            "https://www.openslr.org/93/",
            "cosyvoice",
            CosyVoice3Model),
        new(
            Guid.Parse("eeecdb71-a167-4c70-b358-ac0310935525"),
            Guid.Parse("0a4d3b0f-233c-46b4-938a-40abe96fd891"),
            "CosyVoice·非商用普通话·小雅",
            "使用非商用普通话参考音，通过 CosyVoice 3 0.5B 零样本复刻的柔和女声。",
            "xiao-ya-medium.wav",
            "夜色渐渐安静下来，远处的灯光照亮了回家的路。",
            "zh",
            "普通话",
            "自然、柔和、语速中等，适合青年角色和叙事对白。",
            "Non-commercial use (BZNSYP)",
            "https://huggingface.co/rhasspy/piper-voices/tree/main/zh/zh_CN/xiao_ya/medium",
            "cosyvoice",
            CosyVoice3Model)
    ];

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var resourceIds = Defaults.Select(item => item.ResourceId).ToArray();
        var existingResourceIds = await dbContext.VoicePackages
            .Where(item => resourceIds.Contains(item.ResourceId))
            .Select(item => item.ResourceId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var missing = Defaults
            .Where(item => !existingResourceIds.Contains(item.ResourceId))
            .ToArray();
        if (missing.Length == 0) return;

        var now = timeProvider.GetUtcNow();
        foreach (var definition in missing)
        {
            var path = Path.Combine(
                hostEnvironment.ContentRootPath,
                "VoicePackages",
                "Defaults",
                definition.ReferenceAudioFileName);
            var audio = await File.ReadAllBytesAsync(path, cancellationToken);
            VoiceWave.Validate(audio);
            var durationSeconds = VoiceWave.ReadDurationSeconds(audio);
            if (durationSeconds is < 3 or > 10)
                throw new InvalidOperationException($"内置语音包 {definition.Name} 的参考音必须为 3 到 10 秒。");

            dbContext.VoicePackages.Add(new VoicePackage
            {
                Id = definition.Id,
                ResourceId = definition.ResourceId,
                Version = 1,
                Name = definition.Name,
                Description = definition.Description,
                Engine = definition.Engine,
                BaseModelVersion = definition.BaseModelVersion,
                GptWeightsPath = definition.Engine == "gpt-sovits" ? GptWeightsPath : string.Empty,
                SoVitsWeightsPath = definition.Engine == "gpt-sovits" ? SoVitsWeightsPath : string.Empty,
                ReferenceAudioFileName = definition.ReferenceAudioFileName,
                ReferenceAudioContent = audio,
                ReferenceText = definition.ReferenceText,
                Language = definition.Language,
                Dialect = definition.Dialect,
                SpeakingStyle = definition.SpeakingStyle,
                License = definition.License,
                SourceUrl = definition.SourceUrl,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record DefaultVoicePackage(
        Guid Id,
        Guid ResourceId,
        string Name,
        string Description,
        string ReferenceAudioFileName,
        string ReferenceText,
        string Language,
        string Dialect,
        string SpeakingStyle,
        string License,
        string SourceUrl,
        string Engine = "gpt-sovits",
        string BaseModelVersion = "v2");
}