---
name: shot-first-frame
title: 镜头首帧
description: 根据当前分镜和当前视觉资产组织首帧生成任务。
version: 1.1.0
allowed-tools: generate_missing_storyboard_image_prompts generate_next_storyboard_first_frame
---
# 镜头首帧

## Goal
为每个可生产镜头生成连续性一致的首帧任务。

## Constraints
- 只使用当前或明确指定的视觉资产版本。
- 记录人物、场景和道具依赖。
- 缺失关键参考时阻断生成并报告。
- 先调用一次 `generate_missing_storyboard_image_prompts` 补齐提示词。
- 每轮只调用一次 `generate_next_storyboard_first_frame`，生成一张后立即报告并结束本轮。
- `remaining` 大于 0 时等待下一轮继续，绝不调用任何视频生成工具。