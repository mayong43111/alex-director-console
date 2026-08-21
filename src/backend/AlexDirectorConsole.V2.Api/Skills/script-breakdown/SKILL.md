---
name: script-breakdown
title: 剧本拆解
description: 从正式剧本建立人物、场景、道具资产，并生成视觉参考。
version: 1.1.0
allowed-tools: build_visual_assets generate_missing_visual_prompts generate_missing_visual_images
---
# 剧本拆解

## Goal
从当前正式剧本建立可供资产和分镜流程消费的人物、场景和道具资产，并生成视觉参考。

## Workflow
- 先调用 `build_visual_assets`，只从正式剧本中有证据的角色、场景和道具建立资产。
- 生成参考图时，对 `character`、`scene`、`prop` 依次调用 `generate_missing_visual_prompts`。
- `generate_missing_visual_images` 是分步长任务，每轮对话只调用一次，`maxItems=1`；调用后立即报告并结束本轮。`remaining` 大于 0 时，等待导演下一轮要求继续，不得在同一轮连续调用。
- 工具会跳过已有结果。逐类如实报告生成、已有、失败和剩余数量；存在失败或剩余时不得声称全部完成。

## Constraints
- 只提取剧本中有证据的事实。
- 缺少设定时标记待确认，不自行补写。
- 保留场次和来源定位。