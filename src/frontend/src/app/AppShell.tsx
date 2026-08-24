import { useEffect, useRef, useState } from "react";
import { Button, Dropdown, Tag, Tooltip, type MenuProps } from "antd";
import {
  Outlet,
  useLocation,
  useNavigate,
  useParams,
} from "react-router-dom";
import {
  Bot,
  ChevronDown,
  Clapperboard,
  LayoutDashboard,
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
import { assistantDirectorAgent } from "../api/sessions";
import { AssistantDirectorPanel } from "../components/AssistantDirectorPanel";
import { ProjectRailNavigation } from "../navigation/ProjectRailNavigation";
import { projectNavigation } from "../navigation/projectNavigation";
import {
  AppLayout,
  StandardWorkspaceLayout,
  TitledWorkspaceLayout,
} from "../layouts";

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
      next: { label: "导入原文资料", to: `${projectBase}/story` },
    };
  }
  if (pathname === `${projectBase}/story` || pathname.startsWith(`${projectBase}/story/`)) {
    const [sourceId, view] = pathname
      .slice(`${projectBase}/story`.length)
      .split("/")
      .filter(Boolean);
    const next = sourceId && view === "source"
      ? { label: "分析原文结构", to: `${projectBase}/story/${sourceId}/material` }
      : sourceId && view === "material"
        ? { label: "建立改编方案", to: `${projectBase}/script/${sourceId}/adaptation` }
        : undefined;
    return {
      label: view === "material" ? "原文分析" : view === "source" ? "原文资料" : "故事",
      tabs: [],
      next,
    };
  }
  if (pathname.includes("/assets/")) {
    return {
      label: "项目共享资产",
      tabs: [],
    };
  }
  if (pathname.endsWith("/script") || pathname.includes("/script/")) {
    const [, view] = pathname
      .slice(`${projectBase}/script`.length)
      .split("/")
      .filter(Boolean);
    return {
      label: view === "adaptation" ? "改编方案" : view === "production" ? "正式剧本" : "剧本",
      tabs: [],
    };
  }
  if (pathname.includes("/storyboard")) {
    return {
      label: "分镜",
      tabs: [],
      next: {
        label: "查看生产运行",
        to: `${projectBase}/production/episodes/${episodeId}`,
      },
    };
  }
  if (pathname.endsWith("/production") || pathname.includes("/production/")) {
    return {
      label: "生产",
      tabs: [],
      next: {
        label: "进入成片审阅",
        to: `${projectBase}/review/episodes/${episodeId}`,
      },
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
  const [productionEpisodes, setProductionEpisodes] = useState<
    ProductionEpisodeRecord[]
  >([]);
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
        ? ((JSON.parse(cachedProject) as { name?: string }).name ??
            project.name)
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
        if (error instanceof DOMException && error.name === "AbortError")
          return;
        console.warn("项目信息加载失败", error);
      });
    return () => controller.abort();
  }, [projectId]);
  useEffect(() => {
    const syncProductionRunEpisode = (event: Event) => {
      const episodeId = (event as CustomEvent<{ productionEpisodeId: string }>)
        .detail.productionEpisodeId;
      setSelectedEpisode(episodeId);
    };
    window.addEventListener(
      "alex:production-run-episode",
      syncProductionRunEpisode,
    );
    return () =>
      window.removeEventListener(
        "alex:production-run-episode",
        syncProductionRunEpisode,
      );
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    const loadEpisodes = () => {
      listProductionEpisodes(projectId, controller.signal)
        .then((items) => {
          setProductionEpisodes(items);
          setSelectedEpisode((current) =>
            items.some((item) => item.id === current)
              ? current
              : (items[0]?.id ?? ""),
          );
        })
        .catch((error: unknown) => {
          if (error instanceof DOMException && error.name === "AbortError")
            return;
          console.warn("生产剧集列表加载失败", error);
        });
    };
    loadEpisodes();
    window.addEventListener("alex:production-episodes-updated", loadEpisodes);
    return () => {
      controller.abort();
      window.removeEventListener(
        "alex:production-episodes-updated",
        loadEpisodes,
      );
    };
  }, [projectId]);
  useEffect(() => {
    mainCanvasRef.current?.scrollTo({ top: 0, left: 0 });
  }, [location.pathname]);
  useEffect(() => {
    const controller = new AbortController();
    listProjects(controller.signal)
      .then(setProjects)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === "AbortError")
          return;
        console.warn("项目列表加载失败", error);
      });
    return () => controller.abort();
  }, []);
  useEffect(() => {
    const updateProject = (event: Event) => {
      const detail = (event as CustomEvent<{ projectId: string; name: string }>)
        .detail;
      if (detail.projectId !== projectId) return;
      setProjectName(detail.name);
      setProjects((current) =>
        current.map((item) =>
          item.id === detail.projectId ? { ...item, name: detail.name } : item,
        ),
      );
      const cacheKey = `alex-director-v2.project.${detail.projectId}`;
      const cached = sessionStorage.getItem(cacheKey);
      if (cached) {
        sessionStorage.setItem(
          cacheKey,
          JSON.stringify({ ...JSON.parse(cached), name: detail.name }),
        );
      }
    };
    window.addEventListener("alex:project-updated", updateProject);
    return () =>
      window.removeEventListener("alex:project-updated", updateProject);
  }, [projectId]);
  const [isAgentOverlay, setIsAgentOverlay] = useState(
    () => window.matchMedia("(max-width: 1023px)").matches,
  );
  const [agentOpen, setAgentOpen] = useState(
    () => window.matchMedia("(min-width: 1024px)").matches,
  );
  const updateAgentOpen = (open: boolean) => {
    setAgentOpen(open);
  };
  useEffect(() => {
    const mobileQuery = window.matchMedia("(max-width: 1023px)");
    const syncAgentVisibility = (
      event: MediaQueryListEvent | MediaQueryList,
    ) => {
      setIsAgentOverlay(event.matches);
      setAgentOpen(!event.matches);
    };
    syncAgentVisibility(mobileQuery);
    mobileQuery.addEventListener("change", syncAgentVisibility);
    return () => mobileQuery.removeEventListener("change", syncAgentVisibility);
  }, []);
  const [queueOpen, setQueueOpen] = useState(false);
  const routeEpisode = location.pathname.match(/\/script\/([^/]+)\/production\/?$/)?.[1]
    ?? location.pathname.match(
      /(?:script|storyboard|production|review)\/episodes\/([^/]+)/,
    )?.[1];
  const currentEpisode = routeEpisode ?? selectedEpisode;
  const currentEpisodeData =
    productionEpisodes.find((episode) => episode.id === currentEpisode) ??
    productionEpisodes[0];
  const routingEpisode = currentEpisode;
  const projectBase = `/projects/${projectId}`;
  const workflow = getWorkflow(location.pathname, projectBase, routingEpisode);
  const activeRoot = location.pathname
    .slice(projectBase.length + 1)
    .split("/")[0];
  const activeNavigationKey = activeRoot;
  const active = projectNavigation.find(
    (item) => item.key === activeNavigationKey,
  );

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
      headerStart={
        <>
          <button
            className="director-brand"
            onClick={() => navigate("/")}
            aria-label="返回项目中心"
          >
            <span className="brand-mark">A</span>
            <span>Alex 导演台</span>
          </button>
          <span className="director-header-divider" />
          <Dropdown
            menu={{ items: projectMenuItems, onClick: handleProjectMenuClick }}
            trigger={["click"]}
          >
            <Button type="text" className="project-switcher">
              <span className="project-menu-mark">
                <Clapperboard size={14} />
              </span>
              <span className="project-switcher-name">{projectName}</span>
              <ChevronDown size={14} />
            </Button>
          </Dropdown>
        </>
      }
      headerActions={
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
      }
    >
      <StandardWorkspaceLayout
        agentOverlay={isAgentOverlay}
        onCloseAgent={() => updateAgentOpen(false)}
        navigation={(
          <ProjectRailNavigation
            activeKey={activeNavigationKey}
            projectBase={projectBase}
            productionEpisodeId={routingEpisode}
          />
        )}
        agent={
          agentOpen ? (
            <AssistantDirectorPanel
              key={projectId}
              agent={assistantDirectorAgent}
              session={{
                scopeKey: `project:${projectId}:assistant-director`,
                projectId,
                title: `项目：${projectName}`,
                page: active?.label ?? "驾驶舱",
                episode: currentEpisodeData
                  ? `E${String(currentEpisodeData.episodeNumber).padStart(2, "0")}`
                  : "未创建",
              }}
              onClose={() => updateAgentOpen(false)}
            />
          ) : undefined
        }
      >
        <TitledWorkspaceLayout
          title={workspaceTitle}
          projectName={projectName}
          projectHome={`${projectBase}/settings`}
          pathname={location.pathname}
          tabs={workflow?.tabs}
          contentRef={mainCanvasRef}
          saveFormId={location.pathname.endsWith("/settings") ? "project-settings-form" : undefined}
          compact
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
          <button className="secondary-button" onClick={onClose}>
            取消
          </button>
          <button
            className="primary-button"
            disabled={!selected.length}
            onClick={onClose}
          >
            确认批量处理
          </button>
        </footer>
      </section>
    </div>
  );
}
