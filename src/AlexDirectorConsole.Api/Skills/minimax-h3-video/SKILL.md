---
name: minimax-h3-video
title: MiniMax H3 视频生成
description: Generate and bind MiniMax H3 shot videos from a finalized prompt prepared by the minimax-h3-video-prompt skill. Use for ComfyUI workflow execution, frame preparation, model parameters, video download, validation, persistence, and shot binding; never author or repair the video prompt in this skill.
version: 1.2.0
allowed-tools: list_project_resources query_storyboard read_project_resource_contents inspect_remote_comfyui manage_remote_comfyui generate_comfyui_video bind_shot_asset
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
5. 调用 `query_storyboard` 按来源剧本、场号和镜号确定目标 shot、结构化时长、当前绑定首尾帧及已有视频 take；再调用 `read_project_resource_contents` 读取目标 shot 正文。没有尾帧时可复用首帧。不得要求外部执行者预先裁图。调用 `generate_comfyui_video` 时由工具等比处理到 H3 canvas：可安全裁边时使用 `frameFitMode=cover`，必须保留完整构图时使用 `frameFitMode=contain`，始终禁止非等比拉伸。
6. 每个 shot 在生成前必须另外加载 `minimax-h3-video-prompt` 技能，由该技能读取完整 shot 正文、编写最终提示词并返回完整交接块。没有交接块、任一 `CHECK` 未通过、人物可见但缺少生命微动作，或提示词包含冻结人物的措辞时，禁止调用 `generate_comfyui_video`。本技能不得自行编写、补充、摘要、翻译或修复提示词，只能把交接块中的 `VIDEO_PROMPT` 原样传给 `videoPrompt`。
7. 分辨率策略：快速拉片优先使用项目 `previewResolution`；16:9 H3 粗剪推荐 `608x352`，常规终稿推荐 `1152x640`，关键终稿才使用 `1504x832`。若项目设置与 H3 合法 canvas 不同，先告知导演并选择保持项目比例的 H3 canvas，不得暗中改变画幅。
8. 固定 24 FPS、20 steps、`res_multistep` sampler、`simple` scheduler、denoise 1.0，除非所选 workflow 或导演明确要求不同。必须使用 `query_storyboard` 返回的当前 shot 结构化时长计算帧数，禁止把所有镜头统一成 124 帧。帧数必须满足：

   $$N=17k+5$$

   取能够覆盖设计时长的最小合法值：`N = 17 × ceil((ceil(时长 × FPS) - 5) / 17) + 5`。例如 24 FPS 下，4 秒取 107 帧（约 4.458 秒），8 秒取 192 帧，15 秒取 362 帧（约 15.083 秒）。`generate_comfyui_video` 会从持久化 shot 定义重新计算并强制使用该值，调用参数不得覆盖设计时长。最终可硬裁切，不做时间拉伸。
9. 将提示词交接块中的 `VIDEO_PROMPT` 原样传入 `generate_comfyui_video`；该工具会上传关键帧、提交 workflow、下载并验证 MP4、保存视频素材，并用 `video` 角色独占绑定到 shot。
10. 工具失败时原样报告 SSH、workflow 校验、ComfyUI 或媒体校验错误。只有本地 Blob 中存在通过签名与大小检查的 MP4，才可声明生成成功。

## 验证

- `inspect_remote_comfyui` 的 HTTP `system_stats`、`object_info` 与 `userdata` 真实输出证明服务、所需模型、H3 节点和 workflow 可用。
- 已加载 `minimax-h3-video-prompt`，其交接块全部 `CHECK` 通过；实际提交的 `videoPrompt` 与 `VIDEO_PROMPT` 逐字一致。
- 工具已将关键帧等比处理到 workflow width/height，并在过程事件中报告 `cover` 或 `contain`。
- 视频工具返回媒体资产和对应 shot，且视频已绑定为 `video`。
- 同镜重新生成的视频沿用稳定逻辑资源，返回版本递增；`query_storyboard(includeTakes=true)` 能列出历史 take，当前 `video` 绑定指向本次选用的具体版本。
- 最终回复由系统原样附加 workflow 文件名、宽高、帧数、FPS 和完整视频提示词，Agent 不得摘要或改写。
