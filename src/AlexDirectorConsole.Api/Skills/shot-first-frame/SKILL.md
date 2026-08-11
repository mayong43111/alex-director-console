---
name: shot-first-frame
title: 镜头首帧
description: Generate a selected shot's first frame or keyframe using real character, scene, and prop reference images. Use for 首帧、关键帧、分镜图 or shot image generation.
version: 1.0.0
allowed-tools: read_project_resources inspect_visual_references generate_image generate_image_from_references
---
# 镜头首帧

## When to Use

用于为当前选中的 `shot` 生成首帧、关键帧或分镜图。首帧必须继承项目中已确认的人物外形、场景空间和关键道具视觉，不允许只根据文字提示词从零生成。

## Procedure

1. 读取当前 `shot` 完整正文，提取画面中实际可见的人物、场景和关键道具名称；不要检查未入画对象。
2. 调用 `read_project_resources` 读取这些对象的最新文字设定稿，确定不能违背的造型、空间、材质、状态和连续性约束。
3. 调用 `inspect_visual_references`，一次传入所有可见人物、场景和关键道具名称，取得真实图片候选及资产 ID。
4. 对每个必要对象检查参考图：
   - 没有图片候选：列出缺失对象，说明首帧需要先建立参考图，询问导演是否现在生成；当前轮停止，不调用任何首帧图片工具。
   - 有多个图片候选且无法从名称、版本和最近对话确定使用哪张：列出候选名称与版本，请导演选择；当前轮停止。
   - 只有一个明确候选，或导演已在最近对话中明确选择：记录其图片资产 ID。
5. 导演确认先生成缺失参考图后，按最新文字设定调用 `generate_image` 分别生成缺失的人物、场景或道具参考图。生成完成后不得在同一轮假定所有选择已确认；汇报新图并请导演确认用于首帧。
6. 所有必要参考图明确后，整理首帧提示词：镜头构图、景别、机位、人物动作与表情、空间位置、关键道具状态、光线、色彩、连续性；明确要求分别继承每张参考图对应对象的视觉身份。
7. 调用 `generate_image_from_references`，传入所有已确认参考图片资产 ID。只有工具成功返回后才声明首帧已生成。

## Rules

- “首帧不是修改已有图片”不代表可以不用参考图；这里使用 edits 接口是为了多图视觉条件输入，不是修改某一张原图。
- 文字设定是约束，图片资产才是视觉参考；两者不能互相替代。
- 不得为省步骤只选人物图而忽略已存在的场景图或入画关键道具图。
- 不得把整个完整分镜稿、剧本封面或无关图片作为参考图。
- 默认使用 `medium` 质量，保持当前系统画幅设置。
- 不要自动替导演选择多个同名候选中的任意一张，除非最新版本关系明确。

## Verification

确认 `generate_image_from_references` 返回首帧媒体资源和实际使用的 `referenceAssets`；参考资源必须全部属于当前项目、是图片类型，并覆盖镜头中所有需要保持视觉一致的对象。