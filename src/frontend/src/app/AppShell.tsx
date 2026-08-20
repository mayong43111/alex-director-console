import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  Badge,
  Button,
  Dropdown,
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
  AudioLines,
  Bell,
  BookOpenText,
  Bot,
  Boxes,
  ChevronDown,
  Clapperboard,
  Film,
  Gauge,
  LayoutDashboard,
  RotateCcw,
  Search,
  SendHorizontal,
  SlidersHorizontal,
  Sparkles,
  X,
} from "lucide-react";
import { project } from "../data/mockData";
import {
  getProject,
  listProductionEpisodes,
  listProjects,
  type ProjectRecord,
  type ProductionEpisodeRecord,
} from "../api/projects";
import {
  getCopilotConversation,
  resetCopilotConversation,
  sendCopilotMessage,
  type CopilotMessage,
} from "../api/copilot";
import {
  AppLayout,
  StandardWorkspaceLayout,
  TitledWorkspaceLayout,
} from "../layouts";

const navigation = [
  { label: "设定", icon: SlidersHorizontal, to: "settings" },
  { label: "故事", icon: BookOpenText, to: "story/source" },
  { label: "剧本", icon: Film, to: "script" },
  { label: "资产", icon: Boxes, to: "assets/characters" },
  { label: "音频素材", icon: AudioLines, to: "assets/audio" },
  {
    label: "分镜",
    icon: Clapperboard,
    to: "storyboard",
  },
  { label: "生产", icon: Gauge, to: "production" },
  { label: "审阅", icon: Sparkles, to: "review" },
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
      next: { label: "导入原文资料", to: `${projectBase}/story/source` },
    };
  }
  if (pathname.includes("/story/")) {
    const next = pathname.includes("/story/source")
      ? { label: "分析素材图谱", to: `${projectBase}/story/material` }
      : { label: "建立改编方案", to: `${projectBase}/script/adaptation` };
    return {
      label: "故事",
      tabs: [
        { label: "原文资料", to: `${projectBase}/story/source` },
        { label: "素材图谱", to: `${projectBase}/story/material` },
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
        { label: "音频", to: `${projectBase}/assets/audio` },
      ],
    };
  }
  if (pathname.endsWith("/script") || pathname.includes("/script/")) {
    return {
      label: "剧本",
      tabs: [
        { label: "改编方案", to: `${projectBase}/script/adaptation` },
        ...(episodeId ? [{ label: "正式剧本", to: `${projectBase}/script/episodes/${episodeId}` }] : []),
      ],
    };
  }
  if (pathname.includes("/storyboard")) {
    return {
      label: "分镜",
      tabs: [],
      next: { label: "查看生产运行", to: `${projectBase}/production/episodes/${episodeId}` },
    };
  }
  if (pathname.endsWith("/production") || pathname.includes("/production/")) {
    return {
      label: "生产",
      tabs: [],
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
  const [productionEpisodes, setProductionEpisodes] = useState<ProductionEpisodeRecord[]>([]);
  const [selectedEpisode, setSelectedEpisode] = useState("");
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
    const syncProductionRunEpisode = (event: Event) => {
      const episodeId = (event as CustomEvent<{ productionEpisodeId: string }>).detail.productionEpisodeId;
      setSelectedEpisode(episodeId);
    };
    window.addEventListener("alex:production-run-episode", syncProductionRunEpisode);
    return () => window.removeEventListener("alex:production-run-episode", syncProductionRunEpisode);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    const loadEpisodes = () => {
      listProductionEpisodes(projectId, controller.signal)
        .then((items) => {
          setProductionEpisodes(items);
          setSelectedEpisode((current) =>
            items.some((item) => item.id === current) ? current : (items[0]?.id ?? ""));
        })
        .catch((error: unknown) => {
          if (error instanceof DOMException && error.name === "AbortError") return;
          console.warn("生产剧集列表加载失败", error);
        });
    };
    loadEpisodes();
    window.addEventListener("alex:production-episodes-updated", loadEpisodes);
    return () => {
      controller.abort();
      window.removeEventListener("alex:production-episodes-updated", loadEpisodes);
    };
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
  const [isAgentOverlay, setIsAgentOverlay] = useState(
    () => window.matchMedia("(max-width: 1279px)").matches,
  );
  const [agentOpen, setAgentOpen] = useState(
    () => window.matchMedia("(min-width: 1280px)").matches,
  );
  const updateAgentOpen = (open: boolean) => {
    setAgentOpen(open);
  };
  useEffect(() => {
    const mobileQuery = window.matchMedia("(max-width: 1279px)");
    const syncAgentVisibility = (event: MediaQueryListEvent | MediaQueryList) => {
      setIsAgentOverlay(event.matches);
      setAgentOpen(!event.matches);
    };
    syncAgentVisibility(mobileQuery);
    mobileQuery.addEventListener("change", syncAgentVisibility);
    return () => mobileQuery.removeEventListener("change", syncAgentVisibility);
  }, []);
  const [queueOpen, setQueueOpen] = useState(false);
  const routeEpisode = location.pathname.match(/(?:script|storyboard|production|review)\/episodes\/([^/]+)/)?.[1];
  const currentEpisode = routeEpisode ?? selectedEpisode;
  const currentEpisodeData =
    productionEpisodes.find((episode) => episode.id === currentEpisode) ?? productionEpisodes[0];
  const routingEpisode = currentEpisode;
  const projectBase = `/projects/${projectId}`;
  const workflow = getWorkflow(location.pathname, projectBase, routingEpisode);
  const scopedNavigation = navigation.map((item) => ({
    ...item,
    to: item.to.replace("production-e01", routingEpisode),
  }));
  const primaryNavigation = scopedNavigation.filter((item) => item.to !== "settings");
  const settingsNavigation = scopedNavigation.find((item) => item.to === "settings");
  const activeRoot = location.pathname.slice(projectBase.length + 1).split("/")[0];
  const active = navigation.find((item) => item.to.split("/")[0] === activeRoot);

  const switchProject = (nextProject: ProjectRecord) => {
    setProjectName(nextProject.name);
    navigate(`/projects/${nextProject.id}/settings`, {
      state: { projectName: nextProject.name },
    });
  };

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
  const workspaceTitle = workflow?.label ?? active?.label ?? "驾驶舱";

  return (
    <AppLayout
      agentOpen={agentOpen}
      headerStart={(
        <>
          <button className="director-brand" onClick={() => navigate("/")} aria-label="返回项目中心">
            <span className="brand-mark">A</span>
            <span>Alex 导演台</span>
          </button>
          <span className="director-header-divider" />
        <Dropdown
          menu={{ items: projectMenuItems, onClick: handleProjectMenuClick }}
          trigger={["click"]}
        >
          <Button type="text" className="project-switcher">
            <span className="project-menu-mark"><Clapperboard size={14} /></span>
            <span className="project-switcher-name">{projectName}</span>
            <ChevronDown size={14} />
          </Button>
        </Dropdown>
        </>
      )}
      headerActions={(
        <>
          <Button className="command-search" icon={<Search size={15} />}>
            搜索或执行命令 <kbd>⌘ K</kbd>
          </Button>
          <Badge className="header-notifications" count={3} size="small">
            <Tooltip title="任务通知">
              <Button type="text" icon={<Bell size={17} />} aria-label="任务通知" />
            </Tooltip>
          </Badge>
          <Tooltip title={agentOpen ? "收起副导演" : "展开副导演"}>
            <Button
              className="header-agent-button"
              type={agentOpen ? "primary" : "default"}
              icon={<Bot size={16} />}
              aria-label="副导演"
              aria-expanded={agentOpen}
              onClick={() => updateAgentOpen(!agentOpen)}
            />
          </Tooltip>
        </>
      )}
    >
      <StandardWorkspaceLayout
        agentOverlay={isAgentOverlay}
        onCloseAgent={() => updateAgentOpen(false)}
        navigation={(
          <>
          <nav className="director-rail-nav">
            {primaryNavigation.map(({ label, icon: Icon, to }) => {
              const target = `${projectBase}/${to}`;
              const isActive = to.split("/")[0] === activeRoot;
              return (
                <Tooltip title={label} placement="right" key={target}>
                  <Link
                    to={target}
                    className={`director-rail-link ${isActive ? "active" : ""}`}
                    aria-label={label}
                    aria-current={isActive ? "page" : undefined}
                  >
                    <Icon size={19} strokeWidth={1.8} />
                    <span>{label}</span>
                  </Link>
                </Tooltip>
              );
            })}
          </nav>
          <div className="director-rail-footer">
            {settingsNavigation && (
              <Tooltip title="项目设定" placement="right">
                <Link
                  to={`${projectBase}/${settingsNavigation.to}`}
                  className={`director-rail-link ${activeRoot === "settings" ? "active" : ""}`}
                  aria-label="项目设定"
                  aria-current={activeRoot === "settings" ? "page" : undefined}
                >
                  <settingsNavigation.icon size={19} strokeWidth={1.8} />
                  <span>项目设定</span>
                </Link>
              </Tooltip>
            )}
            <Tooltip title="服务正常 · 4 / 4" placement="right">
              <div className="director-rail-status" aria-label="服务正常，4 / 4">
                <Badge status="success" />
              </div>
            </Tooltip>
          </div>
          </>
        )}
        agent={agentOpen ? (
          <AgentPanel
            key={projectId}
            projectId={projectId}
            projectName={projectName}
            page={active?.label ?? "驾驶舱"}
            episode={currentEpisodeData
              ? `E${String(currentEpisodeData.episodeNumber).padStart(2, "0")}`
              : "未创建"}
            onClose={() => updateAgentOpen(false)}
          />
        ) : undefined}
      >
        <TitledWorkspaceLayout
          title={workspaceTitle}
          projectName={projectName}
          projectHome={`${projectBase}/settings`}
          pathname={location.pathname}
          tabs={workflow?.tabs}
          contentRef={mainCanvasRef}
          status={(
            <>
              <span className="online-dot" />
              <span>工作区已同步</span>
            </>
          )}
        >
            <Outlet />
        </TitledWorkspaceLayout>
        {queueOpen && workflow?.queue && (
          <BatchReviewDialog
            title={workflow.queue}
            stage={workflow.label}
            onClose={() => setQueueOpen(false)}
          />
        )}
      </StandardWorkspaceLayout>
    </AppLayout>
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
  onClose?: () => void;
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
      setError(sendError instanceof Error ? sendError.message : "AI 副导演暂时无法回复。");
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
          {onClose && (
            <button className="icon-button" onClick={onClose} aria-label="关闭副导演">
              <X size={17} />
            </button>
          )}
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
            <strong>AI 副导演已就绪</strong>
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
              <div className="agent-thinking"><span className="spinner" />AI 副导演正在思考...</div>
            )}
          </div>
        )}
        {error && <p className="agent-error">{error}</p>}
        <div ref={scrollAnchor} />
      </div>
      <form className="composer" onSubmit={submitMessage}>
        <div className="composer-context">
          <span>项目级对话</span>
          <span>AI 副导演</span>
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
