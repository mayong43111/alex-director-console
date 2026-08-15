---
name: minimax-h3-video-prompt
title: 视频镜头提示词编写
description: Write and verify executable image-to-video prompts from project shot text. Use when preparing, correcting, reviewing, or regenerating any shot video prompt, especially camera motion, character action, idle motion, frozen-looking video, subtitles, captions, or changing screen text. This skill prepares prompts only and never generates video.
version: 1.1.0
allowed-tools: list_project_resources read_project_resource_contents
---
# 视频镜头提示词编写

## 使用条件

用于把一个项目 shot 的正文转换为可直接交给图生视频模型执行的提示词。只负责编写和检查提示词，不调用视频生成工具。每次生成或重新生成 shot 视频前都必须先加载本技能。

## 输入

1. 使用真实 UUID 读取目标 shot 的完整正文，不从聊天摘要、镜号或旧视频提示词推断。
2. 提取镜头时长、开机画面、主体、人物/物体动作、摄影机运动、停机画面、连续性要求和禁止项。
3. 关键帧只决定可见身份、构图与初始状态；不得把静态关键帧误解成整段视频必须静止。

## 编写流程

1. 先建立单一连续 take 的时间轴：
   - `0%`：起始构图和所有主体的初始姿态。
   - `1%–80%`：按先后顺序执行主体动作与摄影机运动。
   - `80%–100%`：动作收束并落到明确结束构图。
2. 摄影机指令必须写明运动类型、方向、速度、幅度和起止构图。固定机位明确写 `locked-off camera, no camera movement`，但固定摄影机不等于画内主体冻结。
3. 每个可见人物都必须获得可执行动作：头部、视线、面部、躯干、手臂、手指或行走中实际需要发生的部分，按时间顺序写清幅度与速度。先判断动作在该景别和项目预览分辨率下是否可辨；大全景中的眨眼、呼吸和极小视线变化不计为有效主体动作。
4. shot 写“坐着不动”“站着不动”“保持原位”时，只解释为不离开位置、不做新的剧情性大动作。只要画面中有活人，就必须保留可见但克制的生命微动作，例如自然呼吸带来的肩胸轻微起伏、一次自然眨眼、细小的视线稳定或重心调整。除非导演明确要求定格、假人或绝对静止，绝对禁止使用 `completely still`、`motionless throughout`、`no visible motion`、`head, eyes, face, torso, arms, and hands still` 等把人物冻结的措辞。
5. 3 秒以上且有人物可见的镜头，shot 正文必须提供至少一个在当前景别可辨的有效主体动作或连续动作链；生命微动作只能连接有效动作，不能用“呼吸 + 眨眼”机械填满整镜。动作必须克制且不改变 shot 的剧情事实，不能让模型自行发挥。每个节拍必须选定唯一动作，禁止使用 `or`、`either ... or`、`and/or`、“或”“任选其一”等备选表达把动作选择交给模型。
6. 若 shot 正文只有“坐着不动”“保持原位”加摄影机运动，且没有当前景别可辨的主体、物体或环境动作，不得自行虚构操作、手势或剧情行为，也不得仅靠呼吸和眨眼把检查标为通过。返回交接块并将 `ACTION_VISIBILITY` 与 `CHECK` 标为失败，说明需要先用 `storyboard-design` 回修该 shot；禁止交给视频生成技能。纯黑场、明确静物、后期版式或导演明确要求的定格画面可标为 `not_applicable`。
7. 人物有明确剧情动作时，以该动作为主，微动作只用于连接动作与避免僵帧，不得抢戏。无人镜头则明确物体或环境中实际允许发生的运动；导演明确要求纯黑场或静物绝对静止时可以没有画内运动。
8. 只写模型能直接执行的视觉指令。不得解释故事背景、人物想法、观众感受、导演意图、声音、台词作用或抽象气氛；不得用 `builds tension`、`establishes the space`、`cinematic atmosphere` 等目的性描述代替动作。
9. 绝对禁止字幕、标题、对白文字、说明文字、水印或任何新增文字叠加。shot 中要求出现的字幕、消息、输入文字或品牌标题一律作为后期项，从视频提示词中移除。关键帧已有画内文字只能保持原样，不得重写、变形、弹出、滚动或动画化。
10. 末尾必须同时约束：不得新增人物/物体、不得改变身份服装、不得额外肢体动作、不得构图漂移、不得突然运镜、不得切镜或转场，并原样附加：`no subtitles, no captions, no titles, no text overlays, no new or changing text`。

## 交接格式

完成后向视频生成技能交接以下内容，不得只给摘要：

```text
SHOT_ASSET_ID: <真实 shot UUID>
DURATION_SECONDS: <时长>
START_STATE: <起始状态>
SUBJECT_ACTIONS: <按时间顺序的主体动作；活人必须含生命微动作>
ACTION_VISIBILITY: <动作在当前景别和预览分辨率下为何可辨；弱动作镜头写 failed: needs storyboard revision>
CAMERA_MOVEMENT: <类型、方向、速度、幅度、起止构图>
END_STATE: <结束状态>
POST_PRODUCTION_TEXT: <需留给后期的字幕/画内文字；没有则写 none>
VIDEO_PROMPT: <最终完整提示词>
CHECK: executable=yes|no; action_visibility=yes|no|not_applicable; living_subject_motion=yes|not_applicable; camera_explicit=yes; no_story_exposition=yes; no_generated_text=yes
```

任何 `CHECK` 项不是 `yes` 或 `not_applicable` 时都不得交给生成技能。人物可见却把 `living_subject_motion` 标为 `not_applicable` 视为检查失败。`action_visibility=no` 时必须先回修分镜，不得通过增加微动作绕过。

## 验证

- 提示词包含起始状态、按时序的主体动作、明确摄影机运动和结束状态。
- 活人入镜时存在可见生命微动作，且没有冻结人物的措辞。
- 3 秒以上的人物镜头具有当前景别可辨的有效主体动作；呼吸、眨眼和极小视线变化没有被单独计为有效动作。
- 每个动作节拍都唯一确定，不含 `or`、`either ... or` 或其他动作备选表达。
- 固定机位与人物微动作被分别描述，没有把二者混为“全画面不动”。
- 不含故事、情绪目的、声音或让模型自行设计动作的表达。
- 不要求生成、改变或动画化任何文字，并包含固定禁字幕短语。
- 最终提示词与交接字段一致，可被生成技能原样使用。