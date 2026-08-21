---
name: script-writing
title: 剧本创作
description: 编写分集故事，并按原分集直接生成可进入生产的正式剧本。
version: 1.1.0
allowed-tools: create_story_source generate_source_episode_script
---
# 剧本创作

## Goal
先保存导演要求的分集故事，再按原分集转成场次明确、对白完整、时长可校验的正式剧本。

## Workflow
- 写故事时结合当前项目设定完成内容，再调用 `create_story_source` 保存；每个 Markdown 一级标题对应一个原分集。
- 用户要求按原分集生成正式剧本时，直接调用 `generate_source_episode_script`，传入故事来源 ID 与原分集编号。
- `generate_source_episode_script` 固定使用 `source-chapters` 模式，不调用素材分析，不重新编排章节，不生成新的创意大纲。
- 只有工具返回 `created` 或 `generated` 后，才能报告对应步骤成功。

## Constraints
- 不替导演决定尚未确认的创作分歧。
- 保留来源事实与角色设定。
- 正式剧本由生产脚本流程执行时长和结构校验。