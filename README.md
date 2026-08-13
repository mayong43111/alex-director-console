# Alex Director Console / Alex 导演台

[简体中文](#简体中文) | [English](#english)

An AI filmmaking workspace where the user directs and an agent executes.

一个面向 AI 影视制作的导演工作台：用户下达导演令，Agent 作为执行副导演调用技能与工具完成工作。

> [!IMPORTANT]
> This repository does not currently include an open-source license. Public source availability does not grant permission to use, modify, or redistribute the code. See [License](#许可证--license).

---

## 简体中文

### 项目简介

Alex 导演台将项目、剧本、人物、场景、道具、分镜、图片和视频组织在同一工作区。执行副导演会结合当前资源、项目画幅和对话历史，自主选择 Agent Skill 与工具，并通过 NDJSON 实时返回执行过程。

项目目前处于积极开发阶段，适合本地开发和工作流验证；用于生产环境前请自行完成安全与可靠性评估。

### 核心能力

- **项目化制作**：在 SQLite 中持久化项目设置、资源、版本、对话和技能运行记录。
- **流式导演对话**：通过 Azure AI Foundry / Azure OpenAI 接收导演令并实时呈现 Agent、技能和工具进度。
- **剧本创作与改写**：从零创作完整剧本，或自主定位并读取已有剧本后，将重写结果保存为不可变新版本。
- **剧本拆解**：从文本剧本提取分析稿、人物、场景和关键道具资源。
- **不可变资源版本**：修改资源时创建新版本，保留历史 Blob，支持版本审阅。
- **图片工作流**：生成、编辑、合并参考图，并按视觉参考或项目画幅选择输出尺寸。
- **Azure 配音工作流**：由 Agent 将旁白或对白生成音频，保存原文、voice、语速、格式和模型参数并直接播放。
- **ComfyUI 视频工作流**：检查和管理远端 ComfyUI，执行 MiniMax H3 首尾帧视频、静帧组装和片段拼接。
- **可管理技能**：从 `Skills/**/SKILL.md` 发现 Agent Skill，并支持启停与运行审计。

### 技术栈

| 层 | 技术 |
| --- | --- |
| Web | React 19、TypeScript 6、Vite 8、React Router、Lucide |
| API | ASP.NET Core 8 Minimal API、EF Core 8、SQLite |
| Agent | Microsoft Agent Framework Harness、Azure AI OpenAI |
| 媒体 | Azure Image、Azure TTS、ComfyUI、MiniMax H3、ImageSharp |
| 存储 | SQLite 元数据、本地文件 Blob |

### 环境要求

- Windows 10/11（开发启动脚本使用 PowerShell）
- [.NET SDK 8.0.423](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 24+](https://nodejs.org/)
- npm 11+
- Azure AI Foundry 中可用的 Azure OpenAI 部署
- 可选：用于视频工作流的远端 VM、ComfyUI 和对应模型

版本由 [global.json](global.json) 和 [package.json](src/web/package.json) 约束。

### 快速开始

1. 克隆仓库：

   ```powershell
   git clone https://github.com/mayong43111/alex-director-console.git
   cd alex-director-console
   ```

2. 创建本地环境文件：

   ```powershell
   Copy-Item .env.example .env
   ```

3. 编辑 `.env`，至少填写：

   ```dotenv
   AZURE_OPENAI_ENDPOINT=https://<resource-name>.openai.azure.com/
   AZURE_OPENAI_API_KEY=<api-key>
   AZURE_OPENAI_DEPLOYMENT=gpt-5.4

   AZURE_IMAGE_DEPLOYMENT=gpt-image-2
   AZURE_IMAGE_QUALITY=medium
   AZURE_IMAGE_API_VERSION=2025-04-01-preview

   AZURE_SPEECH_DEPLOYMENT=tts
   AZURE_SPEECH_API_VERSION=2025-03-01-preview
   ```

4. 一键启动开发环境：

   ```powershell
   .\start-dev.ps1
   ```

   已安装前端依赖时可跳过安装：

   ```powershell
   .\start-dev.ps1 -SkipInstall
   ```

5. 打开以下地址：

   - Web：<http://localhost:6173>
   - API：<http://localhost:5174>
   - Swagger：<http://localhost:5174/swagger>

也可以分别启动：

```powershell
dotnet run --project src/AlexDirectorConsole.Api --launch-profile http
npm install --prefix src/web
npm run dev --prefix src/web
```

### 配置

`.env` 仅用于数据库中尚无对应全局配置时的首次初始化。之后以系统配置页面保存的数据库值为准，重启不会用 `.env` 覆盖。

#### Azure AI

| 变量 | 必需 | 说明 |
| --- | --- | --- |
| `AZURE_OPENAI_ENDPOINT` | 是 | Azure OpenAI Endpoint |
| `AZURE_OPENAI_API_KEY` | 是 | Azure OpenAI API Key；不要提交到 Git |
| `AZURE_OPENAI_DEPLOYMENT` | 是 | 对话模型部署名 |
| `AZURE_IMAGE_DEPLOYMENT` | 图片功能 | 图片模型部署名 |
| `AZURE_IMAGE_QUALITY` | 否 | `low`、`medium` 或 `high`，默认 `medium` |
| `AZURE_IMAGE_API_VERSION` | 否 | 图片 API 版本 |
| `AZURE_SPEECH_DEPLOYMENT` | 配音功能 | 语音模型部署名；当前资源使用 `tts` |
| `AZURE_SPEECH_API_VERSION` | 否 | 语音 API 版本 |

当前 `tts` 部署支持 `alloy`、`echo`、`fable`、`nova`、`onyx`、`shimmer`。Endpoint 和 API Key 未单独配置时复用 `AZURE_OPENAI_*`。

#### VM 与 ComfyUI

视频工作流需要以下首次初始化变量：

```dotenv
VM_HOST=
VM_PORT=22
VM_USERNAME=azureuser
SSH_PRIVATE_KEY_PATH=%USERPROFILE%\.ssh\id_rsa
COMFYUI_PATH=/home/azureuser/ComfyUI
COMFYUI_PYTHON_PATH=/home/azureuser/envs/comfy311/bin/python
COMFYUI_PORT=8188
COMFYUI_LOCAL_PROXY_PORT=8188
COMFYUI_WORKFLOW_DIRECTORY=/home/azureuser/ComfyUI/user/default/workflows
COMFYUI_OUTPUT_DIRECTORY=/home/azureuser/ComfyUI/output
```

完整模板见 [.env.example](.env.example)。

### 项目结构

```text
alex-director-console/
├─ src/
│  ├─ AlexDirectorConsole.Api/
│  │  ├─ Application/       # 用例、资产边界、维护任务
│  │  ├─ Contracts/         # HTTP DTO
│  │  ├─ Data/              # EF Core 与迁移
│  │  ├─ Endpoints/         # Minimal API 路由
│  │  ├─ Models/            # 持久化模型
│  │  ├─ Services/          # Foundry、ComfyUI、Agent Skill
│  │  ├─ Storage/           # Blob 存储
│  │  ├─ Tools/             # Agent Tool 适配器
│  │  └─ Skills/            # Agent Skill 文件
│  └─ web/
│     └─ src/
│        ├─ api/            # HTTP 与 NDJSON 客户端
│        ├─ features/       # 业务功能与 hooks
│        └─ models/         # 前端契约
├─ docs/                    # 设计与重构文档
├─ .env.example
└─ start-dev.ps1
```

架构演进和边界约束见 [重构计划](docs/refactoring-plan.md)。

### 数据与维护

开发数据默认保存在：

```text
src/AlexDirectorConsole.Api/alex-director-console.db
src/AlexDirectorConsole.Api/App_Data/blobs/
```

数据库迁移会在 API 启动时自动应用。历史资产维护不会在普通 Web 启动时执行。升级旧数据前请备份 SQLite 和 `App_Data`，然后显式运行：

```powershell
dotnet run --project src/AlexDirectorConsole.Api -- --run-maintenance
```

命令会执行尚未完成的版本化维护任务并退出，状态记录在 `App_Data/maintenance-state.json`。

新增 EF Core 迁移：

```powershell
dotnet ef migrations add <MigrationName> `
  --project src/AlexDirectorConsole.Api `
  --startup-project src/AlexDirectorConsole.Api `
  --output-dir Data/Migrations
```

### API 概览

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/api/health` | 健康检查 |
| `GET` | `/api/agent/status` | Agent 与图片模型状态 |
| `GET` / `PUT` | `/api/projects`、`/api/projects/{id}` | 项目读取与保存 |
| `GET` / `POST` | `/api/projects/{id}/assets` | 资源列表与上传 |
| `GET` | `/api/projects/{projectId}/assets/{id}/versions` | 当前项目内的资源版本历史 |
| `DELETE` | `/api/projects/{projectId}/assets/{assetId}` | 删除逻辑资源、全部版本和镜头绑定 |
| `GET` | `/api/projects/{projectId}/assets/{id}/content` | 读取或下载当前项目 Blob |
| `GET` / `POST` | `/api/projects/{id}/messages` | 对话历史与非流式消息 |
| `POST` | `/api/projects/{id}/messages/stream` | NDJSON 流式导演会话 |
| `GET` / `PATCH` | `/api/skills`、`/api/skills/{id}` | 技能列表与启停 |

开发环境可通过 Swagger 查看完整契约。

### 开发与验证

```powershell
dotnet build AlexDirectorConsole.sln
npm run lint --prefix src/web
npm run build --prefix src/web
git diff --check
```

当前尚未引入自动化测试工程。提交改动前至少应通过以上检查，并手动验证受影响工作流。

### 贡献

欢迎通过 [Issues](https://github.com/mayong43111/alex-director-console/issues) 提交缺陷和建议。较大改动建议先创建 Issue，说明目标、行为变化和数据迁移影响。

提交 Pull Request 时请：

1. 保持 API、NDJSON 事件、Blob 路径和资源版本语义兼容，或明确记录破坏性变化。
2. 不提交 `.env`、API Key、SQLite 数据库、Blob、SSH 私钥或生成输出。
3. 保持 Endpoints、Application、Tools 和前端 feature 的职责边界。
4. 运行“开发与验证”中的全部命令。

### 安全说明

- 不要把 Azure Key、SSH 私钥或 VM 凭据提交到仓库。
- `.env` 只适合本地开发；生产部署应使用受控的机密存储。
- ComfyUI 管理工具可操作远端主机，启用前应限制网络、账号权限和可用 workflow。
- 发现安全问题时，请避免在公开 Issue 中披露密钥或可利用细节，优先通过仓库所有者的私密渠道联系。

---

## English

### Overview

Alex Director Console brings projects, scripts, characters, scenes, props, storyboards, images, and videos into one workspace. The director agent uses the active asset, project format, and conversation history to select Agent Skills and tools, while execution progress is streamed to the UI over NDJSON.

The project is under active development. It is intended for local development and workflow evaluation and should be reviewed before production use.

### Features

- **Project-based production**: persist project settings, assets, versions, conversations, and skill runs in SQLite.
- **Streaming director chat**: send instructions through Azure AI Foundry / Azure OpenAI and observe agent, skill, and tool progress in real time.
- **Script writing and revision**: create complete scripts or discover and read an existing script before persisting the rewrite as an immutable new version.
- **Script breakdown**: derive analysis, character, scene, and key-prop assets from a text script.
- **Immutable asset versions**: create a new version for every revision while preserving historical blobs.
- **Image workflows**: generate, edit, and merge references using either square asset framing or the project aspect ratio.
- **Azure voice-over workflows**: let the agent synthesize narration or dialogue while preserving the text, voice, speed, format, and model parameters.
- **ComfyUI video workflows**: inspect and manage remote ComfyUI, run MiniMax H3 frame-to-video workflows, assemble slideshows, and concatenate clips.
- **Manageable skills**: discover Agent Skills from `Skills/**/SKILL.md`, enable or disable them, and audit executions.

### Tech Stack

| Layer | Technology |
| --- | --- |
| Web | React 19, TypeScript 6, Vite 8, React Router, Lucide |
| API | ASP.NET Core 8 Minimal API, EF Core 8, SQLite |
| Agent | Microsoft Agent Framework Harness, Azure AI OpenAI |
| Media | Azure Image, Azure TTS, ComfyUI, MiniMax H3, ImageSharp |
| Storage | SQLite metadata and local file blobs |

### Prerequisites

- Windows 10/11 (the development launcher is a PowerShell script)
- [.NET SDK 8.0.423](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 24+](https://nodejs.org/)
- npm 11+
- An Azure OpenAI deployment in Azure AI Foundry
- Optional: a remote VM, ComfyUI, and the required models for video workflows

Versions are pinned by [global.json](global.json) and [package.json](src/web/package.json).

### Quick Start

1. Clone the repository:

   ```powershell
   git clone https://github.com/mayong43111/alex-director-console.git
   cd alex-director-console
   ```

2. Create the local environment file:

   ```powershell
   Copy-Item .env.example .env
   ```

3. Edit `.env` and provide at least:

   ```dotenv
   AZURE_OPENAI_ENDPOINT=https://<resource-name>.openai.azure.com/
   AZURE_OPENAI_API_KEY=<api-key>
   AZURE_OPENAI_DEPLOYMENT=gpt-5.4

   AZURE_IMAGE_DEPLOYMENT=gpt-image-2
   AZURE_IMAGE_QUALITY=medium
   AZURE_IMAGE_API_VERSION=2025-04-01-preview

   AZURE_SPEECH_DEPLOYMENT=tts
   AZURE_SPEECH_API_VERSION=2025-03-01-preview
   ```

4. Start the development environment:

   ```powershell
   .\start-dev.ps1
   ```

   Skip dependency installation when `node_modules` is already available:

   ```powershell
   .\start-dev.ps1 -SkipInstall
   ```

5. Open:

   - Web: <http://localhost:6173>
   - API: <http://localhost:5174>
   - Swagger: <http://localhost:5174/swagger>

To start each service separately:

```powershell
dotnet run --project src/AlexDirectorConsole.Api --launch-profile http
npm install --prefix src/web
npm run dev --prefix src/web
```

### Configuration

`.env` seeds a global configuration only when no corresponding database record exists. Subsequent restarts preserve values saved through the system configuration UI.

#### Azure AI

| Variable | Required | Description |
| --- | --- | --- |
| `AZURE_OPENAI_ENDPOINT` | Yes | Azure OpenAI endpoint |
| `AZURE_OPENAI_API_KEY` | Yes | Azure OpenAI API key; never commit it |
| `AZURE_OPENAI_DEPLOYMENT` | Yes | Chat model deployment name |
| `AZURE_IMAGE_DEPLOYMENT` | For images | Image model deployment name |
| `AZURE_IMAGE_QUALITY` | No | `low`, `medium`, or `high`; defaults to `medium` |
| `AZURE_IMAGE_API_VERSION` | No | Image API version |
| `AZURE_SPEECH_DEPLOYMENT` | For voice-over | Speech model deployment name; the current resource uses `tts` |
| `AZURE_SPEECH_API_VERSION` | No | Speech API version |

The current `tts` deployment supports `alloy`, `echo`, `fable`, `nova`, `onyx`, and `shimmer`. Speech falls back to `AZURE_OPENAI_*` when no separate endpoint or API key is configured.

#### VM and ComfyUI

Video workflows use the following first-run seed values:

```dotenv
VM_HOST=
VM_PORT=22
VM_USERNAME=azureuser
SSH_PRIVATE_KEY_PATH=%USERPROFILE%\.ssh\id_rsa
COMFYUI_PATH=/home/azureuser/ComfyUI
COMFYUI_PYTHON_PATH=/home/azureuser/envs/comfy311/bin/python
COMFYUI_PORT=8188
COMFYUI_LOCAL_PROXY_PORT=8188
COMFYUI_WORKFLOW_DIRECTORY=/home/azureuser/ComfyUI/user/default/workflows
COMFYUI_OUTPUT_DIRECTORY=/home/azureuser/ComfyUI/output
```

See [.env.example](.env.example) for the complete template.

### Repository Layout

```text
alex-director-console/
├─ src/
│  ├─ AlexDirectorConsole.Api/
│  │  ├─ Application/       # Use cases, asset boundaries, maintenance
│  │  ├─ Contracts/         # HTTP DTOs
│  │  ├─ Data/              # EF Core and migrations
│  │  ├─ Endpoints/         # Minimal API routes
│  │  ├─ Models/            # Persistence models
│  │  ├─ Services/          # Foundry, ComfyUI, Agent Skills
│  │  ├─ Storage/           # Blob storage
│  │  ├─ Tools/             # Agent Tool adapters
│  │  └─ Skills/            # Agent Skill files
│  └─ web/
│     └─ src/
│        ├─ api/            # HTTP and NDJSON clients
│        ├─ features/       # Features and hooks
│        └─ models/         # Frontend contracts
├─ docs/                    # Design and refactoring notes
├─ .env.example
└─ start-dev.ps1
```

See the [refactoring plan](docs/refactoring-plan.md) for architectural boundaries and evolution.

### Data and Maintenance

Development data is stored in:

```text
src/AlexDirectorConsole.Api/alex-director-console.db
src/AlexDirectorConsole.Api/App_Data/blobs/
```

Database migrations are applied automatically when the API starts. Historical asset maintenance is not part of normal web startup. Back up the SQLite database and `App_Data` before upgrading legacy data, then run:

```powershell
dotnet run --project src/AlexDirectorConsole.Api -- --run-maintenance
```

The command runs pending versioned maintenance tasks and exits. Completion state is stored in `App_Data/maintenance-state.json`.

Create an EF Core migration with:

```powershell
dotnet ef migrations add <MigrationName> `
  --project src/AlexDirectorConsole.Api `
  --startup-project src/AlexDirectorConsole.Api `
  --output-dir Data/Migrations
```

### API Overview

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/api/health` | Health check |
| `GET` | `/api/agent/status` | Agent and image model status |
| `GET` / `PUT` | `/api/projects`, `/api/projects/{id}` | Read and save projects |
| `GET` / `POST` | `/api/projects/{id}/assets` | List and upload assets |
| `GET` | `/api/projects/{projectId}/assets/{id}/versions` | Asset version history within the project |
| `DELETE` | `/api/projects/{projectId}/assets/{assetId}` | Delete a logical resource, all versions, and shot links |
| `GET` | `/api/projects/{projectId}/assets/{id}/content` | Read or download a project-scoped blob |
| `GET` / `POST` | `/api/projects/{id}/messages` | Conversation history and non-streaming messages |
| `POST` | `/api/projects/{id}/messages/stream` | Streaming director session over NDJSON |
| `GET` / `PATCH` | `/api/skills`, `/api/skills/{id}` | List, enable, or disable skills |

Swagger exposes the complete contract in development.

### Development and Validation

```powershell
dotnet build AlexDirectorConsole.sln
npm run lint --prefix src/web
npm run build --prefix src/web
git diff --check
```

The project does not currently include an automated test project. At minimum, run the checks above and manually verify affected workflows before submitting changes.

### Contributing

Bug reports and proposals are welcome through [Issues](https://github.com/mayong43111/alex-director-console/issues). For substantial changes, open an issue first and describe the goal, behavioral impact, and any data migration requirements.

When submitting a pull request:

1. Preserve API routes, NDJSON events, blob paths, and asset-version semantics, or document breaking changes explicitly.
2. Do not commit `.env`, API keys, SQLite databases, blobs, SSH keys, or generated output.
3. Keep responsibilities separated across Endpoints, Application, Tools, and frontend features.
4. Run every command in “Development and Validation.”

### Security

- Never commit Azure keys, SSH private keys, or VM credentials.
- `.env` is intended for local development; use managed secret storage in production.
- ComfyUI management tools can operate a remote host. Restrict network access, account permissions, and available workflows before enabling them.
- Do not disclose credentials or exploitable details in a public issue. Contact the repository owner privately for sensitive reports.

---

## 许可证 / License

本仓库目前**未包含 LICENSE 文件**。源代码公开可见不代表已授予使用、修改或再分发许可。在许可证补充前，请先联系仓库所有者获得授权。

This repository currently **does not include a LICENSE file**. Source availability does not grant permission to use, modify, or redistribute the code. Contact the repository owner for authorization until a license is added.
