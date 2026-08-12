---
name: voice-over
title: 配音生成
description: Generate and persist voice-over audio from narration, dialogue, announcements, or other spoken text. Use when the director asks to 生成配音、旁白、对白音频、角色声音、朗读、TTS or text-to-speech.
version: 1.0.0
allowed-tools: list_project_resources read_project_resource_contents generate_speech bind_shot_asset
---
# 配音生成

## When to Use

用于把明确的旁白、对白、播报或朗读文本生成真实音频素材。只写配音建议、声音描述或 SSML 不算完成，必须调用 `generate_speech` 并保存音频。

## Procedure

1. 确定实际朗读文本：导演已给出完整文本时直接使用；导演指向当前剧本或 shot 时读取完整正文，只提取导演要求的旁白或对白，不朗读场景说明和动作描述。
2. 确定资源名称，应包含角色、场次、镜号或用途，例如“林墨 · S03-02 对白”或“全片旁白”。
3. 从导演令和上下文整理 `deliveryInstructions`，写清语言、角色年龄与身份、情绪、音色、节奏、重音和停顿；不得把这些表演说明混入实际朗读的 `text`。
4. 导演指定 voice 时使用其选择；未指定时从 `alloy、echo、fable、nova、onyx、shimmer` 中按角色与用途选择，并在最终回复中报告。默认 `speed=1.0`、`responseFormat=mp3`。
5. 调用 `generate_speech`。批量配音必须严格串行，一次只生成一条；每条保存成功后才能开始下一条。
6. 导演明确要求把配音关联到当前或指定 shot 时，调用 `bind_shot_asset`，role 使用 `other`。未要求关联时保留为项目媒体素材，不擅自绑定。
7. 最终只汇报实际生成的音频名称、voice 和数量；不得声称生成工具没有返回的音频。

## Rules

- 实际朗读文本必须逐字可核对；不得擅自增删产品数据、人物事实、免责声明或关键台词。
- `deliveryInstructions` 只控制演绎，不得要求模型朗读其中内容。
- 同一角色的连续对白默认保持同一 voice、语言、音色和语速，除非剧情或导演令要求变化。
- 当前 `tts` 部署不支持表演指令时，不得声称情绪、音色或停顿指令已被模型采用；工具元数据会明确标记是否应用。
- 工具返回 4xx 部署不存在、voice 无效或参数无效时，不得原样重试；直接按真实错误报告，只有修正配置或参数后才能再次调用。
- 长文本按自然段、场次或镜头拆分，单条不得超过工具限制；拆分后资源名必须可排序和追踪。
- 图片生成失败、聊天记录或已有音频名称不能替代本次工具返回的真实音频资产。

## Verification

- 每条 `generate_speech` 返回 `audio/*` 媒体资产和非空 `speechText`。
- 生成元数据包含 deployment、原文、voice、deliveryInstructions、speed、格式和 API 版本。
- 批量任务的成功数量与实际工具返回数量一致；失败项按名称单独报告。