---
name: script-breakdown
title: 剧本拆解
description: 从剧本中提取场次、人物、场景、道具和可生产约束。
version: 1.0.0
allowed-tools: read_project_resources query_asset write_breakdown
---
# 剧本拆解

## Goal
生成可供资产和分镜流程消费的结构化剧本拆解结果。

## Constraints
- 只提取剧本中有证据的事实。
- 缺少设定时标记待确认，不自行补写。
- 保留场次和来源定位。