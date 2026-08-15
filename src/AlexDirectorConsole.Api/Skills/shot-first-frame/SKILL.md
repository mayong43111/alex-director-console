---
name: shot-first-frame
title: 镜头画面生成
description: Generate and bind one or more shots' first frames, last frames, keyframes, or storyboard images from finalized prompts prepared by the image-generation-prompt skill, preserving all available references unless the director explicitly rejects every reference. Use for 首帧、尾帧、关键帧、分镜图 or batch shot image generation.
version: 2.0.0
allowed-tools: list_project_resources list_shot_first_frame_status query_storyboard read_project_resource_contents read_project_resources inspect_visual_references merge_reference_images generate_image generate_image_from_references bind_shot_asset
---
# 镜头首帧

## When to Use

用于为一个或多个 `shot` 生成首帧、尾帧、关键帧或分镜图。默认尽可能继承项目中所有已有的人物、场景和关键道具参考图；导演允许不补缺失参考时，只跳过缺失项，不影响已有参考图。只有导演明确要求放弃所有参考图时，才依据完整文字设定直接生成。界面当前选中的 shot 只是默认目标，不限制 Agent 读取和处理项目中的其他 shot。

## Procedure

0. 导演询问哪些镜头已有或缺少首帧、首帧完成数量或进度时，必须先调用 `list_shot_first_frame_status`，以持久化的 `first-frame` 绑定和仍存在的图片素材为唯一事实来源，然后直接按工具返回的统计与镜头名称回答。不得根据最近生成图片、聊天记录、生成失败记录或当前选中镜头推断状态。只查询状态时不需要执行后续生成步骤。
1. 确定目标镜头：
   - 单镜任务且导演指向界面当前 shot：直接使用系统提供的当前 shot ID 和完整正文。
   - 导演指定其他镜头、编号范围、场次或“全部镜头”：调用 `list_project_resources`，以 `resourceType=shot` 和名称条件发现全部目标 shot；再调用 `read_project_resource_contents`，按返回的资产 ID 分批读取每个目标 shot 的完整正文。不得要求导演逐个切换资源或粘贴已有正文。
2. 对每个目标 shot，从完整正文提取画面中实际可见的人物、场景和关键道具名称；不要检查未入画对象。保留每个 shot 的资产 ID，后续绑定必须使用。shot 正文中的历史资源 ID、版本或“来源资源”章节即使存在也只能视为旧数据，不得直接作为本次生成输入。
3. 紧接生成前动态调用 `list_project_resources` 查找这些对象当前存在的资源，再调用 `read_project_resources` 读取最新文字设定稿，确定不能违背的造型、空间、材质、状态和连续性约束。每次重新生成都必须重查，不得复用 shot 创建时、此前会话或上次生成保存的资源清单。
4. 调用 `inspect_visual_references`，可合并传入目标镜头中所有可见人物、场景和关键道具名称，取得真实图片候选及资产 ID。只有导演明确说“所有参考图都不用”“一张参考图也不要”“完全不使用任何参考图”或同等清晰的全量放弃指令时，才跳过本步骤。
5. 对每个必要对象检查参考图：
   - 没有图片候选：首次发现时列出缺失对象，说明直接生成可能降低视觉一致性，询问导演是先建立参考图还是跳过；当前轮停止，不调用任何首帧图片工具。
   - 有多个图片候选且无法从名称、版本和最近对话确定使用哪张：首次发现时列出候选名称与版本，请导演选择或明确跳过；当前轮停止。
   - 只有一个明确候选，或导演已在最近对话中明确选择：记录其图片资产 ID。
   - 最近对话已经列出缺失项，导演随后说“其他不需要”“不要这些了”“跳过其他的”“直接生成”或同义指令：仅视为不再补齐已列出的缺失对象，不得丢弃其他已有参考图，也不得再次为相同缺失项询问。
   - 只有导演明确强调“所有参考图都不用”“一张参考图也不要”“完全不使用任何参考图”或同等清晰的全量放弃，才允许文字直出。含糊的“这些”“其他”按最近列出的缺失项解释，不按全部参考图解释。
6. 导演确认先生成缺失参考图后，按最新文字设定调用 `generate_image` 分别生成缺失的人物、场景或道具参考图，`imagePurpose` 必须使用 `asset`，固定输出 1:1。生成完成后不得在同一轮假定所有选择已确认；汇报新图并请导演确认用于首帧。
7. 在用于最终生成前整理参考图：
   - 同一道具有多角度、多状态或多张候选被选用时，调用 `merge_reference_images` 合并成一张道具参考图。
   - 一个镜头有两个及以上关键道具参考图时，调用 `merge_reference_images` 合并成一张道具参考图；合并说明必须逐项写清道具名称、状态和应继承的材质/形制。
   - 一个镜头实际可见人物达到 4 人以上时，调用 `merge_reference_images` 把人物按每组最多 6 人合并；不得把人物和道具混在同一合并图中。
   - 场景图通常保持独立输入；只有同一场景确需多角度共同约束时才合并。
8. 每个 shot 在生成前必须另外加载 `image-generation-prompt` 技能，把本流程读取的完整 shot、最新文字设定和最终参考图清单交给该技能，取得完整交接块。没有交接块、任一 `CHECK` 未通过、参考图映射不完整或提示词包含故事解释/动作备选/新增文字要求时，禁止调用图片工具。本技能不得自行编写、补充、摘要、翻译或修复提示词。
9. 只要该镜存在任何可用且未被导演明确拒绝的参考图，就调用 `generate_image_from_references`。将交接块中的 `IMAGE_PROMPT` 原样作为生成要求；`referenceImageAssetIds` 与交接块中的 `REFERENCE_ASSET_IDS` 必须一致，`referenceImageDescriptions` 与 `REFERENCE_DESCRIPTIONS` 必须严格同序、同数量。缺失对象使用步骤 3 的文字设定补充到提示词，不得因为部分对象缺图而放弃其他已有参考图。记录工具返回的新媒体资产 ID。
10. 只有导演明确放弃所有参考图时，才把交接块中的 `IMAGE_PROMPT` 原样传给 `generate_image`，`imagePurpose` 使用 `project-frame`，记录返回的新媒体资产 ID。
11. 批量镜头必须严格串行：一次只允许为一个镜头调用一个图片生成工具，不得并行发起多个 `generate_image` 或 `generate_image_from_references`。每张图片生成并成功保存后，工具完成事件必须立即事实输出该镜头完整 `imagePrompt`；随后立即调用 `bind_shot_asset`。`shotAssetId` 传该镜头的资产 ID；首帧使用 `first-frame`，尾帧使用 `last-frame`，关键帧或分镜图使用 `reference`。只有该镜绑定成功后才可开始下一镜并最终声明完成。
12. 最终回复只汇报实际生成和绑定结果，不得再次重复、摘要或改写已经逐张即时输出的提示词。使用参考图时，即时输出的是包含“参考图说明”和“生成要求”的最终实际拼接提示词。

## Rules

- 默认使用所有可用参考图以保持视觉一致性；“不补缺失项”与“放弃所有已有参考图”是两种不同授权，不得混淆。
- 导演拥有最终决定权，但全量文字直出必须来自明确的全量放弃表达，不能从“这些”“其他”“直接开始”等上下文指代中扩大解释。
- 文字设定不能提供与图片相同的视觉锚定；部分对象缺图时用文字约束补足，已有对象仍使用图片锚定。
- 不得为省步骤只选人物图而忽略已存在的场景图或入画关键道具图。
- 不得把整个完整分镜稿、剧本封面或无关图片作为参考图。
- 默认使用 `medium` 质量。参考素材固定 1:1；最终 shot 首帧无论走哪条生成路径都按当前项目成片画幅输出。
- 不要自动替导演选择多个同名候选中的任意一张，除非最新版本关系明确。
- 不要只在回复中说图片属于当前镜头；镜头与素材的关系必须由 `bind_shot_asset` 持久化。
- 批量任务不得因当前界面只选中一个 shot 而停止；先用资源目录和正文读取工具取得其余目标 shot。
- 镜头资源只描述拍摄内容，不拥有固定的制作资源依赖；人物、场景、道具及其参考图必须在每次生成前动态发现并选用最新版本。
- 图片结果与完成事件即时输出的完整提示词必须同时交付；批量生成时每一张图片都要有自己对应的完整提示词，不能用一份公共摘要代替，也不能并行或在最终回复重复抄写。

## Verification

逐镜确认已加载 `image-generation-prompt` 且交接块全部 `CHECK` 通过；实际提交的提示词与 `IMAGE_PROMPT` 逐字一致。图片工具必须返回媒体资源及非空 `imagePrompt`、完成事件已立即输出提示词，且 `bind_shot_asset` 返回对应 `shotAssetId` 的绑定记录；确认绑定成功后才开始下一镜。使用参考图时确认 `referenceAssets` 与逐图说明同序同数量；道具或多人合并图需能从 `merge_reference_images` 返回的 layout 追溯全部源图。只有导演明确放弃所有参考图时才确认使用 `generate_image + project-frame`。批量完成数量必须与目标 shot 数量一致，阻断项需按镜号列出。