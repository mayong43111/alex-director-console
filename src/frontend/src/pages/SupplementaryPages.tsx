import {
  useDeferredValue,
  useEffect,
  useState,
  type FormEvent,
} from "react";
import { Link, NavLink, useNavigate, useParams } from "react-router-dom";
import {
  createProject,
  listProjects,
  type ProjectRecord,
} from "../api/projects";
import {
  getComfyUiConfiguration,
  getFoundryConfiguration,
  testComfyUiConnection,
  testFoundryConnection,
  updateComfyUiConfiguration,
  updateFoundryConfiguration,
  type ComfyUiConfiguration,
  type FoundryConfiguration,
} from "../api/systemConfiguration";
import {
  listSkills,
  updateSkill,
  type SkillRecord,
} from "../api/skills";
import {
  ArrowLeft,
  Bot,
  Check,
  ChevronDown,
  CircleAlert,
  Cloud,
  Code2,
  Grid2X2,
  List,
  MoreHorizontal,
  Pause,
  Play,
  Plus,
  RotateCcw,
  Search,
  Server,
  Settings,
  Sparkles,
  Volume2,
} from "lucide-react";

const productionEpisodes = [
  {
    code: "E01",
    title: "失控的早晨",
    duration: 98.4,
    target: "90–110s",
    status: "通过",
  },
  {
    code: "E02",
    title: "追查与反转",
    duration: 113.8,
    target: "90–110s",
    status: "超载",
  },
  {
    code: "E03",
    title: "真相回收",
    duration: 87.2,
    target: "90–110s",
    status: "偏短",
  },
];

function PageTitle({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow: string;
  title: string;
  description: string;
  action?: React.ReactNode;
}) {
  return (
    <header className="page-header">
      <div>
        <span className="eyebrow">{eyebrow}</span>
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
      {action}
    </header>
  );
}

export function ProjectCenterPage() {
  const navigate = useNavigate();
  const [view, setView] = useState<"list" | "grid">("list");
  const [draftName, setDraftName] = useState("");
  const [draftDescription, setDraftDescription] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [projects, setProjects] = useState<ProjectRecord[]>([]);
  const [projectsLoading, setProjectsLoading] = useState(true);
  const [projectsError, setProjectsError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    listProjects(controller.signal)
      .then(setProjects)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        setProjectsError(
          error instanceof Error ? error.message : "项目列表加载失败。",
        );
      })
      .finally(() => {
        if (!controller.signal.aborted) setProjectsLoading(false);
      });
    return () => controller.abort();
  }, []);

  async function submitProject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draftName.trim() || creating) return;

    setCreating(true);
    setCreateError(null);
    try {
      const project = await createProject({
        name: draftName,
        description: draftDescription,
      });
      sessionStorage.setItem(
        `alex-director-v2.project.${project.id}`,
        JSON.stringify(project),
      );
      navigate(`/projects/${project.id}/settings`, {
        state: { projectName: project.name },
      });
    } catch (error) {
      setCreateError(
        error instanceof Error ? error.message : "项目创建失败，请稍后重试。",
      );
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className="project-center">
      <header className="center-topbar">
        <Link className="center-brand" to="/">
          <span>A</span>
          <strong>ALEX DIRECTOR</strong>
        </Link>
        <div>
          <span className="service-dot" />
          服务在线
          <button
            className="icon-button"
            aria-label="系统设置"
            onClick={() => navigate("/settings/services")}
          >
            <Settings size={17} />
          </button>
        </div>
      </header>
      <main className="center-layout">
        <section className="project-browser">
          <PageTitle
            eyebrow="工作空间"
            title="项目"
            description="从创意设定到分集交付的全部制作空间"
            action={
              <button
                className="primary-button"
                onClick={() =>
                  document.getElementById("new-project-name")?.focus()
                }
              >
                <Plus size={14} />
                创建项目
              </button>
            }
          />
          <div className="workspace-toolbar">
            <div className="project-filters">
              <label>
                <Search size={14} />
                <input placeholder="搜索项目" />
              </label>
              <button>
                片型
                <ChevronDown size={13} />
              </button>
              <button>
                状态
                <ChevronDown size={13} />
              </button>
            </div>
            <div className="segmented">
              <button
                className={view === "list" ? "active" : ""}
                onClick={() => setView("list")}
                aria-label="表格视图"
              >
                <List size={14} />
              </button>
              <button
                className={view === "grid" ? "active" : ""}
                onClick={() => setView("grid")}
                aria-label="网格视图"
              >
                <Grid2X2 size={14} />
              </button>
            </div>
          </div>
          <div className={`project-collection ${view}`}>
            {projectsLoading && <p className="project-list-state">正在加载项目…</p>}
            {projectsError && (
              <p className="project-list-state error" role="alert">
                {projectsError}
              </p>
            )}
            {!projectsLoading && !projectsError && projects.length === 0 && (
              <p className="project-list-state">还没有项目，请创建第一个制作空间。</p>
            )}
            {projects.map((project) => (
              <Link
                className="project-item"
                to={`/projects/${project.id}/settings`}
                key={project.id}
              >
                <span className="project-monogram">
                  {project.name.slice(0, 1)}
                </span>
                <span className="project-name">
                  <strong>{project.name}</strong>
                  <small>空项目</small>
                </span>
                <span className="project-stage">等待项目设定</span>
                <span className="state-label draft">草稿</span>
                <span className="project-updated">
                  {new Date(project.updatedAtUtc).toLocaleDateString("zh-CN")}
                </span>
                <MoreHorizontal size={16} />
              </Link>
            ))}
          </div>
        </section>
        <form className="create-project-panel" onSubmit={submitProject}>
          <span className="eyebrow">创建项目</span>
          <h2>建立制作空间</h2>
          <div className="creation-steps">
            <span className="active">
              <b>1</b>基本信息
            </span>
            <span>
              <b>2</b>时长与交付
            </span>
            <span>
              <b>3</b>Agent 设定草案
            </span>
          </div>
          <label htmlFor="new-project-name">
            项目名称
            <input
              id="new-project-name"
              value={draftName}
              maxLength={200}
              onChange={(event) => setDraftName(event.target.value)}
              placeholder="输入项目名称"
              autoComplete="off"
            />
          </label>
          <label htmlFor="new-project-description">
            项目描述
            <textarea
              id="new-project-description"
              value={draftDescription}
              maxLength={4000}
              onChange={(event) => setDraftDescription(event.target.value)}
              placeholder="故事类型、制作目标或项目范围（可选）"
              rows={4}
            />
          </label>
          {createError && (
            <p className="create-project-error" role="alert">
              <CircleAlert size={13} />
              {createError}
            </p>
          )}
          <button
            className="primary-button wide"
            type="submit"
            disabled={!draftName.trim() || creating}
          >
            {creating ? "正在创建…" : "创建并进入项目"}
          </button>
          <div className="agent-draft">
            <Sparkles size={15} />
            <p>
              <strong>先创建空项目</strong>
              <span>项目设定、生产集和资产将在进入工作区后逐步建立。</span>
            </p>
          </div>
        </form>
      </main>
    </div>
  );
}

export function DemoPage({ title }: { title: string }) {
  const { projectId = "" } = useParams();
  return (
    <div className="page full-height-page">
      <div className="source-empty-state development-empty-state" role="status">
        <CircleAlert size={24} />
        <span className="eyebrow">DEMO / 尚未接入真实数据</span>
        <h1>{title}</h1>
        <p>该阶段不属于当前一级实现。页面已停止展示演示数据，完成正式接口和版本语义前不可操作。</p>
        <Link className="primary-button" to={`/projects/${projectId}/story/source`}>
          返回当前制作主线
        </Link>
      </div>
    </div>
  );
}

export function ChaptersPage() {
  const [view, setView] = useState<"board" | "timeline">("board");
  const [selected, setSelected] = useState(2);
  const chapters = [
    {
      title: "建立目标",
      duration: 22,
      beats: 4,
      assets: "林墨 / 天桥食堂",
      intensity: 1,
    },
    {
      title: "阻碍升级",
      duration: 28,
      beats: 5,
      assets: "红色文件袋",
      intensity: 3,
    },
    {
      title: "真相反转",
      duration: 18,
      beats: 3,
      assets: "周岚 / 旧照片",
      intensity: 5,
    },
    {
      title: "结尾回收",
      duration: 25,
      beats: 4,
      assets: "林墨 / 招聘启事",
      intensity: 4,
    },
  ];

  return (
    <div className="page">
      <PageTitle
        eyebrow="故事结构 / v3 draft"
        title="章节与爆点"
        description="用章节、节拍和爽点强度控制每个生产集的叙事节奏"
        action={
          <div className="button-group">
            <label className="select-control">
              <span>生产集</span>
              <select defaultValue="E01">
                <option>E01 · 失控的早晨</option>
                <option>E02 · 追查与反转</option>
              </select>
              <ChevronDown size={13} />
            </label>
            <button className="primary-button">创建版本</button>
          </div>
        }
      />
      <div className="workspace-toolbar">
        <div className="segmented">
          <button
            className={view === "board" ? "active" : ""}
            onClick={() => setView("board")}
          >
            章节板
          </button>
          <button
            className={view === "timeline" ? "active" : ""}
            onClick={() => setView("timeline")}
          >
            爆点时间线
          </button>
        </div>
        <span className="coverage">
          总时长 <strong>93s</strong> · 16 beats
        </span>
      </div>
      {view === "board" ? (
        <div className="chapter-board">
          {chapters.map((chapter, index) => (
            <button
              className={`chapter-card ${selected === index ? "selected" : ""}`}
              onClick={() => setSelected(index)}
              key={chapter.title}
            >
              <header>
                <span>{String(index + 1).padStart(2, "0")}</span>
                <i className={`intensity i-${chapter.intensity}`} />
              </header>
              <h2>{chapter.title}</h2>
              <p>{chapter.assets}</p>
              <div>
                <span>
                  <b>{chapter.duration}s</b> 时长
                </span>
                <span>
                  <b>{chapter.beats}</b> beats
                </span>
              </div>
              <footer>
                {index === 2 ? (
                  <>
                    <Sparkles size={13} />
                    核心反转
                  </>
                ) : index === 3 ? (
                  "待确认结尾力度"
                ) : (
                  "结构检查通过"
                )}
              </footer>
            </button>
          ))}
        </div>
      ) : (
        <div className="payoff-timeline">
          <div className="timeline-axis">
            <span>0s</span>
            <i />
            <span>93s</span>
          </div>
          {chapters.map((chapter, index) => (
            <div
              className="timeline-chapter"
              style={{ width: `${(chapter.duration / 93) * 100}%` }}
              key={chapter.title}
            >
              <b>章 {index + 1}</b>
              <span>{chapter.title}</span>
              {index > 0 && (
                <em className={`payoff p-${chapter.intensity}`}>
                  ◆ {index === 2 ? "反转" : index === 3 ? "胜利" : "触发"}
                </em>
              )}
            </div>
          ))}
        </div>
      )}
      <section className="selected-chapter panel">
        <header className="panel-header">
          <h2>
            章 {selected + 1} · {chapters[selected].title}
          </h2>
          <button className="secondary-button">编辑章节</button>
        </header>
        <div className="chapter-detail">
          <div>
            <span className="eyebrow">叙事目标</span>
            <p>在有限时长内完成信息递进，让关键证据改变主角的行动方向。</p>
          </div>
          <div>
            <span className="eyebrow">Agent 结构检查</span>
            <p className={selected === 3 ? "warning-text" : "success-text"}>
              {selected === 3
                ? "爆点间隔 41 秒，建议提高结尾回收强度。"
                : "铺垫、触发和结果链条完整。"}
            </p>
          </div>
        </div>
      </section>
    </div>
  );
}

export function DurationPage() {
  const [episode, setEpisode] = useState(1);
  const active = productionEpisodes[episode];
  const scenes = [
    {
      code: "S01",
      name: "天桥外部",
      sound: 8.2,
      action: 7.4,
      pause: 2.4,
      total: 18,
    },
    {
      code: "S02",
      name: "食堂一楼",
      sound: 11.3,
      action: 8.7,
      pause: 4,
      total: 24,
    },
    {
      code: "S03",
      name: "食堂二楼",
      sound: 13.2,
      action: 5.8,
      pause: 2.8,
      total: 21.8,
    },
    {
      code: "S04",
      name: "照片揭示",
      sound: 9.5,
      action: 6.2,
      pause: 3.3,
      total: 19,
    },
  ];

  return (
    <div className="page">
      <PageTitle
        eyebrow="剧本 / 时长仪表"
        title="时长与节奏"
        description="按生产集、章节和场次追踪对白、动作、停顿与爆点分布"
        action={
          <div className="button-group">
            <label className="select-control">
              <span>生产集</span>
              <select
                value={episode}
                onChange={(event) => setEpisode(Number(event.target.value))}
              >
                {productionEpisodes.map((item, index) => (
                  <option value={index} key={item.code}>
                    {item.code} · {item.title}
                  </option>
                ))}
              </select>
              <ChevronDown size={13} />
            </label>
            <button className="primary-button">
              <Sparkles size={14} />
              生成压缩提议
            </button>
          </div>
        }
      />
      <div
        className={`duration-hero ${active.status === "通过" ? "pass" : "fail"}`}
      >
        <div>
          <span className="eyebrow">{active.code} 当前时长</span>
          <strong>{active.duration}s</strong>
          <small>目标 {active.target}</small>
        </div>
        <span className="duration-status">
          {active.status === "通过" ? (
            <Check size={18} />
          ) : (
            <CircleAlert size={18} />
          )}
          {active.status}
        </span>
        <div className="duration-track">
          <i
            style={{
              width: `${Math.min((active.duration / 120) * 100, 100)}%`,
            }}
          />
          <b style={{ left: "75%" }} />
          <b style={{ left: "91.6%" }} />
        </div>
      </div>
      <div className="duration-layout">
        <section className="duration-scenes panel">
          <header className="panel-header">
            <h2>场次明细</h2>
            <span>声音 52.4s · 动作 42.1s · 停顿 19.3s</span>
          </header>
          <div className="duration-table-head">
            <span>场次</span>
            <span>声音</span>
            <span>动作</span>
            <span>停顿</span>
            <span>总计</span>
          </div>
          {scenes.map((scene, index) => (
            <button
              className={index === 2 ? "over-limit" : ""}
              key={scene.code}
            >
              <span>
                <strong>{scene.code}</strong>
                <small>{scene.name}</small>
              </span>
              <span>{scene.sound}s</span>
              <span>{scene.action}s</span>
              <span>{scene.pause}s</span>
              <span>
                {scene.total}s {index === 2 && <CircleAlert size={12} />}
              </span>
            </button>
          ))}
        </section>
        <section className="rhythm-panel panel">
          <header className="panel-header">
            <h2>时间分布</h2>
            <span>场次 / 爆点</span>
          </header>
          <div className="stacked-duration">
            <i className="sound" style={{ width: "46%" }} />
            <i className="action" style={{ width: "37%" }} />
            <i className="pause" style={{ width: "17%" }} />
          </div>
          <div className="duration-legend">
            <span>
              <i className="sound" />
              声音 46%
            </span>
            <span>
              <i className="action" />
              动作 37%
            </span>
            <span>
              <i className="pause" />
              停顿 17%
            </span>
          </div>
          <div className="beat-chart">
            {Array.from({ length: 18 }, (_, index) => (
              <i
                style={{ height: `${18 + ((index * 17) % 62)}%` }}
                className={index === 7 || index === 14 ? "payoff" : ""}
                key={index}
              />
            ))}
          </div>
          <div className="chart-axis">
            <span>0s</span>
            <span>◆ 反转 54s</span>
            <span>◆ 回收 88s</span>
            <span>114s</span>
          </div>
          <div className="agent-insight">
            <Bot size={16} />
            <p>
              <strong>优先压缩 S03</strong>
              <span>两句对白可减少约 2.6 秒，叙事证据不受影响。</span>
            </p>
          </div>
        </section>
      </div>
    </div>
  );
}

const dialogueRows = [
  {
    id: 1,
    scene: "S01",
    role: "林墨",
    type: "对白",
    text: "文件去哪儿了？",
    duration: "1.8s",
    status: "已完成",
  },
  {
    id: 2,
    scene: "S01",
    role: "旁白",
    type: "VO",
    text: "清晨的城市仍未完全醒来。",
    duration: "4.2s",
    status: "待制作",
  },
  {
    id: 3,
    scene: "S02",
    role: "周岚",
    type: "对白",
    text: "我没有拿。",
    duration: "1.4s",
    status: "需重录",
  },
  {
    id: 4,
    scene: "S03",
    role: "林墨",
    type: "对白",
    text: "你让我把文件带来。",
    duration: "2.6s",
    status: "已完成",
  },
  {
    id: 5,
    scene: "S03",
    role: "周岚",
    type: "对白",
    text: "三年前，也有人拿着同样的文件袋来过。",
    duration: "4.9s",
    status: "待制作",
  },
  {
    id: 6,
    scene: "S04",
    role: "旁白",
    type: "VO",
    text: "照片背后的日期解释了一切。",
    duration: "3.6s",
    status: "待制作",
  },
];

export function DialoguePage() {
  const [selected, setSelected] = useState<number[]>([2, 5, 6]);
  const [playing, setPlaying] = useState<number | null>(1);
  const toggle = (id: number) =>
    setSelected((current) =>
      current.includes(id)
        ? current.filter((item) => item !== id)
        : [...current, id],
    );

  return (
    <div className="page">
      <PageTitle
        eyebrow="剧本 / 台词本"
        title="台词与配音"
        description="42 条台词 · 18 条配音完成 · 文本版本与音频版本独立追踪"
        action={
          <button className="primary-button">
            <Volume2 size={14} />
            生成配音
          </button>
        }
      />
      <div className="workspace-toolbar">
        <div className="dialogue-filters">
          <button>
            角色
            <ChevronDown size={13} />
          </button>
          <button>
            集 / 场<ChevronDown size={13} />
          </button>
          <button>
            制作状态
            <ChevronDown size={13} />
          </button>
          <label>
            <Search size={14} />
            <input placeholder="搜索台词" />
          </label>
        </div>
        <span className="coverage">
          已选择 <strong>{selected.length}</strong> 条
        </span>
      </div>
      <div className="dialogue-layout">
        <section className="dialogue-table panel">
          <div className="dialogue-head">
            <span />
            <span>场次</span>
            <span>角色 / 类型</span>
            <span>台词</span>
            <span>时长</span>
            <span>状态</span>
            <span />
          </div>
          {dialogueRows.map((row) => (
            <div className="dialogue-row" key={row.id}>
              <label className="check-control">
                <input
                  type="checkbox"
                  checked={selected.includes(row.id)}
                  onChange={() => toggle(row.id)}
                />
                <i />
              </label>
              <strong>{row.scene}</strong>
              <span>
                <b>{row.role}</b>
                <small>{row.type}</small>
              </span>
              <p>{row.text}</p>
              <span className="mono">{row.duration}</span>
              <span
                className={`state-label ${row.status === "已完成" ? "ready" : row.status === "需重录" ? "blocked" : "waiting"}`}
              >
                {row.status}
              </span>
              <button
                className="icon-button"
                onClick={() => setPlaying(playing === row.id ? null : row.id)}
                aria-label={playing === row.id ? "暂停" : "播放"}
              >
                {playing === row.id ? <Pause size={14} /> : <Play size={14} />}
              </button>
              {playing === row.id && (
                <div className="audio-preview">
                  <button className="icon-button">
                    <Pause size={13} />
                  </button>
                  <div className="waveform">
                    {Array.from({ length: 38 }, (_, index) => (
                      <i
                        style={{ height: `${5 + ((index * 7) % 18)}px` }}
                        key={index}
                      />
                    ))}
                  </div>
                  <span>00:00.84 / {row.duration}</span>
                  <small>音频 v2</small>
                </div>
              )}
            </div>
          ))}
        </section>
        <aside className="voice-plan panel">
          <span className="eyebrow">Agent 配音计划</span>
          <h2>批量生成 {selected.length} 条</h2>
          <dl>
            <div>
              <dt>voice</dt>
              <dd>alloy</dd>
            </div>
            <div>
              <dt>speed</dt>
              <dd>1.0</dd>
            </div>
            <div>
              <dt>format</dt>
              <dd>wav / 48kHz</dd>
            </div>
            <div>
              <dt>预计调用</dt>
              <dd>{selected.length} 次</dd>
            </div>
          </dl>
          <button className="primary-button wide" disabled={!selected.length}>
            确认生成 {selected.length} 条
          </button>
          <button className="secondary-button wide">调整配音计划</button>
        </aside>
      </div>
    </div>
  );
}

export function ProductionRunPage() {
  const { runId } = useParams();
  const [paused, setPaused] = useState(false);
  const tasks = [
    ["S01-01", "succeeded", "succeeded", "succeeded", "waiting"],
    ["S01-02", "succeeded", "failed", "queued", "waiting"],
    ["S01-03", "succeeded", "succeeded", "running", "waiting"],
    ["S01-04", "succeeded", "succeeded", "queued", "waiting"],
    ["S02-01", "succeeded", "succeeded", "queued", "waiting"],
    ["S02-02", "succeeded", "failed", "blocked", "waiting"],
  ];

  return (
    <div className="page">
      <PageTitle
        eyebrow="生产 / E01 / 运行详情"
        title={`Run ${runId ?? "042"} · videos`}
        description="Script E01 v4 · Storyboard v3 · 创建于 28 分钟前"
        action={
          <div className="button-group">
            <button
              className="secondary-button"
              onClick={() => setPaused(!paused)}
            >
              {paused ? <Play size={14} /> : <Pause size={14} />}
              {paused ? "继续" : "暂停"}
            </button>
            <button className="secondary-button danger-button">停止运行</button>
          </div>
        }
      />
      <div className="run-stats">
        <div>
          <span className="eyebrow">成功</span>
          <strong>11</strong>
        </div>
        <div>
          <span className="eyebrow">运行</span>
          <strong className="running-text">1</strong>
        </div>
        <div>
          <span className="eyebrow">排队</span>
          <strong>4</strong>
        </div>
        <div>
          <span className="eyebrow">失败</span>
          <strong className="danger-text">2</strong>
        </div>
        <div className="run-progress">
          <span>总体进度 61%</span>
          <i>
            <b style={{ width: "61%" }} />
          </i>
        </div>
      </div>
      <section className="run-table panel">
        <div className="production-run-head">
          <span>镜头</span>
          <span>首帧</span>
          <span>配音</span>
          <span>视频</span>
          <span>组装</span>
          <span>尝试</span>
          <span />
        </div>
        {tasks.map((task, index) => (
          <div
            className={`production-run-row ${index === 2 ? "active" : ""}`}
            key={task[0]}
          >
            <strong>{task[0]}</strong>
            {task.slice(1).map((state, stateIndex) => (
              <span
                className={`task-state ${state}`}
                key={`${task[0]}-${stateIndex}`}
              >
                {state === "succeeded" ? (
                  <Check size={12} />
                ) : state === "failed" ? (
                  <CircleAlert size={12} />
                ) : state === "running" ? (
                  <span className="spinner" />
                ) : null}
                {state}
              </span>
            ))}
            <span className="mono">
              {index === 1 || index === 5 ? "2 / 3" : "1 / 3"}
            </span>
            <button className="icon-button">
              <MoreHorizontal size={15} />
            </button>
          </div>
        ))}
      </section>
      <div className="run-footer">
        <div>
          <span className="service-dot" />
          GPU online
        </div>
        <span>运行 00:28:14</span>
        <span>模型调用 34</span>
        <span>输出 29</span>
        <div className="run-actions">
          <button className="secondary-button">
            <RotateCcw size={14} />
            只重试失败项
          </button>
          <button className="primary-button">
            <Sparkles size={14} />
            诊断失败 2 项
          </button>
        </div>
      </div>
    </div>
  );
}

function GlobalSettingsShell({
  active,
  children,
}: {
  active: "services" | "skills";
  children: React.ReactNode;
}) {
  const returnTo = sessionStorage.getItem("alex-director-v2.lastProjectPath") ?? "/";

  return (
    <div className="global-settings">
      <header>
        <Link className="center-brand" to="/">
          <span>A</span>
          <strong>ALEX</strong>
        </Link>
        <div className="global-breadcrumb">
          <span>全局设置</span>
          <b>/</b>
          <strong>{active === "services" ? "服务连接" : "Agent 技能"}</strong>
        </div>
        <Link className="secondary-button" to={returnTo}>
          <ArrowLeft size={14} />
          返回项目
        </Link>
      </header>
      <div className="global-settings-layout">
        <aside>
          <span className="eyebrow">全局设置</span>
          <NavLink
            className={active === "services" ? "active" : ""}
            to="/settings/services"
          >
            <Server size={16} />
            服务连接
          </NavLink>
          <NavLink
            className={active === "skills" ? "active" : ""}
            to="/settings/skills"
          >
            <Sparkles size={16} />
            Agent 技能
          </NavLink>
          <button>
            <Code2 size={16} />
            开发诊断
          </button>
        </aside>
        <main>{children}</main>
      </div>
    </div>
  );
}

export function ServicesPage() {
  const [configuration, setConfiguration] = useState<FoundryConfiguration | null>(null);
  const [endpoint, setEndpoint] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [clearApiKey, setClearApiKey] = useState(false);
  const [imageEndpoint, setImageEndpoint] = useState("");
  const [imageQuality, setImageQuality] = useState<"low" | "medium" | "high">("medium");
  const [imageApiKey, setImageApiKey] = useState("");
  const [clearImageApiKey, setClearImageApiKey] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<"saving" | "testing" | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getFoundryConfiguration(controller.signal)
      .then((loaded) => {
        setConfiguration(loaded);
        setEndpoint(loaded.endpoint);
        setImageEndpoint(loaded.imageEndpoint);
        setImageQuality(loaded.imageQuality);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "Foundry 配置加载失败。");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  async function saveConfiguration(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;

    setBusy("saving");
    setError(null);
    setMessage(null);
    try {
      const saved = await updateFoundryConfiguration({
        endpoint,
        apiKey: apiKey || undefined,
        clearApiKey,
        imageEndpoint,
        imageQuality,
        imageApiKey: imageApiKey || undefined,
        clearImageApiKey,
      });
      setConfiguration(saved);
      setEndpoint(saved.endpoint);
      setApiKey("");
      setClearApiKey(false);
      setImageEndpoint(saved.imageEndpoint);
      setImageQuality(saved.imageQuality);
      setImageApiKey("");
      setClearImageApiKey(false);
      setMessage("配置已安全保存。");
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Foundry 配置保存失败。");
    } finally {
      setBusy(null);
    }
  }

  async function testConnection() {
    if (busy) return;
    setBusy("testing");
    setError(null);
    setMessage(null);
    try {
      const result = await testFoundryConnection();
      setMessage(result.message);
    } catch (testError) {
      setError(testError instanceof Error ? testError.message : "Foundry 连接测试失败。");
    } finally {
      setBusy(null);
    }
  }

  const configured = configuration?.apiKeyConfigured && Boolean(configuration.endpoint);

  return (
    <GlobalSettingsShell active="services">
      <PageTitle
        eyebrow="基础设施"
        title="服务连接"
        description="项目只引用服务能力，不在项目内保存密钥"
        action={
          configured ? (
            <span className="saved-state">
              <Check size={13} />
              密钥已加密保存
            </span>
          ) : undefined
        }
      />
      <section className="service-list panel">
        <div className="service-row">
          <span className="service-icon">
            <Cloud size={18} />
          </span>
          <span>
            <strong>Azure AI Foundry</strong>
            <small>GPT-5.4 · gpt-image-2</small>
          </span>
          <span className={`connection-state ${configured ? "online" : "offline"}`}>
            <i />
            {loading ? "loading" : configured ? "configured" : "not configured"}
          </span>
          <button className="secondary-button" onClick={() => document.getElementById("foundry-endpoint")?.focus()}>
            配置
          </button>
        </div>
      </section>
      <form className="connection-form panel" onSubmit={saveConfiguration}>
        <header className="panel-header">
          <h2>Azure AI Foundry</h2>
          <span>API Key 仅加密存储，保存后不再回传到浏览器</span>
        </header>
        <div className="form-grid">
          <label>
            <span>Endpoint</span>
            <input
              id="foundry-endpoint"
              type="url"
              placeholder="https://your-resource.openai.azure.com"
              value={endpoint}
              onChange={(event) => setEndpoint(event.target.value)}
              disabled={loading || Boolean(busy)}
              required
            />
          </label>
          <label>
            <span>Deployment</span>
            <input value="gpt-5.4" readOnly />
          </label>
          <label>
            <span>API Key</span>
            <input
              type="password"
              autoComplete="new-password"
              placeholder={configuration?.apiKeyConfigured ? "已配置，留空保持不变" : "输入 Azure API Key"}
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              disabled={loading || Boolean(busy) || clearApiKey}
            />
          </label>
          {configuration?.apiKeyConfigured && (
            <label className="check-field">
              <input
                type="checkbox"
                checked={clearApiKey}
                onChange={(event) => setClearApiKey(event.target.checked)}
                disabled={Boolean(busy)}
              />
              <span>清除已保存的 API Key</span>
            </label>
          )}
          <div className="span-2 section-heading second">
            <div>
              <h2>图像生成</h2>
              <p>留空 Endpoint 或 API Key 时复用上方 GPT-5.4 配置。</p>
            </div>
            {configuration?.imageConfigured && <span className="saved-state"><Check size={13} />gpt-image-2 已配置</span>}
          </div>
          <label>
            <span>Image Endpoint</span>
            <input
              type="url"
              placeholder="留空复用 GPT Endpoint"
              value={imageEndpoint}
              onChange={(event) => setImageEndpoint(event.target.value)}
              disabled={loading || Boolean(busy)}
            />
          </label>
          <label>
            <span>Image Deployment</span>
            <input value="gpt-image-2" readOnly />
          </label>
          <label>
            <span>默认图片质量</span>
            <select
              value={imageQuality}
              onChange={(event) => setImageQuality(event.target.value as "low" | "medium" | "high")}
              disabled={loading || Boolean(busy)}
            >
              <option value="low">低</option>
              <option value="medium">中等</option>
              <option value="high">高</option>
            </select>
          </label>
          <label>
            <span>Image API Key</span>
            <input
              type="password"
              autoComplete="new-password"
              placeholder={configuration?.imageApiKeyConfigured ? "已配置，留空保持不变" : "留空复用 GPT API Key"}
              value={imageApiKey}
              onChange={(event) => setImageApiKey(event.target.value)}
              disabled={loading || Boolean(busy) || clearImageApiKey}
            />
          </label>
          {configuration?.imageApiKeyConfigured && (
            <label className="check-field">
              <input
                type="checkbox"
                checked={clearImageApiKey}
                onChange={(event) => setClearImageApiKey(event.target.checked)}
                disabled={Boolean(busy)}
              />
              <span>清除独立图片 API Key，改为复用 GPT Key</span>
            </label>
          )}
        </div>
        {(message || error) && (
          <p className={`settings-feedback ${error ? "error" : "success"}`}>
            {error || message}
          </p>
        )}
        <footer>
          <button
            className="secondary-button"
            type="button"
            onClick={testConnection}
            disabled={!configured || Boolean(busy)}
          >
            {busy === "testing" ? "测试中..." : "测试连接"}
          </button>
          <button className="primary-button" type="submit" disabled={loading || Boolean(busy)}>
            {busy === "saving" ? "保存中..." : "保存配置"}
          </button>
        </footer>
      </form>
      <ComfyUiConfigurationPanel />
    </GlobalSettingsShell>
  );
}

function ComfyUiConfigurationPanel() {
  const [configuration, setConfiguration] = useState<ComfyUiConfiguration | null>(null);
  const [baseUrl, setBaseUrl] = useState("http://127.0.0.1:8188");
  const [isEnabled, setIsEnabled] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<"saving" | "testing" | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getComfyUiConfiguration(controller.signal)
      .then((loaded) => {
        setConfiguration(loaded);
        setBaseUrl(loaded.baseUrl);
        setIsEnabled(loaded.isEnabled);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "ComfyUI 配置加载失败。");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    setBusy("saving");
    setError(null);
    setMessage(null);
    try {
      const saved = await updateComfyUiConfiguration({ baseUrl, isEnabled });
      setConfiguration(saved);
      setBaseUrl(saved.baseUrl);
      setIsEnabled(saved.isEnabled);
      setMessage("本地 ComfyUI 配置已保存。");
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "ComfyUI 配置保存失败。");
    } finally {
      setBusy(null);
    }
  }

  async function testConnection() {
    if (busy) return;
    setBusy("testing");
    setError(null);
    setMessage(null);
    try {
      const result = await testComfyUiConnection();
      setMessage(result.message);
    } catch (testError) {
      setError(testError instanceof Error ? testError.message : "ComfyUI 连接测试失败。");
    } finally {
      setBusy(null);
    }
  }

  return (
    <>
      <section className="service-list panel comfyui-service-summary">
        <div className="service-row">
          <span className="service-icon"><Server size={18} /></span>
          <span><strong>本地 ComfyUI</strong><small>MiniMax H3 · Turbo 4-step LoRA</small></span>
          <span className={`connection-state ${configuration?.isEnabled ? "online" : "offline"}`}>
            <i />
            {loading ? "loading" : configuration?.isEnabled ? "enabled" : "disabled"}
          </span>
          <button className="secondary-button" onClick={() => document.getElementById("comfyui-base-url")?.focus()}>
            配置
          </button>
        </div>
      </section>
      <form className="connection-form panel" onSubmit={save}>
        <header className="panel-header">
          <h2>本地 ComfyUI</h2>
          <span>由 V2 后端直接访问本机服务</span>
        </header>
        <div className="form-grid">
          <label>
            <span>Base URL</span>
            <input
              id="comfyui-base-url"
              type="url"
              value={baseUrl}
              onChange={(event) => setBaseUrl(event.target.value)}
              disabled={loading || Boolean(busy)}
              required
            />
          </label>
          <label><span>连接模式</span><input value="local-http" readOnly /></label>
          <label><span>Workflow</span><input value="minimax-h3-fl2va-turbo-4step" readOnly /></label>
          <label><span>最大并发</span><input value="1" readOnly /></label>
          <label className="check-field span-2">
            <input
              type="checkbox"
              checked={isEnabled}
              onChange={(event) => setIsEnabled(event.target.checked)}
              disabled={loading || Boolean(busy)}
            />
            <span>启用本地 ComfyUI 视频生成</span>
          </label>
        </div>
        {(message || error) && <p className={`settings-feedback ${error ? "error" : "success"}`}>{error || message}</p>}
        <footer>
          <button
            className="secondary-button"
            type="button"
            onClick={testConnection}
            disabled={!configuration?.isEnabled || Boolean(busy)}
          >
            {busy === "testing" ? "检测中..." : "检测节点与模型"}
          </button>
          <button className="primary-button" type="submit" disabled={loading || Boolean(busy)}>
            {busy === "saving" ? "保存中..." : "保存配置"}
          </button>
        </footer>
      </form>
    </>
  );
}

export function SkillsPage() {
  const [skills, setSkills] = useState<SkillRecord[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const deferredSearch = useDeferredValue(search.trim().toLocaleLowerCase());
  const filteredSkills = skills.filter((skill) =>
    `${skill.name} ${skill.id}`.toLocaleLowerCase().includes(deferredSearch),
  );
  const selected = skills.find((skill) => skill.id === selectedId) ?? skills[0];

  useEffect(() => {
    const controller = new AbortController();
    listSkills(controller.signal)
      .then((loaded) => {
        setSkills(loaded);
        setSelectedId((current) => current ?? loaded[0]?.id ?? null);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "Skill 目录加载失败。");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  async function setSkillEnabled(skill: SkillRecord, isEnabled: boolean) {
    if (pendingId) return;
    setPendingId(skill.id);
    setError(null);
    try {
      const updated = await updateSkill(skill.id, isEnabled);
      setSkills((current) => current.map((item) => item.id === updated.id ? updated : item));
    } catch (updateError) {
      setError(updateError instanceof Error ? updateError.message : "Skill 状态更新失败。");
    } finally {
      setPendingId(null);
    }
  }

  return (
    <GlobalSettingsShell active="skills">
      <PageTitle
        eyebrow="Agent 能力"
        title="技能目录"
        description="管理技能版本、工具权限与项目副本"
        action={
          <button className="primary-button" disabled title="Skill 导入将在项目副本阶段开放">
            <Plus size={14} />
            导入技能
          </button>
        }
      />
      <div className="skills-workspace">
        <section className="skills-list">
          <label>
            <Search size={14} />
            <input
              placeholder="搜索技能"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </label>
          {loading && <p className="settings-empty">正在加载 Skill...</p>}
          {!loading && filteredSkills.length === 0 && (
            <p className="settings-empty">没有匹配的 Skill。</p>
          )}
          {filteredSkills.map((skill) => (
            <button
              className={selected?.id === skill.id ? "active" : ""}
              onClick={() => setSelectedId(skill.id)}
              key={skill.id}
            >
              <span className="skill-icon">
                <Sparkles size={15} />
              </span>
              <span>
                <strong>{skill.name}</strong>
                <small>
                  v{skill.version} · {skill.allowedTools.length} tools
                </small>
              </span>
              <i className={skill.isEnabled ? "on" : "off"}>
                {skill.isEnabled ? "on" : "off"}
              </i>
            </button>
          ))}
        </section>
        {selected ? (
          <section className="skill-detail">
            <header>
              <div>
                <span className="eyebrow">v{selected.version} · {selected.isSystem ? "系统 Skill" : "项目 Skill"}</span>
                <h2>{selected.name}</h2>
              </div>
              <label className="switch">
                <input
                  type="checkbox"
                  checked={selected.isEnabled}
                  onChange={(event) => setSkillEnabled(selected, event.target.checked)}
                  disabled={pendingId === selected.id}
                />
                <i />
                <span>{pendingId === selected.id ? "saving" : selected.isEnabled ? "enabled" : "disabled"}</span>
              </label>
            </header>
            {error && <p className="settings-feedback error">{error}</p>}
            <div className="skill-section">
              <span className="eyebrow">描述</span>
              <p>{selected.description}</p>
            </div>
            <div className="skill-section">
              <span className="eyebrow">Allowed tools</span>
              <div className="tool-chips">
                {selected.allowedTools.map((tool) => <span key={tool}>{tool}</span>)}
              </div>
            </div>
            <div className="skill-section">
              <span className="eyebrow">SKILL.md · {selected.sourcePath}</span>
              <pre>{selected.content}</pre>
            </div>
            <footer>
              <button className="secondary-button" disabled>查看源文件</button>
              <button className="primary-button" disabled>编辑项目副本</button>
            </footer>
          </section>
        ) : (
          <section className="skill-detail settings-empty">请选择一个 Skill。</section>
        )}
      </div>
    </GlobalSettingsShell>
  );
}
