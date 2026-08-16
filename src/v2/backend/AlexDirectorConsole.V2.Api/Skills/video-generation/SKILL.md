---
name: video-generation
title: 视频生成
description: 将已锁定镜头与首帧组织为可追踪的视频生成任务。
version: 1.0.0
allowed-tools: query_storyboard query_asset create_video_task query_production_run
---
# 视频生成

## Goal
创建幂等、可恢复、可审阅的视频生成任务。

## Constraints
- 输入镜头和首帧必须已锁定。
- 每次生成记录模型、参数和资产版本。
- 失败项只按明确错误修复后重试。