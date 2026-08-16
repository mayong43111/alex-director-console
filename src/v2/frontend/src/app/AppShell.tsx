import { useEffect, useRef, useState, type FormEvent } from "react";
import { ProLayout } from "@ant-design/pro-components";
import {
  Badge,
  Button,
  Dropdown,
  Select,
  Tag,
  Tooltip,
  type MenuProps,
} from "antd";
import {
  Link,
  Outlet,
  useLocation,
  useNavigate,
  useParams,
} from "react-router-dom";
import {
  Bell,
  BookOpenText,
  Bot,
  Boxes,
  ChevronDown,
  ChevronRight,
  Clapperboard,
  Film,
  Gauge,
  Images,
  LayoutDashboard,
  RotateCcw,
  Search,
  SendHorizontal,
  Settings,
  SlidersHorizontal,
  Sparkles,
  X,
} from "lucide-react";
import { project } from "../data/mockData";
import {
  getProject,
  listProjects,
  type ProjectRecord,
} from "../api/projects";
import {
  getCopilotConversation,
  resetCopilotConversation,
  sendCopilotMessage,
  type CopilotMessage,
} from "../api/copilot";

const navigation = [
  { label: "驾驶舱", icon: LayoutDashboard, to: "overview" },
  { label: "项目设定", icon: SlidersHorizontal, to: "settings" },
  { label: "故事结构", icon: BookOpenText, to: "story/source/source-e01" },
  { label: "资产圣经", icon: Boxes, to: "assets/characters" },
  { label: "剧本", icon: Film, to: "script/episodes/production-e01" },
  { label: "视觉参考", icon: Images, to: "references" },
  {
    label: "分镜",
    icon: Clapperboard,
    to: "storyboard/episodes/production-e01",
  },
  { label: "生产", icon: Gauge, to: "production" },
  { label: "审阅交付", icon: Sparkles, to: "review/episodes/production-e01" },
];

const productionEpisodes = [
  { id: "production-e01", code: "E01", title: "失控的早晨" },
  { id: "production-e02", code: "E02", title: "追查与反转" },
  { id: "production-e03", code: "E03", title: "真相回收" },
];

type Workflow = {
  label: string;
  tabs: { label: string; to: string }[];
  next?: { label: string; to: string };
  queue?: string;
};

function getWorkflow(
  pathname: string,
  projectBase: string,
  episodeId: string,
): Workflow | undefined {
  if (pathname.endsWith("/settings")) {
    return {
      label: "项目设定",
      tabs: [],
      next: { label: "进入原文分集", to: `${projectBase}/story/source/source-e01` },
    };
  }
  if (pathname.includes("/story/")) {
    const next = pathname.includes("/source/")
      ? { label: "建立改编映射", to: `${projectBase}/story/outline` }
      : pathname.endsWith("/outline")
        ? { label: "规划章节与爆点", to: `${projectBase}/story/chapters` }
        : { label: "进入共享资产", to: `${projectBase}/assets/characters` };
    return {
      label: "故事结构",
      tabs: [
        { label: "原文分集", to: `${projectBase}/story/source/source-e01` },
        { label: "改编映射", to: `${projectBase}/story/outline` },
        { label: "章节与爆点", to: `${projectBase}/story/chapters` },
      ],
      next,
    };
  }
  if (pathname.includes("/assets/")) {
    return {
      label: "项目共享资产",
      tabs: [
        { label: "人物", to: `${projectBase}/assets/characters` },
        { label: "场景", to: `${projectBase}/assets/scenes` },
        { label: "道具", to: `${projectBase}/assets/props` },
      ],
      queue: "2 项待确认",
      next: { label: "检查视觉参考", to: `${projectBase}/references` },
    };
  }
  if (pathname.includes("/script/")) {
    const next = pathname.endsWith("/duration")
      ? { label: "打开分集剧本", to: `${projectBase}/script/episodes/${episodeId}` }
      : pathname.includes("/episodes/")
        ? { label: "检查台词与配音", to: `${projectBase}/script/dialogue` }
        : { label: "进入视觉参考", to: `${projectBase}/references` };
    return {
      label: "剧本",
      tabs: [
        { label: "时长仪表", to: `${projectBase}/script/duration` },
        { label: "分集剧本", to: `${projectBase}/script/episodes/${episodeId}` },
        { label: "台词本", to: `${projectBase}/script/dialogue` },
      ],
      queue: pathname.endsWith("/dialogue") ? "3 条待制作" : "1 项时长阻断",
      next,
    };
  }
  if (pathname.endsWith("/references")) {
    return {
      label: "视觉参考",
      tabs: [],
      queue: "2 项待审阅",
      next: { label: "进入分镜工作区", to: `${projectBase}/storyboard/episodes/${episodeId}` },
    };
  }
  if (pathname.includes("/storyboard")) {
    return {
      label: "分镜",
      tabs: [],
      queue: "1 个阻断镜头",
      next: { label: "创建生产任务", to: `${projectBase}/production/episodes/${episodeId}` },
    };
  }
  if (pathname.endsWith("/production") || pathname.includes("/production/")) {
    return {
      label: "生产",
      tabs: [],
      queue: "2 个失败项",
      next: { label: "进入成片审阅", to: `${projectBase}/review/episodes/${episodeId}` },
    };
  }
  if (pathname.includes("/review")) {
    return { label: "审阅交付", tabs: [], queue: "3 条开放批注" };
  }
  return undefined;
}

export function AppShell() {
  const location = useLocation();
  const navigate = useNavigate();
  const { projectId = "tianqiao" } = useParams();
  const mainCanvasRef = useRef<HTMLElement>(null);
  const [projects, setProjects] = useState<ProjectRecord[]>([]);
  const [projectName, setProjectName] = useState(() => {
    const navigationName = (location.state as { projectName?: string } | null)
      ?.projectName;
    if (navigationName) return navigationName;
    try {
      const cachedProject = sessionStorage.getItem(
        `alex-director-v2.project.${projectId}`,
      );
      return cachedProject
        ? ((JSON.parse(cachedProject) as { name?: string }).name ?? project.name)
        : project.name;
    } catch {
      return project.name;
    }
  });
  useEffect(() => {
    const controller = new AbortController();
    getProject(projectId, controller.signal)
      .then((loadedProject) => {
        if (!loadedProject) return;
        setProjectName(loadedProject.name);
        sessionStorage.setItem(
          `alex-director-v2.project.${loadedProject.id}`,
          JSON.stringify(loadedProject),
        );
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        console.warn("项目信息加载失败", error);
      });
    return () => controller.abort();
  }, [projectId]);
  useEffect(() => {
    sessionStorage.setItem("alex-director-v2.lastProjectPath", location.pathname);
    mainCanvasRef.current?.scrollTo({ top: 0, left: 0 });
  }, [location.pathname]);
  useEffect(() => {
    const controller = new AbortController();
    listProjects(controller.signal)
      .then(setProjects)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        console.warn("项目列表加载失败", error);
      });
    return () => controller.abort();
  }, []);
  useEffect(() => {
    const updateProject = (event: Event) => {
      const detail = (event as CustomEvent<{ projectId: string; name: string }>).detail;
      if (detail.projectId !== projectId) return;
      setProjectName(detail.name);
      setProjects((current) => current.map((item) =>
        item.id === detail.projectId ? { ...item, name: detail.name } : item));
      const cacheKey = `alex-director-v2.project.${detail.projectId}`;
      const cached = sessionStorage.getItem(cacheKey);
      if (cached) {
        sessionStorage.setItem(cacheKey, JSON.stringify({ ...JSON.parse(cached), name: detail.name }));
      }
    };
    window.addEventListener("alex:project-updated", updateProject);
    return () => window.removeEventListener("alex:project-updated", updateProject);
  }, [projectId]);
  const [agentOpen, setAgentOpen] = useState(
    () => window.matchMedia("(min-width: 1280px)").matches,
  );
  useEffect(() => {
    const mediaQuery = window.matchMedia("(min-width: 1280px)");
    const syncAgentState = (event?: MediaQueryListEvent) => {
      setAgentOpen(event?.matches ?? mediaQuery.matches);
    };
    syncAgentState();
    mediaQuery.addEventListener("change", syncAgentState);
    return () => mediaQuery.removeEventListener("change", syncAgentState);
  }, []);
  const [siderCollapsed, setSiderCollapsed] = useState(
    () => window.matchMedia("(max-width: 1366px)").matches,
  );
  useEffect(() => {
    const mediaQuery = window.matchMedia("(max-width: 1366px)");
    const syncCollapsedState = (event: MediaQueryListEvent) => {
      setSiderCollapsed(event.matches);
    };
    mediaQuery.addEventListener("change", syncCollapsedState);
    return () => mediaQuery.removeEventListener("change", syncCollapsedState);
  }, []);
  const [selectedEpisode, setSelectedEpisode] = useState("production-e01");
  const [queueOpen, setQueueOpen] = useState(false);
  const routeEpisode = location.pathname.match(/production-e\d+/)?.[0];
  const currentEpisode = routeEpisode ?? selectedEpisode;
  const currentEpisodeData =
    productionEpisodes.find((episode) => episode.id === currentEpisode) ??
    productionEpisodes[0];
  const projectBase = `/projects/${projectId}`;
  const workflow = getWorkflow(location.pathname, projectBase, currentEpisode);
  const scopedNavigation = navigation.map((item) => ({
    ...item,
    to: item.to.replace("production-e01", currentEpisode),
  }));
  const active = navigation.find((item) =>
    location.pathname.includes(item.to.split("/")[0]),
  );

  const changeEpisode = (episodeId: string) => {
    setSelectedEpisode(episodeId);
    if (location.pathname.includes("/script/episodes/")) {
      navigate(`${projectBase}/script/episodes/${episodeId}`);
    } else if (location.pathname.includes("/storyboard")) {
      navigate(`${projectBase}/storyboard/episodes/${episodeId}`);
    } else if (location.pathname.includes("/production")) {
      navigate(`${projectBase}/production/episodes/${episodeId}`);
    } else if (location.pathname.includes("/review")) {
      navigate(`${projectBase}/review/episodes/${episodeId}`);
    }
  };

  const switchProject = (nextProject: ProjectRecord) => {
    setProjectName(nextProject.name);
    navigate(`/projects/${nextProject.id}/overview`, {
      state: { projectName: nextProject.name },
    });
  };

  const proRoutes = scopedNavigation.map(({ label, icon: Icon, to }) => ({
    path: `${projectBase}/${to}`,
    name: label,
    icon: <Icon size={17} />,
  }));
  const projectMenuItems: MenuProps["items"] = [
    {
      key: "project-center",
      icon: <LayoutDashboard size={15} />,
      label: "返回项目中心",
    },
    { type: "divider" },
    {
      type: "group",
      label: "切换项目",
      children: projects.map((availableProject) => ({
        key: availableProject.id,
        label: (
          <span className="project-menu-option">
            <span className="project-menu-mark">
              {availableProject.name.slice(0, 1)}
            </span>
            <span>{availableProject.name}</span>
            {availableProject.id === projectId && <Tag color="blue">当前</Tag>}
          </span>
        ),
      })),
    },
  ];
  const handleProjectMenuClick: MenuProps["onClick"] = ({ key }) => {
    if (key === "project-center") {
      navigate("/");
      return;
    }
    const nextProject = projects.find((item) => item.id === key);
    if (nextProject) switchProject(nextProject);
  };

  return (
    <ProLayout
      className="alex-pro-layout"
      title="Alex 导演台"
      logo={<span className="brand-mark">A</span>}
      layout="mix"
      fixedHeader
      fixSiderbar
      siderWidth={208}
      breakpoint={false}
      collapsed={siderCollapsed}
      onCollapse={setSiderCollapsed}
      contentWidth="Fluid"
      route={{ path: projectBase, routes: proRoutes }}
      location={{ pathname: location.pathname }}
      menu={{ defaultOpenAll: true }}
      menuItemRender={(item, dom) =>
        item.path ? <Link to={item.path}>{dom}</Link> : dom
      }
      menuHeaderRender={(logo) => (
        <button className="pro-brand" onClick={() => navigate("/")}>
          {logo}
          <span>Alex 导演台</span>
        </button>
      )}
      headerTitleRender={() => (
        <Dropdown
          menu={{ items: projectMenuItems, onClick: handleProjectMenuClick }}
          trigger={["click"]}
        >
          <Button type="text" className="project-switcher">
            <span className="project-menu-mark"><Clapperboard size={14} /></span>
            <span className="project-switcher-name">{projectName}</span>
            <ChevronDown size={14} />
            <span className="header-current-page">{active?.label ?? "驾驶舱"}</span>
          </Button>
        </Dropdown>
      )}
      actionsRender={() => [
        <Select
          className="header-episode-select"
          key="episode"
          aria-label="当前生产集"
          value={currentEpisode}
          onChange={changeEpisode}
          options={productionEpisodes.map((episode) => ({
            value: episode.id,
            label: `${episode.code} · ${episode.title}`,
          }))}
        />,
        <Button className="command-search" icon={<Search size={15} />} key="search">
          搜索或执行命令 <kbd>⌘ K</kbd>
        </Button>,
        <Badge className="header-notifications" count={3} size="small" key="notifications">
          <Tooltip title="任务通知">
            <Button type="text" icon={<Bell size={17} />} aria-label="任务通知" />
          </Tooltip>
        </Badge>,
        <Tooltip title="全局设置" key="settings">
          <Button
            className="header-settings"
            type="text"
            icon={<Settings size={17} />}
            aria-label="全局设置"
            onClick={() => navigate("/settings/services")}
          />
        </Tooltip>,
        <Tooltip title="副导演" key="agent">
          <Button
            className="header-agent-button"
            type={agentOpen ? "primary" : "default"}
            icon={<Bot size={16} />}
            aria-label="副导演"
            onClick={() => setAgentOpen(!agentOpen)}
          />
        </Tooltip>,
      ]}
      menuFooterRender={() => (
        <div className="pro-service-status">
          <Badge status="success" />
          <span>服务正常</span>
          <b>4 / 4</b>
        </div>
      )}
    >
      <div className={`app-shell ${agentOpen ? "" : "agent-closed"}`}>
        <main className="main-canvas" ref={mainCanvasRef}>
          {workflow && (
            <WorkflowToolbar
              workflow={workflow}
              projectName={projectName}
              projectBase={projectBase}
              pathname={location.pathname}
            />
          )}
          <Outlet />
        </main>
        {agentOpen && (
          <AgentPanel
            key={projectId}
            projectId={projectId}
            projectName={projectName}
            page={active?.label ?? "驾驶舱"}
            episode={currentEpisodeData.code}
            onClose={() => setAgentOpen(false)}
          />
        )}
        {queueOpen && workflow?.queue && (
          <BatchReviewDialog
            title={workflow.queue}
            stage={workflow.label}
            onClose={() => setQueueOpen(false)}
          />
        )}
        <div className="activity-bar">
          <Badge status="processing" />
          <strong>2 个任务运行中</strong>
          <span>E01 视频 11/18</span>
          <span>E02 首帧 6/16</span>
          <button>展开活动</button>
        </div>
      </div>
    </ProLayout>
  );
}

function WorkflowToolbar({
  workflow,
  projectName,
  projectBase,
  pathname,
}: {
  workflow: Workflow;
  projectName: string;
  projectBase: string;
  pathname: string;
}) {
  const currentTab = workflow.tabs.find((tab) => pathname === tab.to);
  return (
    <div className="workflow-toolbar">
      <nav className="workflow-breadcrumb" aria-label="面包屑导航">
        <Link to={`${projectBase}/overview`}>{projectName}</Link>
        <ChevronRight size={13} />
        {currentTab ? (
          <>
            <Link to={workflow.tabs[0].to}>{workflow.label}</Link>
            <ChevronRight size={13} />
            <span aria-current="page">{currentTab.label}</span>
          </>
        ) : (
          <span aria-current="page">{workflow.label}</span>
        )}
      </nav>
    </div>
  );
}

function BatchReviewDialog({
  title,
  stage,
  onClose,
}: {
  title: string;
  stage: string;
  onClose: () => void;
}) {
  const items = [
    `${stage} · 高优先级异常`,
    `${stage} · 等待导演确认`,
    `${stage} · 受影响依赖检查`,
  ];
  const [selected, setSelected] = useState(items);
  const toggle = (item: string) =>
    setSelected((current) =>
      current.includes(item)
        ? current.filter((value) => value !== item)
        : [...current, item],
    );
  return (
    <div className="modal-backdrop review-queue-backdrop" onMouseDown={onClose}>
      <section
        className="review-queue-dialog"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header>
          <div>
            <span className="eyebrow">批量处理</span>
            <h2>{title}</h2>
          </div>
          <button className="icon-button" onClick={onClose} aria-label="关闭">
            <X size={17} />
          </button>
        </header>
        <p>Agent 已按阻断程度和依赖影响排序，批量动作仍需人工确认。</p>
        <div className="review-queue-list">
          {items.map((item, index) => (
            <label key={item}>
              <input
                type="checkbox"
                checked={selected.includes(item)}
                onChange={() => toggle(item)}
              />
              <span>
                <strong>{item}</strong>
                <small>影响 {index * 4 + 4} 个下游对象</small>
              </span>
            </label>
          ))}
        </div>
        <footer>
          <span>已选择 {selected.length} 项</span>
          <button className="secondary-button" onClick={onClose}>取消</button>
          <button className="primary-button" disabled={!selected.length} onClick={onClose}>
            确认批量处理
          </button>
        </footer>
      </section>
    </div>
  );
}

function AgentPanel({
  projectId,
  projectName,
  page,
  episode,
  onClose,
}: {
  projectId: string;
  projectName: string;
  page: string;
  episode: string;
  onClose: () => void;
}) {
  const [message, setMessage] = useState("");
  const [messages, setMessages] = useState<CopilotMessage[]>([]);
  const [runtime, setRuntime] = useState("MAF HarnessAgent");
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const scrollAnchor = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    getCopilotConversation(projectId, controller.signal)
      .then((conversation) => {
        setMessages(conversation.messages);
        setRuntime(conversation.runtime);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "副导演会话加载失败。");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [projectId]);

  useEffect(() => {
    scrollAnchor.current?.scrollIntoView({ block: "end" });
  }, [messages, sending]);

  async function sendMessage(content: string) {
    const trimmed = content.trim();
    if (!trimmed || sending) return;

    const pendingId = `pending-${Date.now()}`;
    setSending(true);
    setError(null);
    setMessage("");
    setMessages((current) => [
      ...current,
      {
        id: pendingId,
        sequence: current.length + 1,
        role: "user",
        content: trimmed,
        model: null,
        createdAtUtc: new Date().toISOString(),
      },
    ]);
    try {
      const conversation = await sendCopilotMessage(projectId, {
        content: trimmed,
        page,
        episode,
      });
      setMessages(conversation.messages);
      setRuntime(conversation.runtime);
    } catch (sendError) {
      setMessages((current) => current.filter((item) => item.id !== pendingId));
      setMessage(trimmed);
      setError(sendError instanceof Error ? sendError.message : "GPT-5.4 暂时无法回复。");
    } finally {
      setSending(false);
    }
  }

  async function clearConversation() {
    if (sending || messages.length === 0) return;
    setError(null);
    try {
      await resetCopilotConversation(projectId);
      setMessages([]);
    } catch (resetError) {
      setError(resetError instanceof Error ? resetError.message : "副导演会话清空失败。");
    }
  }

  function submitMessage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void sendMessage(message);
  }

  return (
    <aside className="agent-panel">
      <header className="agent-header">
        <div>
          <Bot size={18} />
          <strong>Agent 副导演</strong>
          <span className="online-dot" />
        </div>
        <div className="agent-header-actions">
          <button
            className="icon-button"
            onClick={clearConversation}
            disabled={sending || messages.length === 0}
            aria-label="清空副导演会话"
            title="清空副导演会话"
          >
            <RotateCcw size={16} />
          </button>
          <button className="icon-button" onClick={onClose} aria-label="关闭副导演">
            <X size={17} />
          </button>
        </div>
      </header>
      <div className="context-block">
        <span className="eyebrow">当前上下文</span>
        <div className="context-tags">
          <span>项目：{projectName}</span>
          <span>页面：{page}</span>
          <span>生产集：{episode}</span>
          <span>{runtime}</span>
        </div>
      </div>
      <div className="agent-content">
        {loading ? (
          <div className="agent-loading"><span className="spinner" />正在加载会话...</div>
        ) : messages.length === 0 ? (
          <div className="agent-empty">
            <div className="agent-avatar"><Sparkles size={16} /></div>
            <strong>GPT-5.4 副导演已就绪</strong>
            <p>可以讨论当前项目、页面和生产集。Agent 会加载启用的 Skills，但不会虚构尚未执行的生产操作。</p>
            <button onClick={() => void sendMessage("请总结当前上下文，并给出三个最值得优先处理的事项。")}>总结当前上下文</button>
            <button onClick={() => void sendMessage("请根据当前项目给出下一步制作建议，不要声称已经执行。")}>建议下一步</button>
          </div>
        ) : (
          <div className="agent-messages">
            {messages.map((item) => (
              <article className={`agent-message ${item.role}`} key={item.id}>
                <header>{item.role === "user" ? "导演" : "副导演"}</header>
                <p>{item.content}</p>
                {item.model && <small>{item.model}</small>}
              </article>
            ))}
            {sending && (
              <div className="agent-thinking"><span className="spinner" />GPT-5.4 正在思考...</div>
            )}
          </div>
        )}
        {error && <p className="agent-error">{error}</p>}
        <div ref={scrollAnchor} />
      </div>
      <form className="composer" onSubmit={submitMessage}>
        <div className="composer-context">
          <span>项目级对话</span>
          <span>GPT-5.4</span>
        </div>
        <textarea
          value={message}
          onChange={(event) => setMessage(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          placeholder="向副导演提问…"
          disabled={loading || sending}
        />
        <div>
          <small>Shift + Enter 换行</small>
          <button className="send-button" disabled={!message.trim() || loading || sending}>
            <SendHorizontal size={14} />
            发送
          </button>
        </div>
      </form>
    </aside>
  );
}
