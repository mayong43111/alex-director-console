# Alex Director Console V2 Backend

此目录包含 V2 ASP.NET Core API、CQRS 应用层和验收测试，与 `../frontend` 独立构建和部署。

后端应引用 `../database/AlexDirectorConsole.V2.Database.csproj` 并复用 `V2DbContext`。Schema、EF Core 迁移和初始化数据只由 `../database` 所有，不在后端项目中重复定义。

## CQRS 约定

- HTTP Endpoint 只负责协议映射和 Command 分发，不直接使用 `V2DbContext`。
- Command Handler 拥有写入用例、输入规范化、业务校验和事务提交。
- Query 与 Command 分开建模；创建响应直接使用 Command Result，不额外查询。
- 每个功能使用 `Features/<Domain>/<UseCase>` 垂直切片目录。

当前已实现：

```http
POST /api/v2/projects
```

该端点创建空项目，不自动创建生产集、资产或创作设定。详细验收条件见 `docs/user-stories/create-project.md`。

## 运行

```powershell
dotnet run --project src/backend/AlexDirectorConsole.V2.Api
```

默认数据库与 DB 项目共用 `../database/App_Data/alex-director-v2.db`，也可通过 `ConnectionStrings__V2Database` 覆盖。
默认 HTTP 地址为 `http://127.0.0.1:6275`，与 V2 前端开发代理一致。

## 测试

```powershell
dotnet test src/backend/AlexDirectorConsole.V2.Api.Tests
```

验收测试使用独立临时 SQLite 数据库并应用真实 EF Core 迁移。