---
name: script-breakdown
title: 剧本拆解
description: Break down a selected or project-discovered script into analysis, character, scene, and key prop resources. Use when the director asks to analyze a script, reanalyze a new script, or extract production entities and assets.
version: 2.3.0
allowed-tools: list_project_resources read_project_resource_contents run_script_breakdown
---
# 剧本拆解

## When to Use

用于分析界面当前选中或从当前项目自主发现的文本剧本，并建立分析稿、人物设定稿、场景设定稿和关键道具设定稿。

## Procedure

1. 界面当前资源是剧本时，使用其资产 ID。当前未选择剧本或选中其他类型资源时，调用 `list_project_resources` 查找 `script`；目标唯一或可由导演令明确匹配时自主选择，不要求导演手动切换界面。
2. 调用 `read_project_resource_contents` 读取目标剧本完整正文，确认所选资源正确。
3. 将目标剧本资产 ID 传给 `run_script_breakdown`，由 Script Agent 识别需要的制作实体并通过工具持久化。
4. 根据工具实际返回的资源数量汇报结果。

## Pitfalls

- 当前资源不是剧本时，不得直接使用其 ID；先查找并读取当前项目中的目标剧本。
- 不因界面选中 shot 或其他资源就要求导演手动切换剧本。
- 不得声称创建了工具未返回的资源。
- 技能输出是项目资源，不在技能页面维护运行产物列表。

## Verification

确认工具成功完成，并返回分析、人物、场景和道具 Asset 清单。
