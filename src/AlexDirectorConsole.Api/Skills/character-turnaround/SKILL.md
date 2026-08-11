---
name: character-turnaround
title: 人物三视图生成
description: Generate or revise consistent character turnaround images. Use for character front, true side, and back views, regenerations, or visual changes to an existing turnaround.
version: 2.1.0
allowed-tools: read_project_resources generate_image edit_image
---
# 人物三视图生成

## When to Use

用于人物三视图的新生成、再次生成，以及对既有三视图的服装、发型、颜色、姿势或其他视觉内容进行修改。

## Procedure

1. 根据导演原始指令、最近对话生成图片和界面当前资源，自行确定人物与目标图片。
2. 调用 `read_project_resources` 读取该人物最新设定稿的完整正文。
3. 新生成或再次生成时，根据最新设定整理完整提示词，再调用 `generate_image`。再次生成沿用已确认的设定与构图要求，但不复用旧图像素。
4. 修改已有图片时，调用 `edit_image`。`sourceImageName` 必须引用最近对话或当前资源中的明确图片名称；工具会把原图和完整修改提示词一起提交给图片模型。
5. 提示词必须要求同一人物的正面、标准侧面、背面，三个视图的身份、年龄、体型、发型、服装和配色一致，背景简洁，不添加无关文字。
6. 仅在图片工具成功返回并保存素材后声明完成。

## Pitfalls

- 不要仅因界面没有选中资源而要求导演重复指定；先检查最近生成图片。
- 不要用 `generate_image` 代替已有图片修改。
- 不要跳过人物最新设定稿。

## Verification

确认工具返回了新的媒体 Asset、版本号递增，并且回复中引用该新图片。
