---
name: shot-first-frame
title: 镜头首帧
description: 根据锁定分镜和批准视觉资产组织首帧生成任务。
version: 1.0.0
allowed-tools: query_storyboard query_asset create_reference_plan create_image_task
---
# 镜头首帧

## Goal
为每个可生产镜头生成连续性一致的首帧任务。

## Constraints
- 只使用已批准或明确指定的视觉资产。
- 记录人物、场景和道具依赖。
- 缺失关键参考时阻断生成并报告。