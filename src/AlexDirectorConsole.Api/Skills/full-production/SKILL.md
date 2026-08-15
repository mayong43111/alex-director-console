---
name: full-production
title: 一句话出片
description: Create, resume, and inspect a durable full-production run covering shot frames, MiniMax H3 videos, voice-over, and final MP4 assembly. Use when the director asks for 一句话出片、一键出片、完整制作、全片制作 or end-to-end production.
version: 0.1.0
allowed-tools: start_full_production get_full_production_status
---
# 一句话出片

## Current Availability

本技能定义持久化全流程入口。只有 `start_full_production` 与 `get_full_production_status` 两个编排工具均可用时才能执行；任一工具不可用时，明确说明后台生产编排尚未启用，不得退化为在单次对话请求中循环执行全部图片、视频、配音和合成工具，也不得要求启动远程 VM。

## Procedure

1. 从导演令提取目标剧本/分镜、输出规格、旁白要求、参考图策略和 VM 完成后策略。未指定时使用项目默认值。
2. 调用 `start_full_production` 创建持久化 run。导演明确要求自主完成或一句话出片时，参考图策略设为：保留所有明确可用参考图，缺失参考图按最新文字设定继续；多个同名候选无法判断时仍进入 `AwaitingInput`。
3. 返回 `runId`、当前阶段和阻断项。不得把“任务已创建”表述为“成片已完成”。
4. 导演查询、继续或补充决定时调用 `get_full_production_status`；后台执行器会从持久化检查点恢复。
5. 只有状态为 `Completed` 且返回通过验证的最终 MP4 资产时，才报告出片完成。

## Rules

- 不在聊天请求生命周期内直接执行整片长任务。
- 不把组件工具可用误判为完整编排可用。
- 不在首帧阶段完成前启动远程 VM。
- 不根据聊天历史推断素材完成状态，只使用 production run item 和持久化绑定。
- Agent 不直接启动或停止 Azure VM；基础设施执行器根据 run 状态和策略处理。

## Verification

- 创建结果包含持久化 `runId`、`status` 和完整 shot item 数。
- 恢复后已成功且输入指纹未变化的 item 不产生新版本。
- 完成结果包含真实 MP4 资产，镜头数与配音数均等于目标数，且验证状态通过。
