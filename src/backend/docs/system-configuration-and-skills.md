# 系统配置与 Skill 管理

## 目标

V2 当前只接入 Azure AI Foundry，LLM 部署固定为 `gpt-5.4`。项目只引用全局能力，不复制 Endpoint 或密钥。系统 Skill 由随服务部署的 `SKILL.md` 定义，数据库保存可变的启停状态。

## Azure AI Foundry

全局配置是固定主键为 `1` 的单例：

- `Endpoint`：Azure AI Foundry/OpenAI 资源地址。
- `Deployment`：固定为 `gpt-5.4`，不接受前端切换到其他模型。
- `ProtectedApiKey`：使用 ASP.NET Core Data Protection 加密后写入 SQLite。
- `UpdatedAtUtc`：最后保存时间。

Windows 本机的 Data Protection 根密钥使用 DPAPI 保护，文件位于 API 的 `App_Data/DataProtection`，该目录不进入 Git。API Key 只写不读：任何响应和错误信息都不包含明文或密文。

接口：

```text
GET  /api/v2/system/foundry-configuration
PUT  /api/v2/system/foundry-configuration
POST /api/v2/system/foundry-configuration/test
```

连接测试通过 Azure OpenAI SDK 对 `gpt-5.4` 发起最小请求。没有真实 Endpoint 和 API Key 时，系统只能验证配置、加密和 UI 闭环，不能声明模型已连通。

## Skill 目录

每个系统 Skill 位于 `Skills/<skill-id>/SKILL.md`。YAML frontmatter 是名称、说明、版本和允许工具的事实来源：

```yaml
---
name: storyboard-design
title: 分镜设计
description: 将已批准剧本转换为结构化镜头。
version: 2.1.0
allowed-tools: query_storyboard write_storyboard
---
```

服务启动时扫描目录并同步 `SkillDefinitions`：

- 新 Skill 默认启用。
- 标题、说明、版本和来源路径随文件更新。
- 启停状态由数据库保留，不被同步覆盖。
- 已从部署目录删除的系统 Skill 会从目录状态中移除。

接口：

```text
GET   /api/v2/skills
GET   /api/v2/skills/{skillId}
PATCH /api/v2/skills/{skillId}
```

后端执行器接入 Skill 时必须同时检查 `IsEnabled` 和 `AllowedTools`，不能只依赖前端开关。

## 当前范围

已实现：Foundry 配置、加密保存、连接测试、系统 Skill 同步、列表、详情、搜索和启停。

暂不开放：Skill 文件上传、直接编辑系统 Skill、项目副本和版本发布。这些操作需要先定义项目覆盖优先级、审计、回滚、路径沙箱和工具权限审批，界面中的相应按钮保持禁用。

## 后续阶段

1. 将 Foundry 客户端注入 Agent 运行时，任务记录实际 deployment、token 和错误分类。
2. 在工具执行前增加 Skill 工具白名单授权检查。
3. 增加项目级 Skill 副本、草稿版本、发布与回滚。
4. 增加 Azure Managed Identity/Key Vault，替代生产环境本机密钥文件。