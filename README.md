# alex 导演台

面向 AI 影视制作的导演工作台。用户是导演，Agent 是执行副导演；系统按导演的即时指令执行，不预设制作计划。

当前纵向切片支持项目资产、Foundry 对话、Agent 技能管理，以及从文本剧本提取人物、场景、道具、场次事件和歧义信息。

## 技术栈

- 前端：React 19、TypeScript、Vite
- 后端：ASP.NET Core 8
- Agent：Microsoft Agent Framework `HarnessAgent`、官方 `AgentSkillsProvider`、Azure OpenAI

## 环境要求

- .NET SDK 8.0.423
- Node.js 24+
- npm 11+

## 本地启动

启动后端：

```powershell
cd src/AlexDirectorConsole.Api
dotnet run
```

后端运行于 `http://localhost:5055`，开发环境 Swagger 位于 `http://localhost:5055/swagger`。

另开终端启动前端：

```powershell
cd src/web
npm install
npm run dev
```

前端运行于 `http://localhost:5173`，`/api` 请求由 Vite 代理至后端。

页面路由：

- `/`：选择或创建项目
- `/projects/:projectId`：指定项目的导演台，刷新后保持当前项目

## 数据库

后端使用 EF Core 8 + SQLite。开发数据库在 API 启动时自动迁移，文件位于：

```text
src/AlexDirectorConsole.Api/alex-director-console.db
```

当前包含 `Projects` 表：

- `Id`：GUID 主键
- `Name`：项目名称，必填，最长 200 字符
- `CreatedAtUtc`：创建时间
- `UpdatedAtUtc`：更新时间

`Assets` 是所有资产类型共用的基础表：

- `Id`：GUID 主键
- `ProjectId`：所属项目 ID
- `Type`：开放字符串类型，例如剧本为 `script`
- `Name`、`FileName`、`ContentType`、`SizeBytes`：资产与原文件元数据
- `BlobKey`：Blob 存储键，唯一索引
- `CreatedAtUtc`、`UpdatedAtUtc`：创建与更新时间

前端项目目前保存在浏览器本地，因此 `Assets.ProjectId` 暂不设置数据库外键；项目 API 接入后再补外键约束。

`ConversationMessages` 保存项目级对话历史：

- `ProjectId`：所属项目 ID
- `Role`：`user` 或 `assistant`
- `Content`：消息正文
- `Model`：响应使用的 Foundry 模型部署名
- `CreatedAtUtc`：消息时间

`SkillDefinitions` 保存可管理的 Agent 技能定义与版本；`SkillRuns` 保存每次技能调用的项目、输入资源、导演令、模型、状态、工具产物清单和错误。系统启动时会幂等注册并升级 `script-breakdown` 剧本拆解技能。

剧本拆解分为分析、人物、场景、道具四个 Agent 阶段。每个阶段由 Agent 直接调用 `write_project_resource` 工具，将自己编写的完整 Markdown 正文保存为本地 Blob 和 Asset；宿主只校验类型、项目、名称与内容长度，不代写资源内容。每个人物、独立场景和关键道具各有一个逻辑资源；同名输出和后续修改保存为该资源的不可变新版本，分析稿继续保留，并通过 `SkillRuns.OutputAssetId` 关联主输出。

## 资产与 Blob

资产元数据写入 SQLite，文件内容通过 `IBlobStorage` 存储。当前实现为本地文件系统：

```text
src/AlexDirectorConsole.Api/App_Data/blobs/
```

该目录和 SQLite 文件均不进入版本控制。默认单文件上限为 100 MB，可通过 `BlobStorage:MaxUploadBytes` 调整。

资产接口：

- `GET /api/projects/{projectId}/assets?type=script`：按项目和类型列出逻辑资源的最新版本
- `POST /api/projects/{projectId}/assets`：multipart 上传，字段为 `file`、`type` 和可选 `name`
- `GET /api/assets/{assetId}/versions`：列出该逻辑资源的全部版本
- `GET /api/assets/{assetId}/content`：读取或下载 Blob 内容

新增迁移：

```powershell
dotnet ef migrations add <MigrationName> `
	--project src/AlexDirectorConsole.Api `
	--startup-project src/AlexDirectorConsole.Api `
	--output-dir Data/Migrations
```

## Azure AI Foundry 对话

复制 `.env.example` 为项目根目录的 `.env`，填写 Azure AI Foundry 中 Azure OpenAI 模型部署的连接信息：

```dotenv
AZURE_OPENAI_ENDPOINT=https://<resource-name>.openai.azure.com/
AZURE_OPENAI_API_KEY=<api-key>
AZURE_OPENAI_DEPLOYMENT=gpt-5.4

# 可复用上面的 Endpoint 和 API Key；默认质量为 medium。
AZURE_IMAGE_DEPLOYMENT=gpt-image-2
AZURE_IMAGE_QUALITY=medium
AZURE_IMAGE_API_VERSION=2025-04-01-preview
```

`.env` 已被 Git 忽略。修改后需要重启 API。后端使用 Azure OpenAI ChatClient，并通过 Microsoft Agent Framework `HarnessAgent` 运行执行副导演。文件技能由官方 `AgentSkillsProvider` 从 `Skills/**/SKILL.md` 发现，按“公布元数据 → `load_skill` 按需加载 → 读取资源或执行工具”的渐进模式工作；Harness 负责函数调用循环、会话内历史和上下文压缩。导演要求生成图片时，Agent 会调用 `generate_image`，通过 Azure Foundry 的 `gpt-image-2` 部署生成 `1024x1024` PNG；默认质量为 `medium`，结果保存为项目素材资源。

对话接口：

- `GET /api/projects/{projectId}/messages`：读取项目对话历史
- `POST /api/projects/{projectId}/messages`：发送导演指令并获取执行副导演回复
- `POST /api/projects/{projectId}/messages/stream`：以 `application/x-ndjson` 流式返回接令、技能、工具、Agent、解析、资源创建、文本增量和完成事件

消息请求始终携带界面当前选中版本的 `assetId`，不再由用户提交 `skillId`。统一执行副导演会读取当前资源元数据及文本正文，并根据导演令自行决定直接回复、调用 `run_script_breakdown`，或调用 `update_current_resource` 创建当前逻辑资源的新版本。旧版本 Blob 不覆盖，可在右侧版本菜单中审阅。`SkillRuns.ResultJson` 仅保存实际工具产物的 Asset 审计清单，不作为 Agent 的创作输出。

技能接口：

- `GET /api/skills`：列出系统技能及启停状态
- `PATCH /api/skills/{skillId}`：启用或停用技能
- `GET /api/projects/{projectId}/skill-runs`：读取最近 50 次技能运行

场景一使用方式：选择一个剧本资源，输入“分析一下，并建立每个人物、场景和关键道具的设定稿”并发送。Agent 会自行路由到剧本拆解技能；执行期间，对话气泡实时显示四个 Agent 阶段及每次 `write_project_resource` 调用。选择任意文本设定稿后可直接说“修改当前稿”，无需再次说明资源名称，Agent 会基于已载入的完整正文调用更新工具。运行结果可在左侧资源分类和技能审计记录中查看。

未配置时发送接口返回 `503`；Foundry 请求失败时返回 `502`，两种情况都不会写入消息历史。

## 验证

```powershell
dotnet build AlexDirectorConsole.sln
cd src/web
npm run lint
npm run build
```

健康检查：

- `GET /api/health`
- `GET /api/agent/status`
