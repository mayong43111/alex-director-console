import { Button, Tooltip } from "antd";
import { useEffect, useState } from "react";
import { AudioLines, Bot, MessageSquareText, Plus, Server, Sparkles } from "lucide-react";
import { Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { assistantDirectorAgent } from "../api/sessions";
import { AssistantDirectorPanel } from "../components/AssistantDirectorPanel";
import { ProjectRailNavigation } from "../navigation/ProjectRailNavigation";
import { AppLayout } from "./AppLayout";
import { SettingsLayout } from "./SettingsLayout";
import { StandardWorkspaceLayout } from "./StandardWorkspaceLayout";
import { TitledWorkspaceLayout } from "./TitledWorkspaceLayout";

export function ApplicationLayout() {
  const location = useLocation();
  const navigate = useNavigate();
  const { projectId } = useParams();
  const projectBase = projectId ? `/projects/${projectId}` : undefined;
  const settingsBase = projectBase ? `${projectBase}/settings` : "/settings";
  const inSettings = location.pathname.startsWith(`${settingsBase}/`);
  const workspaceTitle = location.pathname.endsWith("/sessions")
    ? "Session 管理"
    : location.pathname.endsWith("/agents")
    ? "Agent 管理"
    : location.pathname.endsWith("/skills")
      ? "技能目录"
      : location.pathname.endsWith("/voices")
        ? "语音包库"
      : inSettings ? "服务器连接" : "项目";
  const workspaceDescription = location.pathname.endsWith("/sessions")
    ? "查看不同 Agent 与业务 scope 下独立保存的消息历史"
    : location.pathname.endsWith("/agents")
    ? "维护 Agent 名称、系统提示词与关联技能"
    : location.pathname.endsWith("/skills")
      ? "管理技能版本、工具权限与项目副本"
      : location.pathname.endsWith("/voices")
        ? "跨项目维护角色可绑定的多引擎固定声音"
      : inSettings
        ? "项目只引用服务能力，不在项目内保存密钥"
        : "从创意设定到分集交付的全部制作空间";
  const [isAgentOverlay, setIsAgentOverlay] = useState(
    () => window.matchMedia("(max-width: 1023px)").matches,
  );
  const [agentOpen, setAgentOpen] = useState(
    () => !inSettings && window.matchMedia("(min-width: 1024px)").matches,
  );

  useEffect(() => {
    const mobileQuery = window.matchMedia("(max-width: 1023px)");
    const syncAgentVisibility = (event: MediaQueryListEvent | MediaQueryList) => {
      setIsAgentOverlay(event.matches);
      setAgentOpen(!inSettings && !event.matches);
    };
    syncAgentVisibility(mobileQuery);
    mobileQuery.addEventListener("change", syncAgentVisibility);
    return () => mobileQuery.removeEventListener("change", syncAgentVisibility);
  }, [inSettings]);

  const titledWorkspace = (
    <TitledWorkspaceLayout
      title={workspaceTitle}
      description={workspaceDescription}
      projectName="Alex 导演台"
      projectHome="/"
      pathname={location.pathname}
      compact
      actions={!inSettings ? (
        <Button
          type="primary"
          icon={<Plus size={15} />}
            onClick={() => window.dispatchEvent(new Event("alex:create-project"))}
        >
          创建项目
        </Button>
      ) : undefined}
    >
      {inSettings ? (
        <SettingsLayout
          pathname={location.pathname}
          items={[
            {
              label: "服务器连接",
              to: `${settingsBase}/services`,
              icon: <Server size={16} />,
            },
            {
              label: "语音包库",
              to: `${settingsBase}/voices`,
              icon: <AudioLines size={16} />,
            },
            {
              label: "Agent 管理",
              to: `${settingsBase}/agents`,
              icon: <Bot size={16} />,
            },
            {
              label: "Agent 技能",
              to: `${settingsBase}/skills`,
              icon: <Sparkles size={16} />,
            },
            {
              label: "Session 管理",
              to: `${settingsBase}/sessions`,
              icon: <MessageSquareText size={16} />,
            },
          ]}
        >
          <Outlet />
        </SettingsLayout>
      ) : (
        <Outlet />
      )}
    </TitledWorkspaceLayout>
  );

  return (
    <AppLayout
      agentOpen={!inSettings && agentOpen}
      headerStart={(
        <>
          <button className="director-brand" onClick={() => navigate("/")} aria-label="返回项目中心">
            <span className="brand-mark">A</span>
            <span>Alex 导演台</span>
          </button>
          <span className="director-header-divider" />
          <span className="application-section-title">
            {inSettings ? "全局设置" : "项目中心"}
          </span>
        </>
      )}
      headerActions={!inSettings ? (
        <>
          <Tooltip title={agentOpen ? "收起 Agent" : "展开 Agent"}>
            <Button
              type={agentOpen ? "primary" : "default"}
              icon={<Bot size={16} />}
              aria-label="Agent"
              aria-expanded={agentOpen}
              onClick={() => setAgentOpen((open) => !open)}
            />
          </Tooltip>
        </>
      ) : undefined}
    >
      <StandardWorkspaceLayout
        navigationLabel="应用导航"
        agentOverlay={isAgentOverlay}
        onCloseAgent={() => setAgentOpen(false)}
        navigation={(
          <ProjectRailNavigation
            activeKey={inSettings ? "global-settings" : "project-center"}
            projectBase={inSettings ? projectBase : undefined}
          />
        )}
        agent={agentOpen ? (
          <AssistantDirectorPanel
            agent={assistantDirectorAgent}
            session={{
              scopeKey: "global:project-center:assistant-director",
              title: "项目中心",
              page: "项目中心",
            }}
            onClose={() => setAgentOpen(false)}
          />
        ) : undefined}
      >
        {titledWorkspace}
      </StandardWorkspaceLayout>
    </AppLayout>
  );
}