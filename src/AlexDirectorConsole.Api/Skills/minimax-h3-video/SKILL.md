---
name: minimax-h3-video
title: MiniMax H3 视频生成
description: Inspect and operate a configured remote ComfyUI VM, discover compatible API workflows and installed H3 models, then generate and bind first/last-frame MiniMax H3 shot videos using project settings.
version: 1.0.0
allowed-tools: list_project_resources read_project_resource_contents inspect_remote_comfyui manage_remote_comfyui generate_comfyui_video bind_shot_asset
---
# MiniMax H3 视频生成

## 使用条件

用于导演要求将项目 shot 的首帧/尾帧制作成视频时。必须使用项目设置中的画幅、快速拉片分辨率、视频模型和目标时长，不得自行忽略项目参数。远端 VM、SSH 和 ComfyUI 均由项目设置提供，私钥只引用本机路径。

## 职责边界

- 外部执行者只负责启动 Azure VM、申请 SSH JIT，并把导演令发送给项目 Agent。
- 从收到导演令开始，远端检查、ComfyUI 启停、SSH 隧道、目标 shot 与关键帧发现、关键帧等比处理、workflow 提交、等待生成、MP4 下载与校验、素材保存和 shot 绑定，全部由项目 Agent 通过本技能和允许的工具完成。
- 项目 Agent 不负责 Azure VM 的开机、JIT 或关机。测试结束后是否保持 VM 运行由导演令决定；不得自行关机或释放 VM。

## 流程

1. 调用一次 `manage_remote_comfyui(action=start-tunnel)`。该动作会先探测本地 HTTP 代理；`127.0.0.1:8188` 已可用时必须复用现有隧道，不得重复创建 SSH 进程。随后调用 `inspect_remote_comfyui`，通过 ComfyUI HTTP API 的 `/system_stats`、`/queue` 和 `/userdata` 确认设备、队列和 workflow。不得使用 SSH 重复检查 HTTP 已能提供的信息，也不得根据配置值声称服务已安装或运行。
2. 从 HTTP `object_info` 返回的 loader 选项检查模型清单，至少包含项目所选视频模型，并确认 `MiniMaxH3ImageToVideo` 节点存在。MiniMax H3 基线为：
   - `minimax_h3_fl2va_pruned_int8_convrot.safetensors`
   - `minimax_h3_video_vae_fp16.safetensors`
   - `qwen3vl_32b_minimax_h3_int8_convrot.safetensors`
3. 从检查结果选择 API prompt workflow JSON。workflow 正文由 API 随技能打包的本地资源读取，不得通过 SSH `cat` 远程文件。它必须是 ComfyUI API 格式而非 UI workflow，并声明这些精确占位符：`{{FIRST_FRAME}}`、`{{LAST_FRAME}}`、`{{PROMPT}}`、`{{WIDTH}}`、`{{HEIGHT}}`、`{{FRAME_COUNT}}`、`{{FPS}}`、`{{OUTPUT_PREFIX}}`。如果只有 UI workflow，先要求导演在 ComfyUI 中以 API 格式导出；不得假称可以提交。
4. ComfyUI 未运行时可调用 `manage_remote_comfyui(action=start)`；已有进程但状态异常时使用 `restart`。只有导演明确要求升级，且检查结果证明版本不合适时，才调用 `update`。该动作只允许 fast-forward，不安装未知依赖。
5. 调用 `list_project_resources` 与 `read_project_resource_contents` 确定目标 shot，并发现该 shot 当前独占绑定的首帧和尾帧；没有尾帧时可复用首帧。不得要求外部执行者预先裁图。调用 `generate_comfyui_video` 时由工具等比处理到 H3 canvas：可安全裁边时使用 `frameFitMode=cover`，必须保留完整构图时使用 `frameFitMode=contain`，始终禁止非等比拉伸。
6. 分辨率策略：快速拉片优先使用项目 `previewResolution`；16:9 H3 粗剪推荐 `608x352`，常规终稿推荐 `1152x640`，关键终稿才使用 `1504x832`。若项目设置与 H3 合法 canvas 不同，先告知导演并选择保持项目比例的 H3 canvas，不得暗中改变画幅。
7. 固定 24 FPS、20 steps、`res_multistep` sampler、`simple` scheduler、denoise 1.0，除非所选 workflow 或导演明确要求不同。帧数必须满足：

   $$N=17k+5$$

   常用值：124 帧约 5.167 秒、158 帧约 6.583 秒、175 帧约 7.292 秒、192 帧为 8 秒。生成长度需覆盖 shot 时长，最终可硬裁切，不做时间拉伸。
8. 提示词只安排一个主要动作链，写清人物/物体与镜头关系、相机运动、连续性以及禁止项。调用 `generate_comfyui_video`；该工具会上传关键帧、提交 workflow、下载并验证 MP4、保存视频素材，并用 `video` 角色独占绑定到 shot。
9. 工具失败时原样报告 SSH、workflow 校验、ComfyUI 或媒体校验错误。只有本地 Blob 中存在通过签名与大小检查的 MP4，才可声明生成成功。

## 验证

- `inspect_remote_comfyui` 的 HTTP `system_stats`、`object_info` 与 `userdata` 真实输出证明服务、所需模型、H3 节点和 workflow 可用。
- 工具已将关键帧等比处理到 workflow width/height，并在过程事件中报告 `cover` 或 `contain`。
- 视频工具返回媒体资产和对应 shot，且视频已绑定为 `video`。
- 最终回复由系统原样附加 workflow 文件名、宽高、帧数、FPS 和完整视频提示词，Agent 不得摘要或改写。
