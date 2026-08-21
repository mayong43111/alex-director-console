---
name: project-management
title: 项目管理
description: 通过副导演对话查询、创建和更新影视项目；删除项目必须由用户在项目中心手动确认执行。
version: 1.0.0
allowed-tools: list_projects read_project create_project update_project read_project_settings update_project_settings
---
# 项目管理

## Goal
根据用户意图查询、创建或更新 Alex 导演台中的影视项目。

## Workflow
- 查询全部项目时调用 `list_projects`。
- 查询单个项目时调用 `read_project`，不得猜测项目 ID 或当前数据。
- 创建项目前确认项目名称；描述可以为空。调用 `create_project` 后如实报告结果。
- 更新项目前先调用 `read_project`，仅修改用户明确要求的名称或描述，再调用 `update_project`。
- 查询或完善项目设定时先调用 `read_project_settings`。根据用户明确要求和项目主题形成字段补丁，再调用 `update_project_settings`；未提及但完成设定必需的创意字段，可给出与项目主题一致的专业方案。
- 项目设定补丁支持 `projectName`、`description`、`contentType`、`targetAudience`、`plannedEpisodeCount`、`targetEpisodeSeconds`、`aspectRatio`、`outputWidth`、`outputHeight`、`visualStyle`、`artDirection`、`characterDesign`、`colorPalette`、`cameraLanguage`、`soundStrategy`、`imagePromptPrefix`。只提交需要修改的字段。
- 用户要求删除项目时，不得调用工具或声称已经删除。明确提示用户必须在项目中心使用删除按钮手动操作。

## Constraints
- 不伪造项目、执行结果或数据库状态。
- 工具返回失败时直接说明原因，不得声称操作成功。
- 不执行项目删除，不提供规避人工确认的方式。