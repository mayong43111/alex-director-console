---
name: character-turnaround
title: 人物三视图生成
description: Generate or revise consistent character turnaround images. Use for character front, true side, and back views, regenerations, or visual changes to an existing turnaround.
version: 2.5.0
allowed-tools: read_project_resources generate_image edit_image write_director_revision
---
# 人物三视图生成

## When to Use

用于人物三视图的新生成、再次生成，以及对既有三视图的服装、发型、颜色、姿势或其他视觉内容进行修改。

## Procedure

1. 根据导演原始指令、最近对话生成图片和界面当前资源，自行确定人物与目标图片。
2. 调用 `read_project_resources` 读取该人物最新设定稿的完整正文。
3. 新生成或再次生成时，根据最新设定整理完整提示词，再调用 `generate_image`，`imagePurpose` 必须使用 `asset`。再次生成沿用已确认的设定与构图要求，但不复用旧图像素。
4. 修改已有图片时，调用 `edit_image`，`imagePurpose` 必须使用 `asset`。`sourceImageName` 必须引用最近对话或当前资源中的明确图片名称；工具会把原图和完整修改提示词一起提交给图片模型。
5. 图片修改成功后，把导演要求的造型变化同步合并进步骤 2 读取的完整人物设定稿，再调用 `write_director_revision` 创建该人物设定稿的新版本。`sourceAssetIds` 至少包含步骤 2 的设定稿资产 ID 和步骤 4 返回的新图片资产 ID。不得只改图片、不改文字设定，也不得覆盖或删除设定稿旧版本。
6. 提示词必须要求同一人物的正面、标准侧面、背面，三个视图的身份、年龄、体型、发型、服装和配色一致，背景简洁，不添加无关文字。
7. 多个人物或多张图片必须严格串行处理：一次只调用一个 `generate_image` 或 `edit_image`；等待图片成功保存且工具完成事件立即事实输出该图完整 `imagePrompt` 后，才开始下一张。不得并行调用图片工具，不得把提示词积压到整批完成后。
8. 只有图片新版本与人物设定稿新版本均成功返回后，才声明修改完成，并同时报告两者。

人物三视图属于视觉参考素材，始终使用 1:1（1024x1024），不继承项目的成片画幅。

## Pitfalls

- 不要仅因界面没有选中资源而要求导演重复指定；先检查最近生成图片。
- 不要用 `generate_image` 代替已有图片修改。
- 不要跳过人物最新设定稿。
- 不要把仅存在于修改后图片中的造型变化留在图片里；发型、服装、体型、年龄感、配色、标志物等设定性变化必须同步写入人物设定稿。
- 不要用“本次调整”“提示词要点”等摘要替代工具完成事件即时输出的完整提示词，也不要在最终回复再次手工抄写全文。

## Verification

逐张确认图片工具返回新的媒体 Asset 和非空 `imagePrompt`，且完成事件已立即输出该提示词；批量任务确认前一张完成后才调用下一张。修改图片时还需确认 `write_director_revision` 返回同一人物设定稿的新版本，且设定正文准确包含本次图片修改涉及的设定性变化。
