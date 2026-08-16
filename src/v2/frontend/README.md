# React + TypeScript + Vite

This template provides a minimal setup to get React working in Vite with HMR and some ESLint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the ESLint configuration

If you are developing a production application, we recommend updating the configuration to enable type-aware lint rules:

```js
export default defineConfig([
  # Alex Director UI

  Alex Director UI 是创意生产系统的独立 React 前端。当前版本实现完整页面结构、响应式布局和本地交互，尚未连接业务 API。

  ## 页面结构

  - 项目中心：项目列表与创建流程入口。
  - 项目工作台：驾驶舱、设定、故事结构、资产、剧本、视觉参考、分镜、生产和审阅。
  - 全局设置：服务连接与 Agent 技能目录。
  - 应用壳：顶部上下文、制作导航、Agent 副驾驶和活动状态栏。

  页面按业务资源划分。原文集和生产集使用不同标识；资产属于项目共享范围；剧本、分镜、媒体任务和审阅属于生产集范围。

  ## 数据与 API 边界

  当前展示数据位于 `src/data/mockData.ts` 和各页面顶部的只读 mock 集合中。接入 API 时应将这些集合迁移到独立的 query/service 层，页面组件只消费类型化结果，不直接拼接接口 URL。

  推荐保持以下资源边界：

  - `projects`：项目、全局设定和版本。
  - `source-episodes`：原文集与原文片段。
  - `production-episodes`：生产集与改编映射。
  - `assets`：项目共享的人物、场景、道具和参考图。
  - `scripts`、`storyboards`、`runs`、`reviews`：按生产集隔离。

  ## 本地运行

  ```powershell
  npm install
  npm run dev
  ```

  生产校验：

  ```powershell
  npm run build
  npm run lint
  ```

  ## 响应式约定

  - `>= 1280px`：大屏工作台，包含固定导航和 Agent 面板。
  - `< 1280px`：手机模式，导航使用抽屉，Agent 使用覆盖层。
  - `1280 x 800` 是最低大屏验收尺寸。
    ],
