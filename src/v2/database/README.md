# Alex Director Console V2 Database

该项目拥有 V2 的 SQLite Schema 和 EF Core 迁移。V2 后端应引用此项目并复用 `V2DbContext`，不要在 API 项目中复制表定义。

## Schema 边界

- 项目与生产集：`Projects`、`ProductionEpisodes`。
- 版本与血缘：`Assets`、`ResourceStates`、`AssetDependencies`。
- 创作索引：`VisualReferences`、`ShotDefinitions`、`ShotBeatClaims`、`ShotAssetLinks`。
- 决策与校验：`DirectorDecisions`、`ValidationRuns`、`ValidationResults`。
- Agent 执行：`AgentTasks`、`AgentTaskItems`、`AgentTaskEvents`、`AgentTaskOutputs`。
- 媒体生产：`ProductionRuns`、`ProductionRunItems`。

章节、爆点、原文分集、改编映射、场次和台词正文以统一文档信封写入 `Assets.DocumentJson`。需要外键、版本指针、反向查询或生产集隔离的内容使用关系表。

## 初始化

```powershell
dotnet run --project src/v2/database -- init
```

初始化只创建或升级 Schema，不写入项目、生产集、资产或其他业务数据。

从仓库运行时，默认数据库写入 `src/v2/database/App_Data/alex-director-v2.db`。生产环境应显式指定连接字符串：

```powershell
$env:ALEX_V2_DB_CONNECTION = "Data Source=C:\data\alex-director-v2.db"
dotnet run --project src/v2/database -- init
```

`migrate` 是供部署流程使用的等价命令，同样只应用迁移：

```powershell
dotnet run --project src/v2/database -- migrate
```

检查迁移和核心数据：

```powershell
dotnet run --project src/v2/database -- status
```

## 创建迁移

```powershell
dotnet ef migrations add <MigrationName> `
  --project src/v2/database `
  --startup-project src/v2/database `
  --output-dir Data/Migrations
```

结构化创作文档以 `Asset.DocumentJson` 保存，使用统一文档信封。`ResourceState` 保存逻辑资源的当前版本指针；运行、校验和 Agent 任务必须固定具体 `AssetId`。