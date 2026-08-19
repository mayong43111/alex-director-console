# 用户故事：创建项目

## 故事

作为一名导演或制作负责人，我希望创建一个空项目，以便获得独立的创作工作区，并在之后逐步补充全局设定、原文、生产集和资产。

## 验收条件

1. 用户提交项目名称和可选描述后，系统创建具有服务端生成 ID 和 UTC 时间戳的项目。
2. 项目名称去除首尾空白后必须为 1 至 200 个字符；描述去除首尾空白后最多 4000 个字符。
3. 创建成功返回 `201 Created`、项目表示和 `/api/v2/projects/{id}` 资源地址。
4. 输入无效返回 `400 Bad Request` 和标准 `application/problem+json` 错误，不写入数据库。
5. 创建项目只写入 `Projects` 表，不自动创建生产集、资产、资源状态或创作设定。
6. 相同名称允许存在；项目身份只由 ID 确定。

## API 契约

```http
POST /api/v2/projects
Content-Type: application/json

{
  "name": "天桥食堂",
  "description": "都市悬疑短片"
}
```

成功响应：

```json
{
  "id": "uuid",
  "name": "天桥食堂",
  "description": "都市悬疑短片",
  "currentCreativeSettingsId": null,
  "createdAtUtc": "2026-08-16T00:00:00+00:00",
  "updatedAtUtc": "2026-08-16T00:00:00+00:00"
}
```

## CQRS 边界

- Command：`CreateProjectCommand` 只表达创建意图。
- Handler：校验和规范化输入，创建 `Project` 聚合并提交一次事务。
- Endpoint：完成 HTTP 映射和 Command 分发，不直接访问 `V2DbContext`。
- Query：本故事不实现项目查询；响应使用 Command Result，避免创建后额外读取。