import { useEffect, useRef, useState } from "react";
import {
  Link,
  NavLink,
  Outlet,
  useLocation,
  useNavigate,
  useParams,
} from "react-router-dom";
import {
  ArrowRight,
  Bell,
  BookOpenText,
  Bot,
  Boxes,
  CheckSquare2,
  ChevronDown,
  Clapperboard,
  Film,
  Gauge,
  Images,
  LayoutDashboard,
  Menu,
  Search,
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
  const projectSwitcherRef = useRef<HTMLDivElement>(null);
  const [projectMenuOpen, setProjectMenuOpen] = useState(false);
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
    if (!projectMenuOpen) return;

    const closeProjectMenu = (event: PointerEvent) => {
      if (!projectSwitcherRef.current?.contains(event.target as Node)) {
        setProjectMenuOpen(false);
      }
    };
    document.addEventListener("pointerdown", closeProjectMenu);
    return () => document.removeEventListener("pointerdown", closeProjectMenu);
  }, [projectMenuOpen]);
  const [agentOpen, setAgentOpen] = useState(
    () => window.matchMedia("(min-width: 1280px)").matches,
  );
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
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
    setProjectMenuOpen(false);
    navigate(`/projects/${nextProject.id}/overview`, {
      state: { projectName: nextProject.name },
    });
  };

  return (
    <div className={`app-shell ${agentOpen ? "" : "agent-closed"}`}>
      <header className="topbar">
        <button
          className="icon-button mobile-only"
          onClick={() => setMobileNavOpen(true)}
          aria-label="打开导航"
        >
          <Menu size={18} />
        </button>
        <div className="project-switcher-wrap" ref={projectSwitcherRef}>
          <button
            className="project-switcher"
            onClick={() => setProjectMenuOpen((open) => !open)}
            aria-expanded={projectMenuOpen}
            aria-haspopup="menu"
          >
            <span className="brand-mark">A</span>
            <strong>ALEX</strong>
            <span className="divider" />
            <span className="project-switcher-name">{projectName}</span>
            <ChevronDown size={14} />
          </button>
          {projectMenuOpen && (
            <div className="project-switcher-menu" role="menu">
              <button
                className="project-switcher-home"
                role="menuitem"
                onClick={() => navigate("/")}
              >
                <LayoutDashboard size={15} />
                返回项目中心
              </button>
              <span className="project-switcher-label">切换项目</span>
              {projects.map((availableProject) => (
                <button
                  className={availableProject.id === projectId ? "active" : ""}
                  key={availableProject.id}
                  role="menuitem"
                  onClick={() => switchProject(availableProject)}
                >
                  <span className="project-menu-mark">
                    {availableProject.name.slice(0, 1)}
                  </span>
                  <span>{availableProject.name}</span>
                  {availableProject.id === projectId && <i>当前</i>}
                </button>
              ))}
            </div>
          )}
        </div>
        <div className="breadcrumb">
          <span>制作</span>
          <b>/</b>
          <strong>{active?.label ?? "驾驶舱"}</strong>
        </div>
        <div className="topbar-actions">
          <label className="production-context">
            <span>当前生产集</span>
            <select
              value={currentEpisode}
              onChange={(event) => changeEpisode(event.target.value)}
            >
              {productionEpisodes.map((episode) => (
                <option value={episode.id} key={episode.id}>
                  {episode.code} · {episode.title}
                </option>
              ))}
            </select>
            <ChevronDown size={13} />
          </label>
          <span className="version-chip">{project.version}</span>
          <button className="search-button">
            <Search size={15} />
            <span>搜索或执行命令</span>
            <kbd>⌘ K</kbd>
          </button>
          <button className="icon-button notification" aria-label="任务通知">
            <Bell size={17} />
            <i>3</i>
          </button>
          <button
            className="icon-button"
            aria-label="全局设置"
            onClick={() => navigate("/settings/services")}
          >
            <Settings size={17} />
          </button>
          <button
            className={`agent-toggle ${agentOpen ? "active" : ""}`}
            onClick={() => setAgentOpen(!agentOpen)}
          >
            <Bot size={17} />
            Agent
          </button>
        </div>
      </header>
      <nav className={`side-nav ${mobileNavOpen ? "mobile-open" : ""}`}>
        <div className="mobile-nav-title">
          <strong>制作导航</strong>
          <button
            className="icon-button"
            onClick={() => setMobileNavOpen(false)}
          >
            <X size={18} />
          </button>
        </div>
        <div className="nav-section-label">创作流程</div>
        {scopedNavigation.map(({ label, icon: Icon, to }) => (
          <NavLink
            key={to}
            to={to}
            onClick={() => setMobileNavOpen(false)}
            className={({ isActive }) =>
              isActive ? "nav-item active" : "nav-item"
            }
          >
            <Icon size={18} />
            <span>{label}</span>
            {label === "资产圣经" && <i className="nav-count">2</i>}
          </NavLink>
        ))}
        <div className="nav-footer">
          <span className="service-dot" />
          服务正常<span>4 / 4</span>
        </div>
      </nav>
      <main className="main-canvas">
        {workflow && (
          <WorkflowToolbar
            workflow={workflow}
            episode={currentEpisodeData}
            currentEpisode={currentEpisode}
            onEpisodeChange={changeEpisode}
            onOpenQueue={() => setQueueOpen(true)}
          />
        )}
        <Outlet />
      </main>
      {agentOpen && (
        <AgentPanel
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
        <span className="pulse-dot" />
        <strong>2 个任务运行中</strong>
        <span>E01 视频 11/18</span>
        <span>E02 首帧 6/16</span>
        <button>展开活动</button>
      </div>
    </div>
  );
}

function WorkflowToolbar({
  workflow,
  episode,
  currentEpisode,
  onEpisodeChange,
  onOpenQueue,
}: {
  workflow: Workflow;
  episode: (typeof productionEpisodes)[number];
  currentEpisode: string;
  onEpisodeChange: (episodeId: string) => void;
  onOpenQueue: () => void;
}) {
  return (
    <div className="workflow-toolbar">
      <div className="workflow-context">
        <span>{workflow.label}</span>
        <label>
          <strong>{episode.code}</strong>
          <select
            aria-label="工作区生产集"
            value={currentEpisode}
            onChange={(event) => onEpisodeChange(event.target.value)}
          >
            {productionEpisodes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.code} · {item.title}
              </option>
            ))}
          </select>
          <ChevronDown size={12} />
        </label>
      </div>
      {workflow.tabs.length > 0 && (
        <nav className="workflow-tabs">
          {workflow.tabs.map((tab) => (
            <NavLink
              className={({ isActive }) => (isActive ? "active" : "")}
              to={tab.to}
              key={tab.to}
            >
              {tab.label}
            </NavLink>
          ))}
        </nav>
      )}
      <div className="workflow-actions">
        {workflow.queue && (
          <button className="queue-button" onClick={onOpenQueue}>
            <CheckSquare2 size={14} />
            {workflow.queue}
          </button>
        )}
        {workflow.next && (
          <Link className="workflow-next" to={workflow.next.to}>
            {workflow.next.label}
            <ArrowRight size={14} />
          </Link>
        )}
      </div>
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
  projectName,
  page,
  episode,
  onClose,
}: {
  projectName: string;
  page: string;
  episode: string;
  onClose: () => void;
}) {
  const [tab, setTab] = useState("副驾驶");
  const [message, setMessage] = useState("");
  return (
    <aside className="agent-panel">
      <header className="agent-header">
        <div>
          <Bot size={18} />
          <strong>Agent 副驾驶</strong>
          <span className="online-dot" />
        </div>
        <button className="icon-button" onClick={onClose}>
          <X size={17} />
        </button>
      </header>
      <div className="agent-tabs">
        {["副驾驶", "计划", "待确认", "结果"].map((item) => (
          <button
            key={item}
            className={tab === item ? "active" : ""}
            onClick={() => setTab(item)}
          >
            {item}
            {item === "待确认" && <i>2</i>}
          </button>
        ))}
      </div>
      <div className="context-block">
        <span className="eyebrow">当前上下文</span>
        <div className="context-tags">
          <span>项目：{projectName}</span>
          <span>页面：{page}</span>
          <span>生产集：{episode}</span>
          <span>版本：v4</span>
        </div>
      </div>
      <div className="agent-content">
        <div className="agent-intro">
          <div className="agent-avatar">
            <Sparkles size={16} />
          </div>
          <div>
            <strong>今天优先处理 3 件事</strong>
            <p>根据阻断、依赖和正在运行的任务整理。</p>
          </div>
        </div>
        <button className="suggestion">
          <span>
            <b>1</b>
            <strong>压缩 E01 第 3 场对白</strong>
          </span>
          <p>当前超出目标时长 3.8 秒，只生成修改提议。</p>
          <small>影响 2 个 DialogueBlock · 低风险</small>
        </button>
        <button className="suggestion">
          <span>
            <b>2</b>
            <strong>审阅林墨标准参考图</strong>
          </span>
          <p>此决定阻断后续 12 个镜头的参考计划。</p>
          <small>需要导演确认</small>
        </button>
      </div>
      <div className="composer">
        <div className="composer-context">
          <span>项目级指令</span>
        </div>
        <textarea
          value={message}
          onChange={(event) => setMessage(event.target.value)}
          placeholder="描述目标，或输入 / 查看命令…"
        />
        <div>
          <button className="icon-button" aria-label="添加附件">
            ＋
          </button>
          <button className="send-button" disabled={!message.trim()}>
            发送
          </button>
        </div>
      </div>
    </aside>
  );
}
