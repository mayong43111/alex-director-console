import { Badge, Button, Tooltip } from "antd";
import { useEffect, useState } from "react";
import { ArrowLeft, Bot, LayoutDashboard, MessageSquareText, Plus, Server, Settings, Sparkles } from "lucide-react";
import { Link, Outlet, useLocation, useNavigate } from "react-router-dom";
import { assistantDirectorAgent } from "../api/sessions";
import { AssistantDirectorPanel } from "../components/AssistantDirectorPanel";
import { AppLayout } from "./AppLayout";
import { SettingsLayout } from "./SettingsLayout";
import { StandardWorkspaceLayout } from "./StandardWorkspaceLayout";
import { TitledWorkspaceLayout } from "./TitledWorkspaceLayout";

export function ApplicationLayout() {
  const location = useLocation();
  const navigate = useNavigate();
  const inSettings = location.pathname.startsWith("/settings/");
  const returnTo = sessionStorage.getItem("alex-director-v2.lastProjectPath") ?? "/";
  const workspaceTitle = location.pathname.endsWith("/sessions")
    ? "Session 管理"
    : location.pathname.endsWith("/agents")
    ? "Agent 管理"
    : location.pathname.endsWith("/skills")
      ? "技能目录"
      : inSettings ? "服务器连接" : "项目";
  const workspaceDescription = location.pathname.endsWith("/sessions")
    ? "查看不同 Agent 与业务 scope 下独立保存的消息历史"
    : location.pathname.endsWith("/agents")
    ? "维护 Agent 名称、系统提示词与关联技能"
    : location.pathname.endsWith("/skills")
      ? "管理技能版本、工具权限与项目副本"
      : inSettings
        ? "项目只引用服务能力，不在项目内保存密钥"
        : "从创意设定到分集交付的全部制作空间";
  const [isAgentOverlay, setIsAgentOverlay] = useState(
    () => window.matchMedia("(max-width: 1279px)").matches,
  );
  const [agentOpen, setAgentOpen] = useState(
    () => !inSettings && window.matchMedia("(min-width: 1280px)").matches,
  );

  useEffect(() => {
    const mobileQuery = window.matchMedia("(max-width: 1279px)");
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
              to: "/settings/services",
              icon: <Server size={16} />,
            },
            {
              label: "Agent 管理",
              to: "/settings/agents",
              icon: <Bot size={16} />,
            },
            {
              label: "Agent 技能",
              to: "/settings/skills",
              icon: <Sparkles size={16} />,
            },
            {
              label: "Session 管理",
              to: "/settings/sessions",
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
      headerActions={inSettings ? (
        <>
          <Button icon={<LayoutDashboard size={16} />} onClick={() => navigate("/")}>
            项目中心
          </Button>
          {returnTo !== "/" && (
            <Button type="primary" icon={<ArrowLeft size={16} />} onClick={() => navigate(returnTo)}>
              返回项目
            </Button>
          )}
        </>
      ) : (
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
      )}
    >
      <StandardWorkspaceLayout
        navigationLabel="应用导航"
        agentOverlay={isAgentOverlay}
        onCloseAgent={() => setAgentOpen(false)}
        navigation={(
          <>
            <nav className="director-rail-nav">
              <Tooltip title="项目中心" placement="right">
                <Link
                  className={`director-rail-link ${inSettings ? "" : "active"}`}
                  to="/"
                  aria-label="项目中心"
                  aria-current={inSettings ? undefined : "page"}
                >
                  <LayoutDashboard size={19} strokeWidth={1.8} />
                  <span>项目中心</span>
                </Link>
              </Tooltip>
            </nav>
            <div className="director-rail-footer">
              <Tooltip title="设置" placement="right">
                <Link
                  className={`director-rail-link ${inSettings ? "active" : ""}`}
                  to="/settings/services"
                  aria-label="设置"
                  aria-current={inSettings ? "page" : undefined}
                >
                  <Settings size={19} strokeWidth={1.8} />
                  <span>设置</span>
                </Link>
              </Tooltip>
              <Tooltip title="服务正常" placement="right">
                <div className="director-rail-status" aria-label="服务正常">
                  <Badge status="success" />
                </div>
              </Tooltip>
            </div>
          </>
        )}
        agent={agentOpen ? (
          <AssistantDirectorPanel
            agent={assistantDirectorAgent}
            session={{
              scopeKey: "global:project-center:assistant-director",
              title: "项目中心",
              page: "项目中心",
              context: [
                { label: "页面", value: "项目中心" },
              ],
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