---
name: image-generation-prompt
title: 图片生成提示词编写
description: Write and verify final prompts for every generated or edited image. Use before generate_image, generate_image_from_references, or edit_image for shot frames, storyboards, character turnarounds, character, location, prop, concept, reference, and revision images. This skill prepares prompts only and never generates or edits images.
version: 1.1.0
allowed-tools: list_project_resources read_project_resource_contents read_project_resources inspect_visual_references
---
# 图片生成提示词编写

## 使用条件

任何新生成、参考图生成、再次生成或编辑图片的任务都必须先加载本技能。只负责读取事实、编写最终提示词和质量检查，不调用 `generate_image`、`generate_image_from_references` 或 `edit_image`。

## 输入事实

1. 镜头画面必须使用真实 shot UUID 读取完整正文，并提取景别、机位、构图、可见人物/物体、定格动作、空间关系、光线、色彩和连续性。
2. 已有人物、场景、道具或其他项目对象必须读取最新文字设定，不从聊天摘要、旧提示词或旧版本资源推断。
3. 使用参考图时记录每张真实图片资产 UUID、对象名称、用途和必须继承的视觉特征。参考图只能约束其对应对象，不得把一张图错误应用到其他人物、场景或道具。
4. 编辑图片时读取导演明确修改项，并区分“必须改变”与“必须保持不变”；不得借编辑任务重画未要求变化的内容。

## 编写规则

1. 提示词是静态画面的可执行绘制说明，不是故事梗概、审美分析或让模型自行发挥的建议。只描述最终一帧中实际可见的内容。
2. 按以下顺序写清：图片用途与画幅 -> 主体身份和数量 -> 景别、机位、焦段感与构图 -> 每个主体的精确姿态、视线、手部和表情 -> 主体间及与环境的空间关系 -> 场景、关键道具 -> 光线、色彩、材质 -> 连续性 -> 禁止项。
3. 人物动作必须冻结在一个明确、可绘制的瞬间。禁止使用“正在做一些动作”“自然互动”“富有故事感”等含糊表达；写清头部方向、视线目标、躯干姿态、左右手分别做什么、腿脚位置和表情强度。不得把前后多个时刻或动作过程塞进一张图。
4. 不得向图片模型解释剧情前因后果、人物内心、导演意图、声音、台词作用或观众应有的感受。用可见事实表达气氛，不用抽象目的代替画面。
5. 镜头画面必须忠实于 shot，不能自行增加角色、道具、事件、动作或改变机位。人物/场景/道具设定图必须忠实于最新设定，不得补写未确认的身份特征。
6. 参考图生成提示词必须为每张参考图逐条写明：对应对象、继承内容、不得继承内容。最终 `REFERENCE_ASSET_IDS` 与 `REFERENCE_DESCRIPTIONS` 必须同序同数量；不得只写“参考附件”或“保持一致”。
7. 人物设定图固定为 1:1 四视图版式，不得生成单张全身像或自由排版：画面左侧约 55% 是正面全身，完整显示头顶到脚底；右侧约 45% 分为上下两区，右上并排放背面全身和标准侧面全身，二者完整显示头脚且不重叠，右下是头部与肩部特写。四个视图必须是同一人物，身份、年龄、体型、脸型、五官、发型、服装、材质和配色严格一致；三个全身视图使用中性站姿、统一比例，背景简洁，不得添加标题、标签或分隔文字。
8. 场景设定图固定为 1:1 三视图版式，不得生成单张环境概念图或自由排版：上方约 60% 是场景正面全景，完整交代主要空间、出入口和关键结构；下方约 40% 左侧是同一场景的另一个明确角度，右侧是同一场景的高位垂直俯视图，清楚呈现平面布局、空间关系和动线。三个视图必须保持建筑结构、陈设、材质、光源方向、时间和色彩一致，不得添加人物、标题、标签或分隔文字。
9. 道具设定图使用 1:1，至少包含能确认整体轮廓的主视图和能确认材质、纹理、磨损或关键结构的细节视图；不得把道具放入叙事场面，也不得添加未确认装饰。
10. 默认绝对禁止字幕、标题、对白文字、说明文字、水印、Logo 和任何新增文字叠加。shot 中的字幕、消息文字、输入文字、品牌标题留给后期，不要求图片模型生成可读文字。只有导演明确要求制作文字设计本身时才允许文字，并必须逐字提供内容。
11. 编辑图片只写一份完整可执行提示词：先列必须保持的原图身份、构图、姿态、光线和未修改区域，再列唯一确定的修改。禁止 `or`、`either ... or`、`and/or`、“或”“任选其一”等备选表达。
12. 所有要求必须唯一确定且内部一致。出现参考图与文字设定冲突、人物数量不一致、动作无法在单帧成立或关键信息缺失时，先报告具体冲突，不得交给生成技能。

## 交接格式

```text
IMAGE_PURPOSE: asset|project-frame|edit
TARGET_ASSET_ID: <shot/设定/源图片 UUID；没有则写 none>
ASPECT_RATIO: <1:1 或项目画幅>
REFERENCE_ASSET_IDS: <按输入顺序的 UUID；没有则写 none>
REFERENCE_DESCRIPTIONS: <与 ID 同序逐条说明；没有则写 none>
POST_PRODUCTION_TEXT: <留给后期的文字；没有则写 none>
IMAGE_PROMPT: <最终完整提示词>
CHECK: single_frame=yes; composition_explicit=yes; subjects_explicit=yes; references_mapped=yes|not_applicable; no_story_exposition=yes; no_generated_text=yes; no_alternatives=yes
```

任一 `CHECK` 项不是 `yes` 或 `not_applicable` 时，禁止调用图片工具。生成或编辑技能只能把 `IMAGE_PROMPT` 原样传入工具；不得自行补充、摘要、翻译或修复。

## 验证

- 提示词只描述一个静态瞬间，构图、主体数量、姿态、视线、手部、空间与光线均明确。
- 不含故事解释、声音、抽象目的或动作备选。
- 所有参考图逐一映射且顺序一致，不误用参考对象。
- 默认不生成任何文字，后期文字已单列。
- 人物设定图符合“左侧正面全身、右上背面与侧面全身、右下头部特写”的固定四视图版式；场景设定图符合“上方正面全景、左下另一角度、右下俯视图”的固定三视图版式。
- 编辑任务明确区分保持项和修改项。
- 交接块完整，图片执行技能可原样使用。