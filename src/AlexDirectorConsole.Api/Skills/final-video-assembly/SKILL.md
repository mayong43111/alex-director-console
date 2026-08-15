---
name: final-video-assembly
title: 最终成片组装
description: Assemble persisted shot videos and voice-over into a final narrated MP4. Use when the director asks to 拼接视频、合成配音、生成成片、输出最终视频 or assemble the final movie.
version: 1.0.0
allowed-tools: list_project_resources read_project_resources read_project_resource_contents generate_speech bind_shot_asset assemble_project_video
---
# 最终成片组装

## When to Use

用于把当前项目已持久化的镜头视频与旁白组装为真实 MP4 成片。只列出素材、给出剪辑建议、返回 FFmpeg 命令或声称“已合成”都不算完成，必须调用 `assemble_project_video` 并取得其返回的视频资产。

## Procedure

1. 调用 `list_project_resources` 核验镜头资源与媒体资源。镜头名称必须包含唯一、可排序的 `Sxx-xx` 镜号。
2. 确认每个目标镜头已有真实 `video` 绑定。不得用聊天记录中的生成结果、首帧图片或未绑定的视频名称代替持久化绑定。
3. 核验旁白：优先使用镜头 `other` 音频绑定；未绑定时，工具只允许使用名称中含唯一同镜号的音频。存在多个候选时先调用 `bind_shot_asset` 明确唯一音频，不能猜测。
4. 缺少旁白且导演要求全片配音时，从镜头正文提取实际朗读文本，按 `voice-over` 技能严格串行调用 `generate_speech`，成功后立即以 `other` 绑定到对应镜头。
5. 默认输出参数使用 `width=608`、`height=352`、`fps=24`。导演明确要求其他格式时再调整，宽高必须为偶数。
6. 全片配音使用 `requireNarration=true` 调用一次 `assemble_project_video`。工具会按镜号排序，旁白长于镜头时冻结尾帧，短于镜头时补静音，并在本地 FFmpeg 中生成 H.264/AAC MP4。
7. 只有工具返回 `video/mp4` 资产、正时长、正确镜头数和预期配音数后才声明成片完成。最终报告资源名称、资产 ID、时长、分辨率、FPS、镜头数和配音镜头数。

## Rules

- 不得依赖远程 ComfyUI VM 执行最终组装；该阶段必须使用本地 FFmpeg。
- 不得把 34 镜拆成多个临时成片后仅报告其中一个；最终工具应一次读取完整持久化输入。
- 不得按资产创建时间推断叙事顺序，只能按 `Sxx-xx` 镜号排序。
- 不得默默忽略重复镜号、缺失视频、音频歧义或无效媒体；修正持久化关系后重新调用。
- 工具返回的 `missingNarration` 非空时，不得声称全片已完成配音。
- 最终资产的来源元数据必须保留每镜 shot、video、audio 资产 ID 和音频匹配方式，供后续审计与重做。

## Verification

- `asset.contentType` 为 `video/mp4`，`asset.sizeBytes` 大于 1024。
- `durationSeconds > 0`，`shotCount` 等于目标镜头数。
- 全片配音时 `audioClipCount == shotCount` 且 `missingNarration` 为空。
- 生成元数据的 `operation` 为 `assemble-project-video`，`provider` 为 `local-ffmpeg`。