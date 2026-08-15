---
name: shot-asset-binding
title: 镜头素材绑定
description: Bind existing image, video, or audio assets to selected or explicitly identified shots as first frames, last frames, references, videos, or other shot media.
version: 1.2.0
allowed-tools: list_project_resources query_storyboard bind_shot_asset
---
# 镜头素材绑定

## When to Use

用于把最近生成或导演明确指定的已有图片、视频、音频素材关联到界面当前选中或导演明确指定的 `shot`，包括首帧、尾帧、参考图、镜头视频和其他镜头素材。

## Procedure

1. 确定目标 `shot`：导演指向当前镜头时使用系统上下文中的资产 ID；导演按场号、镜号、范围或来源剧本指定其他镜头时调用 `query_storyboard`，使用返回的最新 `shotAsset.id`，不按名称猜测也不要求导演切换界面。
2. 从最近图片工具结果、最近对话生成素材或导演明确提供的信息中取得真实媒体资产 ID。无法唯一确定素材时列出候选并询问，不得猜测。
3. 根据导演表达选择 role：首帧 `first-frame`、尾帧 `last-frame`、参考图或关键帧 `reference`、镜头视频 `video`、其他 `other`。
4. 调用 `bind_shot_asset`，显式传入目标 `shotAssetId`。只有工具成功返回后才声明绑定完成。

## Rules

- 不修改镜头 Markdown 来模拟绑定；关系必须由工具持久化。
- 不把人物设定稿、场景文字稿或非媒体资源当作镜头素材绑定。
- 同一镜头可以绑定多个素材；不得为绑定新素材删除已有关系。
- `Asset.ResourceId` 表示同一逻辑素材，`Asset.Version` 表示传统制作中的一条 take。绑定记录必须指向实际选用的具体版本 ID；切换 take 只更新绑定选择，不删除同一资源的旧版本。
- 重生成同一镜头、同一用途的素材时必须沿用稳定资源名称，使其成为原逻辑素材的新版本；不得仅因重试就在名称后添加“新版、重做、final2”等后缀创建重复逻辑素材。

## Verification

确认工具返回的素材名称、具体版本资产 ID、role 与当前镜头及导演要求一致；需要核对历史 take 时再次调用 `query_storyboard(includeTakes=true)`。