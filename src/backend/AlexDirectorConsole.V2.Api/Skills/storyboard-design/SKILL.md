---
name: storyboard-design
title: 分镜设计
description: 将当前正式剧本转换为结构化、可校验、可生产的镜头设计。
version: 2.1.0
allowed-tools: query_storyboard write_storyboard query_asset create_reference_plan
---
# 分镜设计

## Goal
根据剧本、视觉资产和时长约束生成结构化镜头。

## Constraints
- 每个镜头必须归属一个生产集和剧本场次。
- 不改写剧本对白与事件顺序。
- 写入前生成变更计划并校验时长。