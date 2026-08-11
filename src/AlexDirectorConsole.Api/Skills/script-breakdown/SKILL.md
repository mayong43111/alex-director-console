---
name: script-breakdown
title: 剧本拆解
description: Break down a selected script into analysis, character, scene, and key prop resources. Use when the director asks to analyze or extract production entities from a script.
version: 2.2.0
allowed-tools: run_script_breakdown
---
# 剧本拆解

## When to Use

用于分析界面当前选中的文本剧本，并建立分析稿、人物设定稿、场景设定稿和关键道具设定稿。

## Procedure

1. 确认界面当前资源是文本剧本。
2. 调用 `run_script_breakdown`，由 Script Agent 自主读取剧本、识别需要的制作实体并通过工具持久化。
3. 根据工具实际返回的资源数量汇报结果。

## Pitfalls

- 当前资源不是剧本时不要调用。
- 不得声称创建了工具未返回的资源。
- 技能输出是项目资源，不在技能页面维护运行产物列表。

## Verification

确认工具成功完成，并返回分析、人物、场景和道具 Asset 清单。
