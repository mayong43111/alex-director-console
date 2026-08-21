import { Fragment, useEffect, useRef, useState, type FormEvent, type UIEvent } from "react";
import {
  Navigate,
  NavLink,
  useLocation,
  useNavigate,
  useParams,
} from "react-router-dom";
import { ProTable, type ProColumns } from "@ant-design/pro-components";
import { Image, InputNumber, Popconfirm, Select, Switch, Tabs, Tooltip } from "antd";
import {
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  Copy,
  Download,
  Edit3,
  Eye,
  AudioLines,
  ImagePlus,
  List,
  Play,
  Plus,
  RefreshCw,
  Save,
  Search,
  Settings,
  Sparkles,
  Trash2,
  Upload,
  WandSparkles,
  X,
} from "lucide-react";
import {
  assistProjectSettingsField,
  generateProjectCover,
  getProjectSettings,
  previewProjectCover,
  saveProjectSettings,
  type ProjectSettings,
  type ProjectSettingsAssistField,
} from "../api/projectSettings";
import {
  createVisualAsset,
  generateMissingVisualReferenceImages,
  generateMissingVisualReferencePrompts,
  generateVoiceReference,
  generateVisualReferenceImage,
  generateVisualReferencePrompt,
  getVoiceProfile,
  importStoryMaterialAssets,
  listAudioMaterials,
  listVisualAssets,
  saveVoiceProfile,
  uploadAudioMaterial,
  uploadVisualReference,
  updateVisualAsset,
  type AudioMaterial,
  type SaveVoiceProfileInput,
  type SaveVisualAssetInput,
  type VisualAsset,
  type VisualAssetKind,
  type VoiceProfile,
} from "../api/projectAssets";
import {
  listProductionEpisodes,
  type ProductionEpisodeRecord,
} from "../api/projects";
import {
  generateMissingStoryboardImagePrompts,
  generateMissingStoryboardImages,
  generateMissingStoryboardVideoPrompts,
  generateMissingStoryboardVideos,
  generateStoryboardImage,
  generateStoryboardImagePrompt,
  generateStoryboardVideo,
  generateStoryboardVideoPrompt,
  generateStoryboard,
  getShotVideo,
  getStoryboard,
  type BatchStoryboardMediaResult,
  type Storyboard,
  type StoryboardShotTextField,
  type ShotVideoProduction,
  rewriteStoryboardShotText,
  updateStoryboardShotAssets,
  updateStoryboardShotMode,
  updateStoryboardShotText,
} from "../api/storyboards";
import type { ImageGenerationPreview } from "../api/generation";
import { builtInAgentIds, builtInAgentLabels } from "../api/agents";
import {
  getProductionRun,
  listProductionRuns,
  type ProductionRun,
} from "../api/production";
import {
  analyzeStoryMaterial,
  appendAdaptationEpisode,
  appendProjectSourceChapters,
  clearAdaptationEpisodes,
  createProjectSource,
  deleteProjectSourceChapter,
  deleteAdaptationEpisode,
  generateAdaptationScript,
  generateProductionScriptForEpisode,
  getAdaptationScript,
  getProductionScriptPackage,
  getStoryMaterialAnalysis,
  listProjectSources,
  regenerateAdaptationEpisode,
  regenerateProductionScript,
  updateAdaptationEpisode,
  updateProductionScriptScene,
  updateProjectSourceChapter,
  type AdaptationScript,
  type ProductionScriptSceneDraft,
  type ProductionScriptPackage,
  type ProjectSource,
  type SourceChapter,
  type StoryMaterialAnalysis,
} from "../api/projectSources";
import { VersionPicker } from "../components/VersionPicker";
import { RelationGraph } from "../components/RelationGraph";
import { AgentTextArea, type AgentTextAreaStatus } from "../components/AgentTextArea";
import { WorkspaceHeaderExtension } from "../layouts";

const episodes = [
  { id: "production-e01", code: "E01", title: "失控的早晨", state: "review" },
  { id: "production-e02", code: "E02", title: "追查与反转", state: "running" },
  { id: "production-e03", code: "E03", title: "真相回收", state: "draft" },
];

function PageTitle({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow?: string;
  title: string;
  description?: string;
  action?: React.ReactNode;
}) {
  return (
    <header className="page-header">
      <div>
        {eyebrow && <span className="eyebrow">{eyebrow}</span>}
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      {action}
    </header>
  );
}

function EpisodeSelect({ value = "production-e01" }: { value?: string }) {
  const location = useLocation();
  const navigate = useNavigate();
  const projectBase = location.pathname.match(/^\/projects\/[^/]+/)?.[0] ??
    "/projects/tianqiao";
  const changeEpisode = (episodeId: string) => {
    if (location.pathname.includes("/script/")) {
      navigate(`${projectBase}/script/${episodeId}/production`);
    } else if (location.pathname.includes("/storyboard")) {
      navigate(`${projectBase}/storyboard/episodes/${episodeId}`);
    } else if (location.pathname.includes("/review")) {
      navigate(`${projectBase}/review/episodes/${episodeId}`);
    }
  };
  return (
    <label className="select-control">
      <span>生产集</span>
      <select
        value={value}
        onChange={(event) => changeEpisode(event.target.value)}
      >
        {episodes.map((item) => (
          <option key={item.id} value={item.id}>
            {item.code} · {item.title}
          </option>
        ))}
      </select>
      <ChevronDown size={13} />
    </label>
  );
}

export function SettingsPage() {
  const { projectId = "" } = useParams();
  return <ProjectSettingsEditor key={projectId} projectId={projectId} />;
}

const ratioPresets = [
  { ratio: "16:9" as const, width: 1920, height: 1080, label: "横屏叙事" },
  { ratio: "9:16" as const, width: 1080, height: 1920, label: "竖屏短剧" },
  { ratio: "2.39:1" as const, width: 2048, height: 858, label: "宽银幕" },
];

const resolutionPresets = [
  { id: "480p", label: "480p", sizes: { "16:9": [854, 480], "9:16": [480, 854], "2.39:1": [1148, 480] } },
  { id: "720p", label: "720p", sizes: { "16:9": [1280, 720], "9:16": [720, 1280], "2.39:1": [1720, 720] } },
  { id: "1024", label: "1024", sizes: { "16:9": [1024, 576], "9:16": [576, 1024], "2.39:1": [1024, 428] } },
  { id: "1080p", label: "1080p", sizes: { "16:9": [1920, 1080], "9:16": [1080, 1920], "2.39:1": [2580, 1080] } },
  { id: "2k", label: "2K", sizes: { "16:9": [2048, 1152], "9:16": [1152, 2048], "2.39:1": [2048, 858] } },
  { id: "4k", label: "4K", sizes: { "16:9": [3840, 2160], "9:16": [2160, 3840], "2.39:1": [3840, 1608] } },
] as const;

const settingSections = [
  { id: "settings-basics", label: "基础与片型" },
  { id: "settings-delivery", label: "画幅与交付" },
  { id: "settings-visual", label: "视觉与角色" },
  { id: "settings-language", label: "摄影与声音" },
  { id: "settings-generation", label: "生成约束" },
];

const assistFieldLabels: Record<ProjectSettingsAssistField, string> = {
  visualStyle: "视觉风格",
  artDirection: "美术方向",
  characterDesign: "角色造型硬约束",
  colorPalette: "色彩策略",
  cameraLanguage: "摄影语言",
  soundStrategy: "声音策略",
  imagePromptPrefix: "图像生成约束",
};

function ProjectSettingsEditor({ projectId }: { projectId: string }) {
  const [settings, setSettings] = useState<ProjectSettings | null>(null);
  const [activeSection, setActiveSection] = useState(settingSections[0].id);
  const [status, setStatus] = useState<"loading" | "idle" | "dirty" | "saving" | "saved" | "error">("loading");
  const [generatingCover, setGeneratingCover] = useState(false);
  const [coverConfirmation, setCoverConfirmation] = useState(false);
  const [coverInstruction, setCoverInstruction] = useState("");
  const [coverPreview, setCoverPreview] = useState<ImageGenerationPreview | null>(null);
  const [coverPreviewVisible, setCoverPreviewVisible] = useState(false);
  const [assistingField, setAssistingField] = useState<ProjectSettingsAssistField | null>(null);
  const [assistConfirmation, setAssistConfirmation] = useState<ProjectSettingsAssistField | null>(null);
  const [assistInstruction, setAssistInstruction] = useState("");
  const [textAgentStatuses, setTextAgentStatuses] = useState<Record<string, AgentTextAreaStatus>>({});
  const [error, setError] = useState<string | null>(null);
  const textAgentBusy = Object.values(textAgentStatuses).some((agentStatus) => agentStatus !== "idle");

  function trackTextAgent(field: string, agentStatus: AgentTextAreaStatus) {
    setTextAgentStatuses((current) => current[field] === agentStatus
      ? current
      : { ...current, [field]: agentStatus });
  }

  useEffect(() => {
    const controller = new AbortController();
    getProjectSettings(projectId, controller.signal)
      .then((loaded) => {
        setSettings(loaded);
        setStatus("idle");
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "项目设定加载失败。");
        setStatus("error");
      });
    return () => controller.abort();
  }, [projectId]);

  function updateField<Key extends keyof ProjectSettings>(
    field: Key,
    value: ProjectSettings[Key],
  ) {
    setSettings((current) => current ? { ...current, [field]: value } : current);
    setStatus("dirty");
    setError(null);
  }

  function selectRatio(ratio: ProjectSettings["aspectRatio"]) {
    setSettings((current) => {
      if (!current) return current;
      const resolution = resolutionPresets.find((item) => {
        const [width, height] = item.sizes[current.aspectRatio];
        return width === current.outputWidth && height === current.outputHeight;
      }) ?? resolutionPresets.find((item) => item.id === "1080p")!;
      const [outputWidth, outputHeight] = resolution.sizes[ratio];
      return { ...current, aspectRatio: ratio, outputWidth, outputHeight };
    });
    setStatus("dirty");
  }

  function selectResolution(resolutionId: string) {
    const preset = resolutionPresets.find((item) => item.id === resolutionId);
    if (!preset) return;
    const [outputWidth, outputHeight] = preset.sizes[settings!.aspectRatio];
    setSettings((current) => current ? { ...current, outputWidth, outputHeight } : current);
    setStatus("dirty");
  }

  async function submitSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!settings || status === "saving") return;
    setStatus("saving");
    setError(null);
    try {
      const saved = await saveProjectSettings(projectId, settings);
      setSettings(saved);
      setStatus("saved");
      window.dispatchEvent(new CustomEvent("alex:project-updated", {
        detail: { projectId, name: saved.projectName },
      }));
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "项目设定保存失败。");
      setStatus("error");
    }
  }

  async function previewCover() {
    const currentSettings = settings;
    if (!currentSettings || generatingCover || status === "saving") return;
    const shouldSaveSettings = status === "dirty" || currentSettings.version === 0;
    setGeneratingCover(true);
    setError(null);
    try {
      let current = currentSettings;
      if (shouldSaveSettings) {
        setStatus("saving");
        current = await saveProjectSettings(projectId, currentSettings);
        setSettings(current);
        setStatus("saved");
      }
      const preview = await previewProjectCover(projectId, coverInstruction.trim() || undefined);
      setCoverPreview(preview);
    } catch (coverError) {
      setError(coverError instanceof Error ? coverError.message : "概念封面生成规格加载失败。");
      if (shouldSaveSettings) setStatus("error");
    } finally {
      setGeneratingCover(false);
    }
  }

  async function generateCover() {
    const currentSettings = settings;
    if (!currentSettings || !coverPreview || generatingCover) return;
    setGeneratingCover(true);
    setError(null);
    try {
      const cover = await generateProjectCover(
        projectId,
        coverInstruction.trim() || undefined,
        coverPreview.prompt,
      );
      setSettings({ ...currentSettings, cover });
      setCoverConfirmation(false);
      setCoverPreview(null);
      setCoverInstruction("");
      setStatus("saved");
    } catch (coverError) {
      setError(coverError instanceof Error ? coverError.message : "概念封面生成失败。");
      setStatus("error");
    } finally {
      setGeneratingCover(false);
    }
  }

  function requestCoverGeneration() {
    setCoverInstruction("");
    setCoverPreview(null);
    setCoverConfirmation(true);
  }

  async function assistField(field: ProjectSettingsAssistField, instruction?: string) {
    const currentSettings = settings;
    if (!currentSettings || assistingField) return;
    setAssistingField(field);
    setError(null);
    try {
      const result = await assistProjectSettingsField(
        projectId,
        field,
        String(currentSettings[field] ?? ""),
        currentSettings,
        instruction,
      );
      updateField(field, result.value);
    } catch (assistError) {
      setError(assistError instanceof Error ? assistError.message : "AI 帮写失败。");
    } finally {
      setAssistingField(null);
    }
  }

  function requestAssist(field: ProjectSettingsAssistField) {
    const hasContent = Boolean(String(settings?.[field] ?? "").trim());
    if (!hasContent) {
      void assistField(field);
      return;
    }
    setAssistInstruction("");
    setAssistConfirmation(field);
  }

  function renderAiFieldAction(field: ProjectSettingsAssistField) {
    const hasContent = Boolean(String(settings?.[field] ?? "").trim());
    return (
      <button
        className="ai-field-icon"
        type="button"
        onClick={() => requestAssist(field)}
        disabled={Boolean(assistingField)}
        title={hasContent ? "使用 AI 调整当前内容" : "使用 AI 生成内容"}
        aria-label={hasContent ? `调整${assistFieldLabels[field]}` : `生成${assistFieldLabels[field]}`}
      >
        {assistingField === field ? <span className="spinner" /> : <WandSparkles size={15} />}
      </button>
    );
  }

  function openSection(sectionId: string) {
    setActiveSection(sectionId);
    const editor = document.getElementById("project-settings-form");
    const section = document.getElementById(sectionId);
    if (!editor || !section) return;
    const top = section.getBoundingClientRect().top
      - editor.getBoundingClientRect().top
      + editor.scrollTop;
    editor.scrollTo({ top, behavior: "smooth" });
  }

  function syncActiveSection(event: UIEvent<HTMLFormElement>) {
    const editor = event.currentTarget;
    if (editor.scrollTop + editor.clientHeight >= editor.scrollHeight - 2) {
      setActiveSection(settingSections.at(-1)!.id);
      return;
    }
    const threshold = editor.scrollTop + 96;
    let currentSection = settingSections[0].id;
    for (const section of settingSections) {
      const element = document.getElementById(section.id);
      if (element && element.offsetTop <= threshold) currentSection = section.id;
    }
    setActiveSection(currentSection);
  }

  if (!settings) {
    return (
      <div className="page settings-loading">
        <span className="spinner" />
        <strong>{status === "error" ? "项目设定无法加载" : "正在读取项目设定"}</strong>
        {error && <p>{error}</p>}
      </div>
    );
  }

  return (
    <div className="page settings-page">
      <div className="settings-layout">
        <aside className="subnav" aria-label="项目设定章节">
          <div className="settings-subnav-heading">
            <strong>项目设定</strong>
            <span>快速跳转</span>
          </div>
          {settingSections.map((item, index) => (
            <button
              type="button"
              className={activeSection === item.id ? "active" : ""}
              key={item.id}
              onClick={() => openSection(item.id)}
              aria-current={activeSection === item.id ? "location" : undefined}
            >
              <b>{String(index + 1).padStart(2, "0")}</b>
              {item.label}
            </button>
          ))}
        </aside>
        <form
          className="editor-surface settings-editor"
          id="project-settings-form"
          data-workspace-save-state={textAgentBusy || assistingField ? "blocked" : status}
          onSubmit={submitSettings}
          onScroll={syncActiveSection}
        >
          <section className="settings-cover" aria-label="项目概念封面">
            {settings.cover ? (
              <Image
                src={`${settings.cover.contentUrl}?v=${settings.cover.version}`}
                alt={`${settings.projectName}概念封面`}
                preview={{
                  mask: false,
                  visible: coverPreviewVisible,
                  onVisibleChange: setCoverPreviewVisible,
                }}
              />
            ) : (
              <div className="settings-cover-empty"><ImagePlus size={22} strokeWidth={1.5} /><span>尚未生成封面</span></div>
            )}
            <div className="settings-cover-actions">
              {settings.cover && (
                <button
                  className="icon-button"
                  type="button"
                  onClick={() => setCoverPreviewVisible(true)}
                  title="预览封面"
                  aria-label="预览封面"
                >
                  <Eye size={15} />
                </button>
              )}
              <button
                className="icon-button"
                type="button"
                onClick={requestCoverGeneration}
                disabled={generatingCover || status === "saving"}
                title={settings.cover ? "重新生成封面" : "生成封面"}
                aria-label={settings.cover ? "重新生成封面" : "生成封面"}
              >
                {generatingCover ? <span className="spinner" /> : <Sparkles size={15} />}
              </button>
              {settings.cover && <VersionPicker compact projectId={projectId} assetId={settings.cover.assetId} label="封面版本" />}
            </div>
          </section>
          <section className="settings-section" id="settings-basics">
            <div className="section-heading">
              <div>
                <span className="eyebrow">01 / FOUNDATION</span>
                <h2>基础与片型</h2>
                <p>定义项目身份、受众和生产规模。</p>
              </div>
              <div className="settings-state-actions">
                {settings.assetId && (
                  <VersionPicker compact projectId={projectId} assetId={settings.assetId} label="项目设定版本" />
                )}
                <span className={`saved-state ${status}`}>
                  <Check size={13} />
                  {status === "dirty" ? "有未保存修改" : status === "saved" ? "新版本已保存" : `v${settings.version} 已同步`}
                </span>
              </div>
            </div>
            <div className="form-grid">
              <label>
                <span>项目名称</span>
                <input required maxLength={200} value={settings.projectName} onChange={(event) => updateField("projectName", event.target.value)} />
              </label>
              <label>
                <span>片型</span>
                <select value={settings.contentType} onChange={(event) => updateField("contentType", event.target.value)}>
                  <option>动画短剧</option>
                  <option>动画故事片</option>
                  <option>漫画动态影像</option>
                </select>
              </label>
              <label className="span-2">
                <span>项目简介</span>
                <AgentTextArea
                  agentId={builtInAgentIds.projectDescriptionWriter}
                  agentLabel={builtInAgentLabels.projectDescriptionWriter}
                  value={settings.description}
                  onChange={(value) => updateField("description", value)}
                  context={settings}
                  onStatusChange={(agentStatus) => trackTextAgent("description", agentStatus)}
                  rows={3}
                  maxLength={4000}
                />
              </label>
              <label className="span-2">
                <span>目标受众</span>
                <input required maxLength={300} value={settings.targetAudience} onChange={(event) => updateField("targetAudience", event.target.value)} />
              </label>
              <label>
                <span>计划生产集数</span>
                <input
                  required
                  type="number"
                  min={-1}
                  max={1000}
                  step={1}
                  title="-1 表示由剧本内容决定集数"
                  value={settings.plannedEpisodeCount}
                  onChange={(event) => {
                    const value = Number(event.target.value);
                    if (!Number.isInteger(value)) return;
                    updateField(
                      "plannedEpisodeCount",
                      value === 0 ? (settings.plannedEpisodeCount === -1 ? 1 : -1) : value,
                    );
                  }}
                />
              </label>
              <label>
                <span>单集目标时长</span>
                <div className="input-suffix">
                  <input required type="number" min={1} max={86400} value={settings.targetEpisodeSeconds} onChange={(event) => updateField("targetEpisodeSeconds", Number(event.target.value))} />
                  <span>秒</span>
                </div>
              </label>
            </div>
          </section>

          <section className="settings-section" id="settings-delivery">
            <div className="section-heading">
              <div>
                <span className="eyebrow">02 / FRAME</span>
                <h2>画幅与交付</h2>
                <p>后续图像、分镜和视频默认继承此规格。</p>
              </div>
            </div>
            <div className="ratio-options">
              {ratioPresets.map((item) => (
                <button
                  type="button"
                  className={settings.aspectRatio === item.ratio ? "active" : ""}
                  onClick={() => selectRatio(item.ratio)}
                  key={item.ratio}
                >
                  <i style={{ aspectRatio: item.ratio.replace(":", "/") }} />
                  <strong>{item.ratio}</strong>
                  <small>{item.label}</small>
                </button>
              ))}
            </div>
            <div className="delivery-readout">
              <label className="resolution-selector">
                <span>输出分辨率</span>
                <select
                  aria-label="输出分辨率"
                  value={resolutionPresets.find((item) => {
                    const [width, height] = item.sizes[settings.aspectRatio];
                    return width === settings.outputWidth && height === settings.outputHeight;
                  })?.id ?? "custom"}
                  onChange={(event) => selectResolution(event.target.value)}
                >
                  {!resolutionPresets.some((item) => {
                    const [width, height] = item.sizes[settings.aspectRatio];
                    return width === settings.outputWidth && height === settings.outputHeight;
                  }) && (
                    <option value="custom" disabled>当前 · {settings.outputWidth} × {settings.outputHeight}</option>
                  )}
                  {resolutionPresets.map((item) => {
                    const [width, height] = item.sizes[settings.aspectRatio];
                    return <option value={item.id} key={item.id}>{item.label} · {width} × {height}</option>;
                  })}
                </select>
              </label>
              <div><span>画面方向</span><strong>{settings.outputWidth > settings.outputHeight ? "横向" : "纵向"}</strong></div>
              <div><span>继承范围</span><strong>全项目</strong></div>
            </div>
          </section>

          <section className="settings-section" id="settings-visual">
            <div className="section-heading">
              <div>
                <span className="eyebrow">03 / VISUAL BIBLE</span>
                <h2>视觉与角色</h2>
                <p>这里的规则将进入后续角色、场景和镜头生成上下文。</p>
              </div>
            </div>
            <div className="form-grid">
              <label>
                <span>视觉风格</span>
                <div className="ai-field-control">
                  <input required maxLength={200} value={settings.visualStyle} onChange={(event) => updateField("visualStyle", event.target.value)} />
                  {renderAiFieldAction("visualStyle")}
                </div>
              </label>
              <label className="span-2">
                <span>美术方向</span>
                <AgentTextArea
                  agentId={builtInAgentIds.artDirectionWriter}
                  agentLabel={builtInAgentLabels.artDirectionWriter}
                  value={settings.artDirection}
                  onChange={(value) => updateField("artDirection", value)}
                  context={settings}
                  onStatusChange={(agentStatus) => trackTextAgent("artDirection", agentStatus)}
                  rows={4}
                  maxLength={2000}
                />
              </label>
              <label className="span-2">
                <span>角色造型硬约束</span>
                <AgentTextArea
                  agentId={builtInAgentIds.characterDesignWriter}
                  agentLabel={builtInAgentLabels.characterDesignWriter}
                  value={settings.characterDesign}
                  onChange={(value) => updateField("characterDesign", value)}
                  context={settings}
                  onStatusChange={(agentStatus) => trackTextAgent("characterDesign", agentStatus)}
                  required
                  rows={4}
                  maxLength={1000}
                />
                <small>主人公的物种、体型、服装和拟人化程度必须在这里明确。</small>
              </label>
              <label className="span-2">
                <span>色彩策略</span>
                <AgentTextArea
                  agentId={builtInAgentIds.colorPaletteWriter}
                  agentLabel={builtInAgentLabels.colorPaletteWriter}
                  value={settings.colorPalette}
                  onChange={(value) => updateField("colorPalette", value)}
                  context={settings}
                  onStatusChange={(agentStatus) => trackTextAgent("colorPalette", agentStatus)}
                  rows={3}
                  maxLength={1000}
                />
              </label>
            </div>
          </section>

          <section className="settings-section" id="settings-language">
            <div className="section-heading">
              <div>
                <span className="eyebrow">04 / DIRECTION</span>
                <h2>摄影与声音</h2>
                <p>保持每个生产集的镜头语法和听觉识别一致。</p>
              </div>
            </div>
            <div className="form-grid">
              <label className="span-2">
                <span>摄影语言</span>
                <AgentTextArea
                  agentId={builtInAgentIds.cameraLanguageWriter}
                  agentLabel={builtInAgentLabels.cameraLanguageWriter}
                  value={settings.cameraLanguage}
                  onChange={(value) => updateField("cameraLanguage", value)}
                  context={settings}
                  onStatusChange={(agentStatus) => trackTextAgent("cameraLanguage", agentStatus)}
                  rows={4}
                  maxLength={2000}
                />
              </label>
              <label className="span-2">
                <span>声音策略</span>
                <AgentTextArea
                  agentId={builtInAgentIds.soundStrategyWriter}
                  agentLabel={builtInAgentLabels.soundStrategyWriter}
                  value={settings.soundStrategy}
                  onChange={(value) => updateField("soundStrategy", value)}
                  context={settings}
                  onStatusChange={(agentStatus) => trackTextAgent("soundStrategy", agentStatus)}
                  rows={4}
                  maxLength={2000}
                />
              </label>
            </div>
          </section>

          <section className="settings-section" id="settings-generation">
            <div className="section-heading">
              <div>
              <span className="eyebrow">05 / GENERATION</span>
              <h2>图像生成约束</h2>
              <p>作为所有图像提示词的项目级前缀，不包含具体镜头内容。</p>
              </div>
            </div>
            <div className="form-grid">
              <div className="span-2">
                <AgentTextArea
                  agentId={builtInAgentIds.imagePromptPrefixWriter}
                  agentLabel={builtInAgentLabels.imagePromptPrefixWriter}
                  value={settings.imagePromptPrefix}
                  onChange={(value) => updateField("imagePromptPrefix", value)}
                  context={settings}
                  onStatusChange={(agentStatus) => trackTextAgent("imagePromptPrefix", agentStatus)}
                  rows={6}
                  maxLength={4000}
                  placeholder="例如：法式彩色冒险漫画，拟人犬角色，清晰墨线……"
                />
              </div>
            </div>
          </section>

          {error && <p className="settings-error">{error}</p>}
        </form>
      </div>
      {coverConfirmation && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={() => setCoverConfirmation(false)}
        >
          <form
            className="dialog ai-assist-dialog generation-confirmation-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              void (coverPreview ? generateCover() : previewCover());
            }}
          >
            <span className="eyebrow">图像模型 / {settings.cover ? "重新生成" : "首次生成"}</span>
            <h2>{coverPreview ? "核对概念封面生成规格" : `${settings.cover ? "重新生成" : "生成"}概念封面`}</h2>
            <p>{coverPreview ? "确认后将严格按以下提示词、参数和输入资产版本执行。" : "先预览完整生成规格；生成结果会保存提示词、参数及输入版本。"}</p>
            <label>
              <span>本次调整意见（可选）</span>
              <textarea
                autoFocus={!coverPreview}
                disabled={Boolean(coverPreview)}
                rows={4}
                maxLength={1000}
                value={coverInstruction}
                onChange={(event) => { setCoverInstruction(event.target.value); setCoverPreview(null); }}
                placeholder="例如：强化三位主角的动作姿态，减少背景人物，保留现有漫画风格"
              />
            </label>
            {coverPreview && <GenerationPreviewDetails preview={coverPreview} />}
            <div>
              <button className="secondary-button" type="button" onClick={() => { setCoverConfirmation(false); setCoverPreview(null); }}>取消</button>
              <button className="primary-button" type="submit">
                <Sparkles size={13} />
                {coverPreview ? "确认并生成" : "预览生成规格"}
              </button>
            </div>
          </form>
        </div>
      )}
      {assistConfirmation && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={() => setAssistConfirmation(null)}
        >
          <form
            className="dialog ai-assist-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              const field = assistConfirmation;
              const instruction = assistInstruction.trim();
              setAssistConfirmation(null);
              void assistField(field, instruction || undefined);
            }}
          >
            <span className="eyebrow">AI / 智能调整</span>
            <h2>调整“{assistFieldLabels[assistConfirmation]}”</h2>
            <p>将基于当前内容和完整项目设定生成替换文本，结果会先回填到表单，不会自动保存版本。</p>
            <label>
              <span>补充意见（可选）</span>
              <textarea
                autoFocus
                rows={4}
                maxLength={1000}
                value={assistInstruction}
                onChange={(event) => setAssistInstruction(event.target.value)}
                placeholder="例如：保留现有方向，减少抽象形容词，强化可执行约束"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" onClick={() => setAssistConfirmation(null)}>取消</button>
              <button className="primary-button" type="submit">
                <WandSparkles size={13} />
                确认优化
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

type SourceWorkspaceView = "source" | "analysis" | "script";

type ScriptWorkspaceView = "adaptation" | "production";

function getScriptWorkspacePath(
  projectId: string,
  resourceId: string,
  view: ScriptWorkspaceView,
) {
  return `/projects/${projectId}/script/${resourceId}/${view}`;
}

function getSourceWorkspacePath(
  projectId: string,
  sourceId: string,
  view: SourceWorkspaceView,
) {
  if (view === "script") {
    return getScriptWorkspacePath(projectId, sourceId, "adaptation");
  }
  return `/projects/${projectId}/story/${sourceId}/${view === "analysis" ? "material" : "source"}`;
}

export function SourcePage() {
  const { projectId = "", sourceId, sourceEpisodeId } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const workspaceView: SourceWorkspaceView = /\/story\/[^/]+\/material\/?$/.test(location.pathname)
    ? "analysis"
    : /\/script\/[^/]+\/adaptation\/?$/.test(location.pathname)
      || location.pathname.includes("/script/adaptation")
      || location.pathname.includes("/script/draft")
      ? "script"
      : "source";
  const requestedSourceId = sourceId ?? sourceEpisodeId;
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [sources, setSources] = useState<ProjectSource[]>([]);
  const [selectedChapterId, setSelectedChapterId] = useState<string>();
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [importOpen, setImportOpen] = useState(false);
  const [importing, setImporting] = useState(false);
  const [importTitle, setImportTitle] = useState("");
  const [importDescription, setImportDescription] = useState("");
  const [importContent, setImportContent] = useState("");
  const [importFileName, setImportFileName] = useState("");
  const [importMode, setImportMode] = useState<"create" | "append">("create");
  const [chapterEditor, setChapterEditor] = useState<{ id: string; title: string; content: string } | null>(null);
  const [chapterSaving, setChapterSaving] = useState(false);
  const fileModeRef = useRef<"create" | "append">("create");
  const [analysis, setAnalysis] = useState<StoryMaterialAnalysis | null>(null);
  const [analysisSourceId, setAnalysisSourceId] = useState<string>();
  const [analysisErrorSourceId, setAnalysisErrorSourceId] = useState<string>();
  const [script, setScript] = useState<AdaptationScript | null>(null);
  const [projectSettings, setProjectSettings] = useState<ProjectSettings | null>(null);
  const [working, setWorking] = useState<"analysis" | "script" | "append" | "update" | "regenerate" | "delete" | "clear" | "confirm" | null>(null);
  const analysisControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    listProjectSources(projectId, controller.signal)
      .then((items) => {
        setSources(items);
        setError("");
        const requested = items.find((item) => item.id === requestedSourceId);
        const first = requested ?? items[0];
        if (first) {
          setSelectedChapterId(first.chapters[0]?.id);
          const canonicalPath = getSourceWorkspacePath(projectId, first.id, workspaceView);
          if (!requested || location.pathname !== canonicalPath) {
            navigate(canonicalPath, { replace: true });
          }
        } else if (requestedSourceId) {
          navigate(workspaceView === "script"
            ? `/projects/${projectId}/script/adaptation`
            : `/projects/${projectId}/story`, { replace: true });
        }
      })
      .catch((loadError: unknown) => {
        if (!controller.signal.aborted) {
          setError(loadError instanceof Error ? loadError.message : "原文资料加载失败。");
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [location.pathname, navigate, projectId, requestedSourceId, workspaceView]);

  const activeSource = sources.find((item) => item.id === requestedSourceId) ?? sources[0];
  const selectedChapter = activeSource?.chapters.find((item) => item.id === selectedChapterId)
    ?? activeSource?.chapters[0];
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const filteredChapters = activeSource?.chapters.filter((chapter) =>
    !normalizedSearch
    || chapter.title.toLocaleLowerCase().includes(normalizedSearch)
    || chapter.content.toLocaleLowerCase().includes(normalizedSearch)) ?? [];
  const activeSourceId = activeSource?.id;
  const analysisLoadState = analysisSourceId === activeSourceId
    ? "ready"
    : analysisErrorSourceId === activeSourceId
      ? "error"
      : "loading";

  useEffect(() => () => analysisControllerRef.current?.abort(), [activeSourceId, selectedChapterId]);

  useEffect(() => {
    if (!activeSourceId) return;
    const controller = new AbortController();
    Promise.all([
      getStoryMaterialAnalysis(projectId, activeSourceId, controller.signal),
      getAdaptationScript(projectId, activeSourceId, controller.signal),
      getProjectSettings(projectId, controller.signal),
    ]).then(([loadedAnalysis, loadedScript, loadedSettings]) => {
      setAnalysis(loadedAnalysis);
      setAnalysisSourceId(activeSourceId);
      setAnalysisErrorSourceId(undefined);
      setScript(loadedScript);
      setProjectSettings(loadedSettings);
    }).catch((loadError: unknown) => {
      if (!controller.signal.aborted) {
        setAnalysisErrorSourceId(activeSourceId);
        setError(loadError instanceof Error ? loadError.message : "故事开发资料加载失败。");
      }
    });
    return () => controller.abort();
  }, [activeSourceId, projectId]);

  const selectSource = (source: ProjectSource) => {
    setSelectedChapterId(source.chapters[0]?.id);
    navigate(getSourceWorkspacePath(projectId, source.id, workspaceView));
  };

  const openImport = (mode: "create" | "append" = "create") => {
    setImportMode(mode);
    setImportTitle("");
    setImportDescription("");
    setImportContent("");
    setImportFileName("");
    setError("");
    setImportOpen(true);
  };

  const chooseFile = async (file: File | undefined) => {
    if (!file) return;
    const content = await file.text();
    setImportMode(fileModeRef.current);
    setImportTitle(file.name.replace(/\.(txt|md|markdown)$/i, ""));
    setImportContent(content);
    setImportFileName(file.name);
    setError("");
    setImportOpen(true);
    if (fileInputRef.current) fileInputRef.current.value = "";
  };

  const submitImport = async (event: FormEvent) => {
    event.preventDefault();
    setImporting(true);
    setError("");
    try {
      const source = importMode === "append" && activeSource
        ? await appendProjectSourceChapters(projectId, activeSource.id, {
            content: importContent,
            fileName: importFileName || undefined,
          })
        : await createProjectSource(projectId, {
            title: importTitle,
            description: importDescription,
            content: importContent,
            fileName: importFileName || undefined,
          });
      setSources((current) => importMode === "append"
        ? current.map((item) => item.id === source.id ? source : item)
        : [...current, source]);
      const selectedImportedChapter = importMode === "append"
        ? source.chapters.find((chapter) => !activeSource?.chapters.some((item) => item.id === chapter.id))
        : source.chapters[0];
      setSelectedChapterId(selectedImportedChapter?.id ?? source.chapters[0]?.id);
      if (importMode === "append" && analysis) {
        setAnalysis({
          ...analysis,
          isStale: true,
          staleReason: `原文资料已更新到 v${source.version}，尚有章节未分析。`,
        });
      }
      setImportOpen(false);
      navigate(getSourceWorkspacePath(projectId, source.id, "source"));
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "原文资料导入失败。");
    } finally {
      setImporting(false);
    }
  };

  const markAnalysisStale = (source: ProjectSource) => {
    if (!analysis) return;
    setAnalysis({
      ...analysis,
      isStale: true,
      staleReason: `原文资料已更新到 v${source.version}，章节分析需要重新生成。`,
    });
  };

  const saveChapter = async (event: FormEvent) => {
    event.preventDefault();
    if (!activeSource || !chapterEditor) return;
    setChapterSaving(true);
    setError("");
    try {
      const source = await updateProjectSourceChapter(
        projectId,
        activeSource.id,
        chapterEditor.id,
        { title: chapterEditor.title, content: chapterEditor.content },
      );
      setSources((current) => current.map((item) => item.id === source.id ? source : item));
      setSelectedChapterId(chapterEditor.id);
      markAnalysisStale(source);
      setChapterEditor(null);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "章节保存失败。");
    } finally {
      setChapterSaving(false);
    }
  };

  const deleteChapter = async (chapterId: string) => {
    if (!activeSource) return;
    const deletedIndex = activeSource.chapters.findIndex((chapter) => chapter.id === chapterId);
    setChapterSaving(true);
    setError("");
    try {
      const source = await deleteProjectSourceChapter(projectId, activeSource.id, chapterId);
      setSources((current) => current.map((item) => item.id === source.id ? source : item));
      setSelectedChapterId(source.chapters[Math.min(deletedIndex, source.chapters.length - 1)]?.id);
      markAnalysisStale(source);
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "章节删除失败。");
    } finally {
      setChapterSaving(false);
    }
  };

  const editChapter = (chapter: SourceChapter) => {
    setError("");
    setChapterEditor({ id: chapter.id, title: chapter.title, content: chapter.content });
  };

  const requestAnalysis = async () => {
    if (working === "analysis") {
      analysisControllerRef.current?.abort();
      return;
    }
    if (!activeSource || !selectedChapter) return;
    const controller = new AbortController();
    analysisControllerRef.current = controller;
    setWorking("analysis");
    setError("");
    try {
      const result = await analyzeStoryMaterial(
        projectId,
        activeSource.id,
        selectedChapter.id,
        controller.signal,
      );
      setAnalysis(result);
    } catch (analysisError) {
      if (analysisError instanceof DOMException && analysisError.name === "AbortError") return;
      setError(analysisError instanceof Error ? analysisError.message : "素材分析失败。");
    } finally {
      if (analysisControllerRef.current === controller) {
        analysisControllerRef.current = null;
        setWorking(null);
      }
    }
  };

  const requestScript = async (
    mode: AdaptationScript["mode"],
    desiredEpisodeCount?: number,
    instruction?: string,
  ) => {
    if (!activeSource) return;
    setWorking("script");
    setError("");
    try {
      const result = await generateAdaptationScript(projectId, activeSource.id, {
        mode,
        desiredEpisodeCount,
        instruction: instruction ?? (mode === "rearranged"
          ? "严格参考项目设定、原文章节和素材图谱重新编排；每集建立清晰冲突、大小爆点和集尾追看动力。"
          : "按原文章节顺序建立改编方案，不重新编排，不分析大小爆点。"),
      });
      setScript(result);
      navigate(getScriptWorkspacePath(projectId, activeSource.id, "adaptation"));
    } catch (scriptError) {
      setError(scriptError instanceof Error ? scriptError.message : "改编大纲生成失败。");
    } finally {
      setWorking(null);
    }
  };

  const appendScriptEpisode = async (count: number, instruction?: string) => {
    if (!activeSource) return;
    setWorking("append");
    setError("");
    try {
      setScript(await appendAdaptationEpisode(projectId, activeSource.id, { count, instruction }));
    } catch (appendError) {
      setError(appendError instanceof Error ? appendError.message : "添加剧集失败。");
    } finally {
      setWorking(null);
    }
  };

  const deleteScriptEpisode = async (episodeNumber: number) => {
    if (!activeSource) return false;
    setWorking("delete");
    setError("");
    try {
      setScript(await deleteAdaptationEpisode(projectId, activeSource.id, episodeNumber));
      return true;
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "删除剧集失败。");
      return false;
    } finally {
      setWorking(null);
    }
  };

  const regenerateScriptEpisode = async (episodeNumber: number, instruction: string) => {
    if (!activeSource) return false;
    setWorking("regenerate");
    setError("");
    try {
      setScript(await regenerateAdaptationEpisode(
        projectId,
        activeSource.id,
        episodeNumber,
        { instruction },
      ));
      return true;
    } catch (regenerateError) {
      setError(regenerateError instanceof Error ? regenerateError.message : "重新生成剧集失败。");
      return false;
    } finally {
      setWorking(null);
    }
  };

  const updateScriptEpisode = async (
    episodeNumber: number,
    input: { title: string; logline: string; sceneSummaries: string[] },
  ) => {
    if (!activeSource) return false;
    setWorking("update");
    setError("");
    try {
      setScript(await updateAdaptationEpisode(projectId, activeSource.id, episodeNumber, input));
      return true;
    } catch (updateError) {
      setError(updateError instanceof Error ? updateError.message : "修改章节失败。");
      return false;
    } finally {
      setWorking(null);
    }
  };

  const clearScriptEpisodes = async () => {
    if (!activeSource) return false;
    setWorking("clear");
    setError("");
    try {
      setScript(await clearAdaptationEpisodes(projectId, activeSource.id));
      return true;
    } catch (clearError) {
      setError(clearError instanceof Error ? clearError.message : "清空改编方案失败。");
      return false;
    } finally {
      setWorking(null);
    }
  };

  const generateEpisodeProductionScript = async (episodeNumber: number) => {
    if (!activeSource) return false;
    const existingProductionEpisodeId = script?.productionEpisodeMap?.[episodeNumber];
    if (existingProductionEpisodeId) {
      navigate(getScriptWorkspacePath(projectId, existingProductionEpisodeId, "production"));
      return true;
    }
    setWorking("confirm");
    setError("");
    try {
      const updated = await generateProductionScriptForEpisode(
        projectId,
        activeSource.id,
        episodeNumber,
      );
      setScript(updated);
      window.dispatchEvent(new Event("alex:production-episodes-updated"));
      const productionEpisodeId = updated.productionEpisodeMap?.[episodeNumber];
      if (productionEpisodeId) {
        navigate(getScriptWorkspacePath(projectId, productionEpisodeId, "production"));
      }
      return true;
    } catch (confirmError) {
      setError(confirmError instanceof Error ? confirmError.message : "单集正式剧本生成失败。");
      return false;
    } finally {
      setWorking(null);
    }
  };

  return (
    <div className={`page full-height-page${workspaceView === "analysis" ? " material-page" : ""}`}>
      {workspaceView === "source" && (
        <input
          ref={fileInputRef}
          className="visually-hidden"
          type="file"
          accept=".txt,.md,.markdown,text/plain,text/markdown"
          onChange={(event) => void chooseFile(event.target.files?.[0])}
        />
      )}
      {workspaceView === "script" && (
        <ScriptStageTabs
          projectId={projectId}
          active="adaptation"
          sourceId={activeSource?.id}
          productionEpisodeId={script?.productionEpisodeIds[0]}
        />
      )}
      {sources.length > 0 && workspaceView !== "script" && (
        <Tabs
          className={`story-view-tabs${workspaceView === "source" ? " with-actions" : ""}`}
          size="small"
          activeKey={workspaceView}
          items={[
            { key: "source", label: "原文章节" },
            { key: "analysis", label: "素材图谱" },
          ]}
          onChange={(key) => {
            if (activeSource) {
              navigate(getSourceWorkspacePath(projectId, activeSource.id, key === "source" ? "source" : "analysis"));
            }
          }}
          tabBarExtraContent={workspaceView === "source" ? (
            <div className="button-group">
              <button className="secondary-button" type="button" onClick={() => {
                fileModeRef.current = "append";
                fileInputRef.current?.click();
              }}>
                <Upload size={14} />上传追加
              </button>
              <button className="primary-button" type="button" onClick={() => openImport("append")}>
                <Plus size={14} />追加章节
              </button>
            </div>
          ) : undefined}
        />
      )}
      {error && !importOpen && <div className="settings-error">{error}</div>}
      {loading ? (
        <div className="source-empty-state"><strong>正在读取原文资料…</strong></div>
      ) : sources.length === 0 ? (
        <div className="source-empty-state">
          <div className="source-empty-mark"><Upload size={24} /></div>
          <span className="eyebrow">从来源开始</span>
          <h2>这个项目还没有原文资料</h2>
          <p>上传 TXT / Markdown，或直接粘贴原文。系统会识别章节，稍后可从任意章节取材、重排或原创，不会自动创建生产剧集。</p>
          <div className="button-group">
            <button className="secondary-button" type="button" onClick={() => {
              fileModeRef.current = "create";
              fileInputRef.current?.click();
            }}>
              <Upload size={14} />上传文本
            </button>
            <button className="primary-button" type="button" onClick={() => openImport("create")}>
              <Plus size={14} />粘贴原文
            </button>
          </div>
        </div>
      ) : workspaceView === "source" ? (
        <div className="document-workspace">
          <aside className="document-tree">
            <div className="tree-search">
              <Search size={14} />
              <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="搜索原文资料" />
            </div>
            {sources.length > 1 && (
              <label className="tree-source-select">
                <span className="visually-hidden">选择原文资料</span>
                <select value={activeSource?.id} onChange={(event) => {
                  const source = sources.find((item) => item.id === event.target.value);
                  if (source) selectSource(source);
                }}>
                  {sources.map((source) => <option value={source.id} key={source.id}>{source.title}</option>)}
                </select>
                <ChevronDown size={13} />
              </label>
            )}
            <div className="tree-group">
              {filteredChapters.map((chapter) => {
                const isAnalyzed = analysis?.analyzedChapterIds?.includes(chapter.id) ?? false;
                const status = analysisLoadState === "loading"
                  ? { className: "loading", label: "读取中" }
                  : analysisLoadState === "error"
                    ? { className: "unknown", label: "未知" }
                    : isAnalyzed
                      ? { className: "analyzed", label: "已分析" }
                      : { className: "pending", label: "未分析" };
                return (
                  <div className="tree-section-row" key={chapter.id}>
                    <button
                      type="button"
                      className={selectedChapter?.id === chapter.id ? "tree-section active" : "tree-section"}
                      onClick={() => setSelectedChapterId(chapter.id)}
                    >
                      <span className="tree-section-number">{String(chapter.number).padStart(2, "0")}</span>
                      <strong title={chapter.title}>{chapter.title}</strong>
                      <span className={`tree-analysis-status ${status.className}`} title={`分析状态：${status.label}`}>
                        {isAnalyzed && analysisLoadState === "ready" && <Check size={10} />}
                        {status.label}
                      </span>
                    </button>
                    <div className="tree-section-actions">
                      <button type="button" title="编辑章节" aria-label={`编辑 ${chapter.title}`} onClick={() => editChapter(chapter)}>
                        <Edit3 size={12} />
                      </button>
                      <Popconfirm
                        title="删除章节"
                        description="删除后将保存为新的原文版本。"
                        okText="删除"
                        cancelText="取消"
                        okButtonProps={{ danger: true }}
                        disabled={activeSource.chapters.length <= 1 || chapterSaving}
                        onConfirm={() => void deleteChapter(chapter.id)}
                      >
                        <button type="button" title="删除章节" aria-label={`删除 ${chapter.title}`} disabled={activeSource.chapters.length <= 1 || chapterSaving}>
                          <Trash2 size={12} />
                        </button>
                      </Popconfirm>
                    </div>
                  </div>
                );
              })}
            </div>
          </aside>
          <article className="reader">
            <header>
              <div className="reader-chapter-title">
                <span>{String(selectedChapter?.number ?? 0).padStart(2, "0")}</span>
                <h2>{selectedChapter?.title}</h2>
              </div>
              <div className="button-group">
                <button
                  className="secondary-button icon-button"
                  type="button"
                  disabled={!selectedChapter || chapterSaving}
                  onClick={() => selectedChapter && editChapter(selectedChapter)}
                  aria-label="编辑当前章节"
                  title="编辑章节"
                >
                  <Edit3 size={14} />
                </button>
                <Popconfirm
                  title="删除当前章节"
                  description="删除后将保存为新的原文版本。"
                  okText="删除"
                  cancelText="取消"
                  okButtonProps={{ danger: true }}
                  disabled={!selectedChapter || (activeSource?.chapters.length ?? 0) <= 1 || chapterSaving}
                  onConfirm={() => selectedChapter && void deleteChapter(selectedChapter.id)}
                >
                  <button
                    className="secondary-button icon-button"
                    type="button"
                    disabled={!selectedChapter || (activeSource?.chapters.length ?? 0) <= 1 || chapterSaving}
                    aria-label="删除当前章节"
                    title={(activeSource?.chapters.length ?? 0) <= 1 ? "至少保留一个章节" : "删除章节"}
                  >
                    <Trash2 size={14} />
                  </button>
                </Popconfirm>
                <button
                  className="secondary-button icon-button"
                  type="button"
                  disabled={!selectedChapter || (working !== null && working !== "analysis")}
                  onClick={() => void requestAnalysis()}
                  aria-label={working === "analysis" ? "取消本章分析" : "分析本章"}
                  title={working === "analysis" ? "取消本章分析" : "分析本章"}
                >
                  {working === "analysis" ? <span className="spinner" /> : <Sparkles size={14} />}
                </button>
                {activeSource && <VersionPicker compact projectId={projectId} assetId={activeSource.assetId} label="原文版本" />}
              </div>
            </header>
            <div className="source-copy">
              {selectedChapter?.content.split(/\n\s*\n/).map((paragraph, index) => (
                <p key={`${selectedChapter.id}-${index}`}>{paragraph}</p>
              ))}
            </div>
          </article>
        </div>
      ) : workspaceView === "analysis" ? (
        <StoryMaterialWorkspace
          source={activeSource}
          analysis={analysis}
        />
      ) : (
        <AdaptationScriptWorkspace
          key={script?.assetId ?? activeSource?.id}
          projectId={projectId}
          source={activeSource}
          analysis={analysis}
          script={script}
          plannedEpisodeCount={projectSettings?.plannedEpisodeCount}
          working={working}
          onGenerate={requestScript}
          onAppend={appendScriptEpisode}
          onUpdate={updateScriptEpisode}
          onRegenerate={regenerateScriptEpisode}
          onDelete={deleteScriptEpisode}
          onClear={clearScriptEpisodes}
          onGenerateProductionScript={generateEpisodeProductionScript}
        />
      )}
      {importOpen && (
        <div className="modal-backdrop">
          <form className="dialog source-import-dialog" onSubmit={submitImport}>
            <span className="eyebrow">{importMode === "append" ? `更新 ${activeSource?.title}` : "导入参考源"}</span>
            <h2>{importMode === "append" ? "追加原文章节" : importFileName ? "确认文本资料" : "粘贴原文资料"}</h2>
            <p>{importMode === "append" ? `保存后生成原文 v${(activeSource?.version ?? 0) + 1}；既有剧本不会自动变化。` : "导入只创建原文资料和章节，不会创建生产剧集。"}</p>
            {importMode === "create" && (
              <>
                <label>
                  <span>资料名称</span>
                  <input autoFocus required maxLength={200} value={importTitle} onChange={(event) => setImportTitle(event.target.value)} placeholder="例如：三个火枪手原著" />
                </label>
                <label>
                  <span>用途说明（可选）</span>
                  <input maxLength={2000} value={importDescription} onChange={(event) => setImportDescription(event.target.value)} placeholder="例如：角色关系与关键事件参考" />
                </label>
              </>
            )}
            <label>
              <span>{importMode === "append" ? "新增章节内容" : "原文内容"}</span>
              <textarea required value={importContent} onChange={(event) => setImportContent(event.target.value)} placeholder="使用 Markdown 标题或“第一章”一类标题可自动拆分章节" />
            </label>
            {error && <div className="settings-error">{error}</div>}
            <div>
              <button className="secondary-button" type="button" onClick={() => setImportOpen(false)} disabled={importing}>取消</button>
              <button className="primary-button" type="submit" disabled={importing}>
                <Upload size={13} />{importing ? "保存中" : importMode === "append" ? "保存新版本" : "导入资料"}
              </button>
            </div>
          </form>
        </div>
      )}
      {chapterEditor && (
        <div className="modal-backdrop" onMouseDown={() => !chapterSaving && setChapterEditor(null)}>
          <form className="dialog chapter-editor-dialog" onMouseDown={(event) => event.stopPropagation()} onSubmit={saveChapter}>
            <span className="eyebrow">原文章节</span>
            <h2>编辑章节</h2>
            <p>保存后生成原文 v{(activeSource?.version ?? 0) + 1}，已有分析会标记为需要更新。</p>
            <label>
              <span>章节标题</span>
              <input autoFocus required maxLength={300} value={chapterEditor.title} onChange={(event) => setChapterEditor({ ...chapterEditor, title: event.target.value })} />
            </label>
            <label>
              <span>章节正文</span>
              <textarea required value={chapterEditor.content} onChange={(event) => setChapterEditor({ ...chapterEditor, content: event.target.value })} />
            </label>
            {error && <div className="settings-error">{error}</div>}
            <div>
              <button className="secondary-button" type="button" disabled={chapterSaving} onClick={() => setChapterEditor(null)}>取消</button>
              <button className="primary-button" type="submit" disabled={chapterSaving}>
                <Save size={13} />{chapterSaving ? "保存中" : "保存新版本"}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

function StoryMaterialWorkspace({
  source,
  analysis,
}: {
  source?: ProjectSource;
  analysis: StoryMaterialAnalysis | null;
}) {
  if (!analysis) {
    return (
      <div className="source-empty-state development-empty-state">
        <span className="eyebrow">编剧素材准备</span>
        <h2>尚无章节分析</h2>
        <p>{source?.chapterCount ?? 0} 个章节均未生成素材记录。</p>
      </div>
    );
  }

  return (
    <div className="development-workspace material-development-workspace">
      {analysis.isStale && <div className="development-warning">{analysis.staleReason} 既有剧本不会自动变化。</div>}
      <section className="relation-graph-section">
        <div className="relation-graph-heading">
          <strong>素材关系网</strong>
          <div className="relation-graph-legend">
            <span className="character">人物 {analysis.characters.length}</span>
            <span className="location">场景 {analysis.locations.length}</span>
            <span className="beat">情节 {analysis.plotBeats.length}</span>
          </div>
        </div>
        <RelationGraph
          key={analysis.assetId}
          characters={analysis.characters}
          locations={analysis.locations}
          plotBeats={analysis.plotBeats}
          relations={analysis.relations}
        />
      </section>
    </div>
  );
}

function AdaptationSettingsDialog({
  selectedMode,
  sourceChapterCount,
  plannedEpisodeCount,
  modeChanged,
  saveDisabled,
  onModeChange,
  onClose,
  onSave,
}: {
  selectedMode: AdaptationScript["mode"];
  sourceChapterCount: number;
  plannedEpisodeCount?: number;
  modeChanged: boolean;
  saveDisabled: boolean;
  onModeChange: (mode: AdaptationScript["mode"]) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const hasFixedEpisodeCount = plannedEpisodeCount !== undefined && plannedEpisodeCount >= 1;
  const outcome = selectedMode === "source-chapters"
    ? `将沿用现有 ${sourceChapterCount} 个原文章节，一章对应一集。`
    : `将由大纲编排助手按单集时长重新组织章节，首批最多生成 6 集${hasFixedEpisodeCount ? `，项目计划共 ${plannedEpisodeCount} 集` : ""}。`;

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div
        className="dialog adaptation-settings-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="adaptation-settings-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header className="adaptation-settings-header">
          <span className="adaptation-settings-icon"><Settings size={17} /></span>
          <div className="adaptation-settings-identity">
            <span className="eyebrow">改编方案 / 基础设置</span>
            <h2 id="adaptation-settings-title">章节编排设置</h2>
            <p>决定原文进入剧集规划时采用的章节结构。</p>
          </div>
          <button
            className="secondary-button icon-button"
            type="button"
            aria-label="关闭章节编排设置"
            title="关闭"
            onClick={onClose}
          >
            <X size={15} />
          </button>
        </header>
        <section className="adaptation-settings-body">
          <div className="adaptation-mode-control">
            <div>
              <strong>重新排版章节</strong>
              <p>开启后按单集时长重新组织章节；关闭后直接沿用原文章节。</p>
            </div>
            <Switch
              checked={selectedMode === "rearranged"}
              aria-label="重新排版章节"
              onChange={(checked) => onModeChange(checked ? "rearranged" : "source-chapters")}
            />
          </div>
          <div className={`adaptation-mode-outcome ${modeChanged ? "warning" : ""}`}>
            {modeChanged ? <CircleAlert size={15} /> : <Check size={15} />}
            <div>
              <span>{modeChanged ? "将创建新版本" : "保存结果"}</span>
              <p>{modeChanged
                ? "保存后会创建新的改编方案版本；已有正式剧本不会删除，原方案仍可恢复。"
                : outcome}</p>
            </div>
          </div>
        </section>
        <footer className="adaptation-settings-footer">
          <button className="secondary-button" type="button" onClick={onClose}>取消</button>
          <button className="primary-button" type="button" disabled={saveDisabled} onClick={onSave}>
            <Save size={13} />保存设置
          </button>
        </footer>
      </div>
    </div>
  );
}

function AdaptationScriptWorkspace({
  projectId,
  source,
  analysis,
  script,
  plannedEpisodeCount,
  working,
  onGenerate,
  onAppend,
  onUpdate,
  onRegenerate,
  onDelete,
  onClear,
  onGenerateProductionScript,
}: {
  projectId: string;
  source?: ProjectSource;
  analysis: StoryMaterialAnalysis | null;
  script: AdaptationScript | null;
  plannedEpisodeCount?: number;
  working: "analysis" | "script" | "append" | "update" | "regenerate" | "delete" | "clear" | "confirm" | null;
  onGenerate: (
    mode: AdaptationScript["mode"],
    desiredEpisodeCount?: number,
    instruction?: string,
  ) => Promise<void>;
  onAppend: (count: number, instruction?: string) => Promise<void>;
  onUpdate: (
    episodeNumber: number,
    input: { title: string; logline: string; sceneSummaries: string[] },
  ) => Promise<boolean>;
  onRegenerate: (episodeNumber: number, instruction: string) => Promise<boolean>;
  onDelete: (episodeNumber: number) => Promise<boolean>;
  onClear: () => Promise<boolean>;
  onGenerateProductionScript: (episodeNumber: number) => Promise<boolean>;
}) {
  const [activeEpisodeNumber, setActiveEpisodeNumber] = useState(1);
  const [editEpisodeNumber, setEditEpisodeNumber] = useState<number | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [editLogline, setEditLogline] = useState("");
  const [editSceneSummaries, setEditSceneSummaries] = useState<string[]>([]);
  const [regenerateEpisodeNumber, setRegenerateEpisodeNumber] = useState<number | null>(null);
  const [regenerateInstruction, setRegenerateInstruction] = useState("");
  const [appendOpen, setAppendOpen] = useState(false);
  const [appendCount, setAppendCount] = useState(1);
  const [appendInstruction, setAppendInstruction] = useState("");
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [settingsConfirmation, setSettingsConfirmation] = useState(false);
  const [selectedMode, setSelectedMode] = useState<AdaptationScript["mode"]>(
    script?.mode ?? "rearranged",
  );
  const hasFixedEpisodeCount = plannedEpisodeCount !== undefined && plannedEpisodeCount >= 1;
  const initialBatchCount = hasFixedEpisodeCount
    ? Math.min(plannedEpisodeCount, 6)
    : undefined;
  const maxAppendCount = 6;

  if (!script) {
    return (
      <div className="development-workspace script-draft-workspace adaptation-setup-workspace">
        <div className="adaptation-mode-toolbar">
          <div className="adaptation-mode-summary">
            <span>未设置</span>
            <button
              className="secondary-button icon-button"
              type="button"
              aria-label="编辑章节编排设置"
              title="编辑章节编排设置"
              onClick={() => setSettingsOpen(true)}
            >
              <Settings size={13} />
            </button>
          </div>
        </div>
        {settingsOpen && (
          <AdaptationSettingsDialog
            selectedMode={selectedMode}
            sourceChapterCount={source?.chapterCount ?? 0}
            plannedEpisodeCount={plannedEpisodeCount}
            modeChanged={false}
            saveDisabled={working !== null || (selectedMode === "rearranged" && (!analysis || analysis.isStale))}
            onModeChange={setSelectedMode}
            onClose={() => setSettingsOpen(false)}
            onSave={() => setSettingsConfirmation(true)}
          />
        )}
        {settingsConfirmation && (
          <div className="modal-backdrop adaptation-settings-confirmation" onMouseDown={() => setSettingsConfirmation(false)}>
            <div className="dialog" role="alertdialog" aria-modal="true" aria-label="确认保存章节编排设置" onMouseDown={(event) => event.stopPropagation()}>
              <span className="eyebrow">危险操作</span>
              <h2>保存后会生成当前方案</h2>
              <p>系统会按所选编排方式生成第一个改编方案版本。后续可修改、删除或从版本记录恢复。</p>
              <div>
                <button className="secondary-button" type="button" onClick={() => setSettingsConfirmation(false)}>取消</button>
                <button
                  className="primary-button danger-confirm-button"
                  type="button"
                  disabled={working !== null}
                  onClick={async () => {
                    await onGenerate(
                      selectedMode,
                      selectedMode === "rearranged" ? initialBatchCount : undefined,
                      "保存章节编排基础设置，并生成首个方案版本。",
                    );
                    setSettingsConfirmation(false);
                    setSettingsOpen(false);
                  }}
                >
                  <CircleAlert size={13} />{working === "script" ? "保存中" : "确认保存并生成"}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  }

  const activeEpisode = script.episodes.find((episode) => episode.proposalNumber === activeEpisodeNumber)
    ?? script.episodes[0];
  const activeEpisodeHooks = activeEpisode ? [
    ...(activeEpisode.smallHooks ?? []).map((hook, index, items) => ({
      hook,
      tone: "small" as const,
      position: (index + 1) / (items.length + 1),
    })),
    ...(activeEpisode.bigHooks ?? []).map((hook, index, items) => ({
      hook,
      tone: "big" as const,
      position: (index + 1) / (items.length + 1),
    })),
  ]
    .sort((left, right) => left.position - right.position || left.tone.localeCompare(right.tone))
    .map((item) => ({
      ...item,
      sceneIndex: Math.round(item.position * Math.max(0, activeEpisode.scenes.length - 1)),
    })) : [];
  const modeDirty = selectedMode !== script.mode;

  return (
    <div className={`development-workspace script-draft-workspace ${script.mode === "source-chapters" ? "source-chapter-adaptation" : ""}`}>
      <div className="adaptation-mode-toolbar">
        <div className="adaptation-mode-summary">
          <span>{script.mode === "source-chapters" ? "按原章节改编" : "重新排版章节"}</span>
          <button
            className="secondary-button icon-button"
            type="button"
            disabled={working !== null}
            aria-label="编辑章节编排设置"
            title="编辑章节编排设置"
            onClick={() => {
              setSelectedMode(script.mode);
              setSettingsOpen(true);
            }}
          >
            <Settings size={13} />
          </button>
        </div>
        <div className="button-group">
          <VersionPicker compact projectId={projectId} assetId={script.assetId} label="改编方案版本" />
          <button
            className="secondary-button icon-button"
            type="button"
            disabled={working !== null || (script.mode === "rearranged" && (!analysis || analysis.isStale))}
            aria-label="按当前故事更新方案"
            title="按当前故事更新方案"
            onClick={() => void onGenerate(
              script.mode,
              script.mode === "rearranged" ? initialBatchCount : undefined,
              "基于现有方案和当前故事生成新版本。",
            )}
          >
            <RefreshCw size={13} />
          </button>
          {script.episodes.length > 0 && (
            <Popconfirm
              title="清空全部章节"
              description="当前方案会保存为空版本，之后仍可继续生成新章节。"
              okText="清空"
              cancelText="取消"
              okButtonProps={{ danger: true }}
              disabled={working !== null}
              onConfirm={async () => {
                if (await onClear()) setActiveEpisodeNumber(1);
              }}
            >
              <button
                className="secondary-button icon-button danger-button"
                type="button"
                disabled={working !== null}
                aria-label="清空全部章节"
                title="清空全部章节"
              >
                <Trash2 size={13} />
              </button>
            </Popconfirm>
          )}
          {script.mode === "rearranged" && (
            <button
              className="secondary-button"
              type="button"
              disabled={working !== null || !analysis || analysis.isStale}
              onClick={() => {
                setAppendCount(1);
                setAppendInstruction("");
                setAppendOpen(true);
              }}
            >
              <Plus size={13} />生成新章节
            </button>
          )}
        </div>
      </div>
      {script.hasNewerSourceVersion && <div className="development-warning">原文已有新版本，但此草案仍锁定原文 v{script.sourceVersion}，内容未被自动修改。</div>}
      <div className="script-draft-layout">
        <aside className="script-episode-directory">
          <header><strong>剧集目录</strong><span>{script.episodes.length}</span></header>
          <div>
            {script.episodes.map((episode) => (
              <button
                className={episode.proposalNumber === activeEpisode?.proposalNumber ? "active" : ""}
                key={episode.proposalNumber}
                type="button"
                onClick={() => setActiveEpisodeNumber(episode.proposalNumber)}
              >
                <span>E{String(episode.proposalNumber).padStart(2, "0")}</span>
                <strong>{episode.title}</strong>
                <small>{script.mode === "source-chapters"
                  ? `原文第 ${episode.sourceChapterNumbers.join("、")} 章`
                  : `${episode.scenes.length} 个节点 · ${episode.targetSeconds}s`}</small>
              </button>
            ))}
          </div>
        </aside>
        <section className="script-draft-main">
          {!activeEpisode && (
            <div className="adaptation-empty-plan">
              <h3>方案中还没有章节</h3>
              <p>{script.mode === "rearranged"
                ? "可以继续让大纲编排助手生成新章节。"
                : "可以按当前基础设置重新载入原文章节。"}</p>
              {script.mode === "rearranged" && (
                <button
                  className="primary-button"
                  type="button"
                  disabled={working !== null || !analysis || analysis.isStale}
                  onClick={() => {
                    setAppendCount(1);
                    setAppendInstruction("");
                    setAppendOpen(true);
                  }}
                >
                  <Plus size={13} />生成新章节
                </button>
              )}
            </div>
          )}
          {activeEpisode && (
            <div className="script-proposal-list single-episode">
              <section>
                <header>
                  <span>E{String(activeEpisode.proposalNumber).padStart(2, "0")}</span>
                  <div><h3>{activeEpisode.title}</h3></div>
                  <div className="episode-draft-actions">
                    {script.mode === "rearranged" && <small>{activeEpisode.targetSeconds}s</small>}
                    <button
                      className={`${script.productionEpisodeMap?.[activeEpisode.proposalNumber] ? "secondary-button" : "primary-button"} icon-button production-script-button`}
                      type="button"
                      disabled={working !== null}
                      aria-label={working === "confirm"
                        ? "正在生成正式剧本"
                        : script.productionEpisodeMap?.[activeEpisode.proposalNumber]
                          ? "查看正式剧本"
                          : "生成正式剧本"}
                      title={script.productionEpisodeMap?.[activeEpisode.proposalNumber]
                        ? "查看正式剧本"
                        : "生成正式剧本"}
                      onClick={() => void onGenerateProductionScript(activeEpisode.proposalNumber)}
                    >
                      {script.productionEpisodeMap?.[activeEpisode.proposalNumber] ? <Eye size={13} /> : <Check size={13} />}
                    </button>
                    <button
                      className="secondary-button icon-button"
                      type="button"
                      disabled={working !== null}
                      aria-label="手工修改本章"
                      title="手工修改本章"
                      onClick={() => {
                        setEditTitle(activeEpisode.title);
                        setEditLogline(activeEpisode.logline);
                        setEditSceneSummaries(activeEpisode.scenes.map((scene) => scene.summary));
                        setEditEpisodeNumber(activeEpisode.proposalNumber);
                      }}
                    >
                      <Edit3 size={14} />
                    </button>
                    {script.mode === "rearranged" && (
                      <button
                        className="secondary-button icon-button"
                        type="button"
                        disabled={working !== null}
                        aria-label="按意见重写本集"
                        title="按意见重写本集"
                        onClick={() => {
                          setRegenerateInstruction("");
                          setRegenerateEpisodeNumber(activeEpisode.proposalNumber);
                        }}
                      >
                        <RefreshCw size={14} />
                      </button>
                    )}
                    <Popconfirm
                      title={`删除 E${String(activeEpisode.proposalNumber).padStart(2, "0")}`}
                      description="删除后其余章节会自动重新编号，并保存为新版本。"
                      okText="删除"
                      cancelText="取消"
                      okButtonProps={{ danger: true }}
                      disabled={working !== null}
                      onConfirm={async () => {
                        if (await onDelete(activeEpisode.proposalNumber)) {
                          setActiveEpisodeNumber(Math.min(
                            activeEpisode.proposalNumber,
                            script.episodes.length - 1,
                          ));
                        }
                      }}
                    >
                      <button
                        className="secondary-button icon-button danger-button"
                        type="button"
                        disabled={working !== null}
                        aria-label="删除本章"
                        title="删除本章"
                      >
                        <Trash2 size={14} />
                      </button>
                    </Popconfirm>
                  </div>
                </header>
                <div>
                  {activeEpisode.scenes.map((scene, sceneIndex) => (
                    <Fragment key={scene.sceneNumber}>
                      <article>
                        <b>{String(scene.sceneNumber).padStart(2, "0")}</b>
                        <div><strong>{scene.heading}</strong><p>{scene.summary}</p>{script.mode === "rearranged" && <small>主线作用：{scene.storyFunction}</small>}</div>
                        {script.mode === "rearranged" && <div><span>{scene.characters.join(" · ")}</span><small>{scene.props.length ? `道具线索：${scene.props.join(" · ")}` : "无关键道具"}</small></div>}
                      </article>
                      {script.mode === "rearranged" && activeEpisodeHooks
                        .filter((item) => item.sceneIndex === sceneIndex)
                        .map((item, hookIndex) => (
                          <div className={`episode-hook-marker ${item.tone}`} key={`${item.tone}-${hookIndex}-${item.hook}`}>
                            <span><i /><b>{item.tone === "small" ? "小爆点" : "大爆点"}</b></span>
                            <p>{item.hook}</p>
                          </div>
                        ))}
                    </Fragment>
                  ))}
                </div>
              </section>
            </div>
          )}
        </section>
      </div>
      {settingsOpen && script && (
        <AdaptationSettingsDialog
          selectedMode={selectedMode}
          sourceChapterCount={source?.chapterCount ?? 0}
          plannedEpisodeCount={plannedEpisodeCount}
          modeChanged={modeDirty}
          saveDisabled={!modeDirty || working !== null || (selectedMode === "rearranged" && (!analysis || analysis.isStale))}
          onModeChange={setSelectedMode}
          onClose={() => setSettingsOpen(false)}
          onSave={() => setSettingsConfirmation(true)}
        />
      )}
      {settingsConfirmation && script && (
        <div className="modal-backdrop adaptation-settings-confirmation" onMouseDown={() => setSettingsConfirmation(false)}>
          <div className="dialog" role="alertdialog" aria-modal="true" aria-label="确认保存章节编排设置" onMouseDown={(event) => event.stopPropagation()}>
            <span className="eyebrow">危险操作</span>
            <h2>保存后会重建当前方案</h2>
            <p>切换编排方式会生成新的改编方案版本，并解除既有正式剧本与新方案的章节关联；已有正式剧本不会删除。当前方案可从版本记录恢复。</p>
            <div>
              <button className="secondary-button" type="button" onClick={() => setSettingsConfirmation(false)}>取消</button>
              <button
                className="primary-button danger-confirm-button"
                type="button"
                disabled={working !== null}
                onClick={async () => {
                  await onGenerate(
                    selectedMode,
                    selectedMode === "rearranged" ? initialBatchCount : undefined,
                    "保存章节编排基础设置，并按新设置生成方案。",
                  );
                  setSettingsConfirmation(false);
                  setSettingsOpen(false);
                }}
              >
                <CircleAlert size={13} />{working === "script" ? "保存中" : "确认保存并重建"}
              </button>
            </div>
          </div>
        </div>
      )}
      {editEpisodeNumber !== null && (
        <div className="modal-backdrop" onMouseDown={() => setEditEpisodeNumber(null)}>
          <form
            className="dialog episode-edit-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={async (event) => {
              event.preventDefault();
              if (await onUpdate(editEpisodeNumber, {
                title: editTitle.trim(),
                logline: editLogline.trim(),
                sceneSummaries: editSceneSummaries.map((item) => item.trim()),
              })) {
                setEditEpisodeNumber(null);
              }
            }}
          >
            <header className="episode-edit-dialog-header">
              <div><span className="eyebrow">E{String(editEpisodeNumber).padStart(2, "0")} / 手工修改</span><h2>修改章节方案</h2></div>
              <button className="text-button icon-button" type="button" aria-label="关闭章节编辑" onClick={() => setEditEpisodeNumber(null)}><X size={15} /></button>
            </header>
            <div className="episode-edit-dialog-body">
              <label>
                <span>章节标题</span>
                <input autoFocus required value={editTitle} onChange={(event) => setEditTitle(event.target.value)} />
              </label>
              <label>
                <span>章节概要</span>
                <textarea required value={editLogline} onChange={(event) => setEditLogline(event.target.value)} />
              </label>
              {editSceneSummaries.map((summary, index) => (
                <label key={index}>
                  <span>剧情节点 {String(index + 1).padStart(2, "0")}</span>
                  <textarea
                    required
                    value={summary}
                    onChange={(event) => setEditSceneSummaries((items) => items.map((item, itemIndex) =>
                      itemIndex === index ? event.target.value : item))}
                  />
                </label>
              ))}
            </div>
            <footer className="episode-edit-dialog-footer">
              <button className="secondary-button" type="button" disabled={working === "update"} onClick={() => setEditEpisodeNumber(null)}>取消</button>
              <button
                className="primary-button"
                type="submit"
                disabled={working === "update" || !editTitle.trim() || !editLogline.trim() || editSceneSummaries.some((item) => !item.trim())}
              >
                <Save size={13} />{working === "update" ? "保存中" : "保存修改"}
              </button>
            </footer>
          </form>
        </div>
      )}
      {appendOpen && (
        <div className="modal-backdrop" onMouseDown={() => setAppendOpen(false)}>
          <form
            className="dialog episode-regenerate-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={async (event) => {
              event.preventDefault();
              await onAppend(appendCount, appendInstruction.trim() || undefined);
              setAppendOpen(false);
            }}
          >
            <span className="eyebrow">继续编排 / 当前 {script.episodes.length} 集</span>
            <h2>生成下一批剧集大纲</h2>
            <p>助手会读取原文、素材图谱与当前方案，从下一集继续编排，不改写已有剧集。每批最多生成 6 集。</p>
            <label>
              <span>本批集数</span>
              <InputNumber
                autoFocus
                min={1}
                max={maxAppendCount}
                precision={0}
                value={appendCount}
                onChange={(value) => setAppendCount(value ?? 1)}
              />
            </label>
            <label>
              <span>补充要求（可选）</span>
              <textarea
                value={appendInstruction}
                onChange={(event) => setAppendInstruction(event.target.value)}
                placeholder="例如：下一批加快推进，突出配角背叛，并在最后一集留下身份悬念。"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" disabled={working === "append"} onClick={() => setAppendOpen(false)}>取消</button>
              <button className="primary-button" type="submit" disabled={working === "append"}>
                <Plus size={13} />{working === "append" ? "生成中" : `生成 ${appendCount} 集`}
              </button>
            </div>
          </form>
        </div>
      )}
      {regenerateEpisodeNumber !== null && (
        <div className="modal-backdrop">
          <form
            className="dialog episode-regenerate-dialog"
            onSubmit={async (event) => {
              event.preventDefault();
              if (!regenerateInstruction.trim()) return;
              if (await onRegenerate(regenerateEpisodeNumber, regenerateInstruction.trim())) {
                setRegenerateEpisodeNumber(null);
              }
            }}
          >
            <span className="eyebrow">E{String(regenerateEpisodeNumber).padStart(2, "0")} / 重新生成</span>
            <h2>填写本集改编要求</h2>
            <p>原著只作为人物与事件素材；本集会按要求重新编排，其他剧集保持不变。已确认版本会保留，并另存为新草案。</p>
            <label>
              <span>改编要求</span>
              <textarea
                autoFocus
                required
                value={regenerateInstruction}
                onChange={(event) => setRegenerateInstruction(event.target.value)}
                placeholder="例如：强化主角主动选择，以误会冲突推进，结尾增加身份反转；控制在 100 秒内。"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" disabled={working === "regenerate"} onClick={() => setRegenerateEpisodeNumber(null)}>取消</button>
              <button className="primary-button" type="submit" disabled={working === "regenerate" || !regenerateInstruction.trim()}>
                <RefreshCw size={13} />{working === "regenerate" ? "生成中" : "按要求重新生成"}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

export function OutlinePage() {
  const [view, setView] = useState<"outline" | "mapping">("mapping");
  return (
    <div className="page">
      <PageTitle
        eyebrow="剧本 / 改编方案"
        title="改编大纲"
        description="从原文资料的任意章节取材，建立非一一对应的生产剧集改编关系"
        action={<button className="primary-button">创建新版本</button>}
      />
      <div className="view-tabs">
        <button
          className={view === "outline" ? "active" : ""}
          onClick={() => setView("outline")}
        >
          主线与角色弧线
        </button>
        <button
          className={view === "mapping" ? "active" : ""}
          onClick={() => setView("mapping")}
        >
          分集映射
        </button>
      </div>
      {view === "mapping" ? (
        <div className="mapping-board">
          <header>
            <span>原文资料 / 章节</span>
            <span>改编关系</span>
            <span>生产剧本集</span>
          </header>
          {[
            ["原文 E01 · 初遇 / 文件", "拆分 + 重排", "E01 · 失控的早晨"],
            ["原文 E01 · 文件\n原文 E02 · 追查", "合并", "E02 · 追查与反转"],
            ["原文 E03 · 真相 / 回收", "保留 + 原创结尾", "E03 · 真相回收"],
          ].map((row, index) => (
            <div className="mapping-row" key={row[2]}>
              <div>
                <span className="source-badge">SOURCE</span>
                <strong>{row[0]}</strong>
                <small>{index === 1 ? "2 个来源片段" : "已锁定证据"}</small>
              </div>
              <div className="mapping-link">
                <i />
                <span>{row[1]}</span>
                <i />
              </div>
              <div>
                <span className="episode-badge">EPISODE</span>
                <strong>{row[2]}</strong>
                <small>目标 90–110 秒</small>
              </div>
            </div>
          ))}
          <button className="add-mapping">
            <Plus size={14} />
            添加映射
          </button>
        </div>
      ) : (
        <div className="editor-surface prose-editor">
          <h2>核心命题</h2>
          <p>一个试图守住规则的人，被迫在真相与安全之间做出选择。</p>
          <h2>主线</h2>
          <p>林墨带着关键文件进入天桥食堂，在层层误导中查明匿名警告的来源。</p>
        </div>
      )}
    </div>
  );
}

function ScriptStageTabs({
  projectId,
  active,
  sourceId,
  productionEpisodeId,
}: {
  projectId: string;
  active: "adaptation" | "production";
  sourceId?: string;
  productionEpisodeId?: string;
}) {
  const navigate = useNavigate();
  return (
    <Tabs
      className="script-view-tabs"
      size="small"
      activeKey={active}
      items={[
        { key: "adaptation", label: "改编方案", disabled: !sourceId },
        { key: "production", label: "正式剧本", disabled: !productionEpisodeId },
      ]}
      onChange={(key) => {
        if (key === "adaptation" && sourceId) {
          navigate(getScriptWorkspacePath(projectId, sourceId, "adaptation"));
        } else if (key === "production" && productionEpisodeId) {
          navigate(getScriptWorkspacePath(projectId, productionEpisodeId, "production"));
        }
      }}
    />
  );
}

export function ScriptLandingPage() {
  const { projectId = "" } = useParams();
  const [episodes, setEpisodes] = useState<ProductionEpisodeRecord[] | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    listProductionEpisodes(projectId, controller.signal)
      .then(setEpisodes)
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "生产剧集加载失败。");
      });
    return () => controller.abort();
  }, [projectId]);

  if (episodes?.[0]) {
    return <Navigate to={getScriptWorkspacePath(projectId, episodes[0].id, "production")} replace />;
  }

  return (
    <div className="page full-height-page">
      <ScriptStageTabs projectId={projectId} active="production" />
      <div className="source-empty-state development-empty-state">
        <span className="eyebrow">剧本 / 正式资产</span>
        <h1>{error || episodes === null ? "正在读取生产剧集" : "尚未创建正式剧本"}</h1>
        <p>{error || "先在剧本中完成并固化改编方案。固化后会创建独立生产集和对应的正式剧本资产。"}</p>
        {!error && episodes !== null && (
          <NavLink className="primary-button" to={`/projects/${projectId}/script/adaptation`}>
            进入改编方案
          </NavLink>
        )}
      </div>
    </div>
  );
}

const assetKinds: Record<
  string,
  { kind: VisualAssetKind; label: string; singular: string }
> = {
  characters: { kind: "character", label: "人物", singular: "人物" },
  scenes: { kind: "scene", label: "场景", singular: "场景" },
  props: { kind: "prop", label: "道具", singular: "道具" },
};

const assetReferenceSpecs: Record<VisualAssetKind, { title: string; layout: string }> = {
  character: {
    title: "人物四视图设定稿",
    layout: "左侧正面全身 · 右上背面与侧面 · 右下头部特写",
  },
  scene: {
    title: "场景三视图设定稿",
    layout: "上方正面视角 · 左下反面视角 · 右下俯视图",
  },
  prop: {
    title: "道具正面设定稿",
    layout: "单一道具 · 正面视角 · 无其他视图",
  },
};

type AssetEditorState = {
  name: string;
  summary: string;
  visualDescription: string;
  mustKeep: string;
  avoid: string;
  storyReferences: string;
};

const emptyAssetEditor: AssetEditorState = {
  name: "",
  summary: "",
  visualDescription: "",
  mustKeep: "",
  avoid: "",
  storyReferences: "",
};

function toAssetEditor(asset: VisualAsset): AssetEditorState {
  return {
    name: asset.name,
    summary: asset.summary,
    visualDescription: asset.visualDescription,
    mustKeep: asset.mustKeep.join("\n"),
    avoid: asset.avoid.join("\n"),
    storyReferences: asset.storyReferences.join("\n"),
  };
}

function splitAssetLines(value: string): string[] {
  return value
    .split(/\r?\n|，|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function CharacterVoicePanel({ projectId, character }: { projectId: string; character: VisualAsset }) {
  const [profile, setProfile] = useState<VoiceProfile | null>(null);
  const [editor, setEditor] = useState<SaveVoiceProfileInput>({
    name: `${character.name}标准音色`,
    designPrompt: "",
    sampleText: "",
    language: "Chinese",
    seed: null,
  });
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState<"save" | "generate" | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getVoiceProfile(projectId, character.resourceId, controller.signal)
      .then((loaded) => {
        setProfile(loaded);
        setEditor(loaded ? {
          name: loaded.name,
          designPrompt: loaded.designPrompt,
          sampleText: loaded.sampleText,
          language: loaded.language,
          seed: loaded.seed,
        } : {
          name: `${character.name}标准音色`,
          designPrompt: "",
          sampleText: "",
          language: "Chinese",
          seed: null,
        });
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "音色配置加载失败。");
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [projectId, character.resourceId, character.name]);

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setWorking("save");
    setError("");
    try {
      const saved = await saveVoiceProfile(projectId, character.resourceId, editor);
      setProfile(saved);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "音色配置保存失败。");
    } finally {
      setWorking(null);
    }
  };

  const generate = async () => {
    setWorking("generate");
    setError("");
    try {
      setProfile(await generateVoiceReference(projectId, character.resourceId));
    } catch (generateError) {
      setError(generateError instanceof Error ? generateError.message : "参考音生成失败。");
    } finally {
      setWorking(null);
    }
  };

  return (
    <section className="character-voice-workbench">
      <header>
        <div>
          <span className="eyebrow">VOICE DESIGN</span>
          <strong>角色音色</strong>
          <p>{profile ? `音色配置 v${profile.version}` : loading ? "正在读取配置" : "尚未建立音色配置"}</p>
          <p>一致性由固定参考音、提示词与种子维持；当前无需训练 LoRA。</p>
        </div>
        {profile && (
          <button className="primary-button" onClick={generate} disabled={working !== null}>
            <AudioLines size={14} />
            {working === "generate" ? "正在生成" : profile.reference ? "重新生成参考音" : "生成参考音"}
          </button>
        )}
      </header>
      {error && <div className="settings-error voice-profile-error">{error}</div>}
      <form className="voice-profile-form" onSubmit={save}>
        <label>
          <span>音色名称</span>
          <input required maxLength={100} value={editor.name} onChange={(event) => setEditor((current) => ({ ...current, name: event.target.value }))} />
        </label>
        <label className="voice-prompt-field">
          <span>音色设计描述</span>
          <textarea required minLength={10} maxLength={2000} rows={4} value={editor.designPrompt} onChange={(event) => setEditor((current) => ({ ...current, designPrompt: event.target.value }))} placeholder="年龄、性别、音高、质感、语速、气质与情绪边界" />
        </label>
        <label className="voice-sample-field">
          <span>试音文本</span>
          <textarea required maxLength={1000} rows={3} value={editor.sampleText} onChange={(event) => setEditor((current) => ({ ...current, sampleText: event.target.value }))} />
        </label>
        <label>
          <span>语言</span>
          <select value={editor.language} onChange={(event) => setEditor((current) => ({ ...current, language: event.target.value }))}>
            <option value="Chinese">中文</option>
            <option value="English">英文</option>
          </select>
        </label>
        <label>
          <span>随机种子</span>
          <input type="number" value={editor.seed ?? ""} onChange={(event) => setEditor((current) => ({ ...current, seed: event.target.value === "" ? null : Number(event.target.value) }))} placeholder="自动" />
        </label>
        <footer>
          <button className="secondary-button" type="submit" disabled={working !== null || loading}>
            <Save size={14} />{working === "save" ? "保存中" : profile ? "保存新版本" : "保存音色配置"}
          </button>
          {profile?.reference && (
            <div className="voice-reference-player">
              <audio controls preload="metadata" src={profile.reference.contentUrl} key={profile.reference.assetId} />
              <span>{profile.reference.model} · {profile.reference.device} · v{profile.reference.version}</span>
            </div>
          )}
        </footer>
      </form>
    </section>
  );
}

function formatAudioSize(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function formatAudioDuration(seconds: number): string {
  if (seconds <= 0) return "读取播放时长";
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${Math.round(seconds % 60).toString().padStart(2, "0")}`;
}

function AssetTabs({
  assetType,
  counts,
}: {
  assetType: string;
  counts?: Partial<Record<"characters" | "scenes" | "props" | "audio", number>>;
}) {
  return (
    <div className="asset-tabs">
      {[
        ["人物", "characters"],
        ["场景", "scenes"],
        ["道具", "props"],
        ["音频", "audio"],
      ].map(([item, path]) => (
        <NavLink
          className={path === assetType ? "active" : ""}
          to={`../${path}`}
          relative="path"
          key={item}
        >
          {item}
          {counts?.[path as keyof typeof counts] !== undefined && (
            <span>{counts[path as keyof typeof counts]}</span>
          )}
        </NavLink>
      ))}
    </div>
  );
}

function AudioAssetsPage({ projectId }: { projectId: string }) {
  const [materials, setMaterials] = useState<AudioMaterial[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadName, setUploadName] = useState("");
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [error, setError] = useState("");
  const visible = materials.filter((item) =>
    item.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  const selected = materials.find((item) => item.assetId === selectedId) ?? visible[0] ?? null;

  useEffect(() => {
    const controller = new AbortController();
    listAudioMaterials(projectId, controller.signal)
      .then((loaded) => {
        setMaterials(loaded);
        setSelectedId((current) => loaded.some((item) => item.assetId === current)
          ? current
          : loaded[0]?.assetId ?? "");
        setError("");
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "音频素材加载失败。");
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [projectId]);

  const submitUpload = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!uploadFile) return;
    setUploading(true);
    setError("");
    try {
      const uploaded = await uploadAudioMaterial(projectId, uploadName, uploadFile);
      setMaterials((current) => [uploaded, ...current]);
      setSelectedId(uploaded.assetId);
      setUploadOpen(false);
      setUploadName("");
      setUploadFile(null);
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : "音频素材上传失败。");
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="page full-height-page asset-bible-page">
      <AssetTabs assetType="audio" counts={{ audio: materials.length }} />
      <div className="asset-bible-workspace">
        <header className="asset-bible-toolbar">
          <div className="asset-bible-context"><strong>音频素材</strong><span>{materials.length}</span></div>
          <div className="asset-bible-search">
            <label><Search size={14} /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="搜索音频" /></label>
          </div>
          <div className="asset-bible-actions">
            <button className="primary-button" onClick={() => setUploadOpen(true)}>
              <Upload size={14} />上传音频
            </button>
          </div>
        </header>
        {error && <div className="settings-error asset-error">{error}</div>}
        <div className="asset-workspace audio-asset-workspace">
          <section className="asset-list-panel">
          {visible.map((material) => (
            <button
              className={selected?.assetId === material.assetId ? "asset-row active" : "asset-row"}
              onClick={() => setSelectedId(material.assetId)}
              key={material.assetId}
            >
              <span className="asset-thumb audio-thumb"><AudioLines size={18} /></span>
              <span className="asset-row-copy"><strong>{material.name}</strong><small>{material.source} · {formatAudioDuration(material.durationSeconds)}</small></span>
              <span className="asset-row-meta"><span className="state-label ready">可用</span><small>v{material.version}</small></span>
            </button>
          ))}
          {!loading && visible.length === 0 && <div className="asset-list-empty"><strong>尚无音频素材</strong><p>上传音频，或在人物资产中生成角色参考音。</p></div>}
          {loading && <div className="asset-list-empty">正在读取音频素材...</div>}
          </section>
          {selected ? (
            <section className="asset-detail audio-asset-detail">
            <header className="asset-detail-header">
              <div className="asset-detail-identity"><span className="eyebrow">AUDIO · {selected.source} · v{selected.version}</span><h2>{selected.name}</h2><p>{selected.fileName}</p></div>
            </header>
            <div className="audio-preview-stage">
              <AudioLines size={28} />
              <audio controls preload="metadata" src={selected.contentUrl} key={selected.assetId} />
            </div>
            <dl className="detail-grid">
              <div><dt>来源</dt><dd>{selected.source}</dd></div>
              <div><dt>格式</dt><dd>{selected.contentType}</dd></div>
              <div><dt>时长</dt><dd>{formatAudioDuration(selected.durationSeconds)}</dd></div>
              <div><dt>文件大小</dt><dd>{formatAudioSize(selected.sizeBytes)}</dd></div>
              <div><dt>用途</dt><dd>{selected.kind === "voice-reference" ? "角色音色基准与后续克隆参考" : "对白、环境声、音乐或制作参考"}</dd></div>
              <div><dt>更新时间</dt><dd>{new Date(selected.updatedAtUtc).toLocaleString()}</dd></div>
            </dl>
            </section>
          ) : (
            <section className="asset-detail asset-detail-empty"><AudioLines size={28} /><h2>尚无音频素材</h2><p>角色参考音生成后也会自动出现在这里。</p></section>
          )}
        </div>
      </div>
      {uploadOpen && (
        <div className="modal-backdrop" onMouseDown={() => setUploadOpen(false)}>
          <form className="dialog audio-upload-dialog" onMouseDown={(event) => event.stopPropagation()} onSubmit={submitUpload}>
            <span className="eyebrow">AUDIO MATERIAL</span><h2>上传音频素材</h2>
            <label><span>名称</span><input required maxLength={100} value={uploadName} onChange={(event) => setUploadName(event.target.value)} placeholder="对白、环境声或音乐名称" /></label>
            <label><span>音频文件</span><input required type="file" accept="audio/wav,audio/mpeg,audio/mp4,audio/ogg,audio/flac,audio/aac,audio/webm" onChange={(event) => setUploadFile(event.target.files?.[0] ?? null)} /></label>
            <p>支持 WAV、MP3、M4A、OGG、FLAC、AAC、WebM，最大 50 MB。</p>
            <div><button type="button" className="secondary-button" onClick={() => setUploadOpen(false)}>取消</button><button type="submit" className="primary-button" disabled={uploading || !uploadFile || !uploadName.trim()}><Upload size={14} />{uploading ? "上传中" : "上传"}</button></div>
          </form>
        </div>
      )}
    </div>
  );
}

export function AssetsPage() {
  const { projectId = "", assetType = "characters" } = useParams();
  return assetType === "audio"
    ? <AudioAssetsPage key={projectId} projectId={projectId} />
    : <VisualAssetsPage projectId={projectId} assetType={assetType} />;
}

function VisualAssetsPage({ projectId, assetType }: { projectId: string; assetType: string }) {
  const kindConfig = assetKinds[assetType] ?? assetKinds.characters;
  const [assets, setAssets] = useState<VisualAsset[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState<"import" | "save" | "prompt" | "image" | "batch-prompt" | "batch-image" | "upload-reference" | null>(null);
  const [error, setError] = useState("");
  const [batchMessage, setBatchMessage] = useState("");
  const [editorOpen, setEditorOpen] = useState(false);
  const [referenceFeedbackOpen, setReferenceFeedbackOpen] = useState(false);
  const [referenceFeedback, setReferenceFeedback] = useState("");
  const [useCurrentReference, setUseCurrentReference] = useState(true);
  const referenceUploadInput = useRef<HTMLInputElement>(null);
  const [editingAsset, setEditingAsset] = useState<VisualAsset | null>(null);
  const [editor, setEditor] = useState<AssetEditorState>(emptyAssetEditor);
  const [promptCopied, setPromptCopied] = useState(false);
  const kindAssets = assets.filter((item) => item.kind === kindConfig.kind);
  const visibleAssets = kindAssets.filter((item) =>
    item.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  const selected = kindAssets.find((item) => item.resourceId === selectedId)
    ?? kindAssets[0]
    ?? null;
  const counts = {
    characters: assets.filter((item) => item.kind === "character").length,
    scenes: assets.filter((item) => item.kind === "scene").length,
    props: assets.filter((item) => item.kind === "prop").length,
  };

  useEffect(() => {
    const controller = new AbortController();
    listVisualAssets(projectId, controller.signal)
      .then((loaded) => {
        setAssets(loaded);
        setError("");
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "资产加载失败。");
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [projectId]);

  const openCreate = () => {
    setEditingAsset(null);
    setEditor(emptyAssetEditor);
    setEditorOpen(true);
  };

  const openEdit = (asset: VisualAsset) => {
    setEditingAsset(asset);
    setEditor(toAssetEditor(asset));
    setEditorOpen(true);
  };

  const importMaterials = async () => {
    setWorking("import");
    setError("");
    try {
      const imported = await importStoryMaterialAssets(projectId);
      setAssets(imported);
      const first = imported.find((item) => item.kind === kindConfig.kind);
      if (first) setSelectedId(first.resourceId);
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "从素材图谱建立资产失败。");
    } finally {
      setWorking(null);
    }
  };

  const saveAsset = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const input: SaveVisualAssetInput = {
      kind: kindConfig.kind,
      name: editor.name,
      summary: editor.summary,
      visualDescription: editor.visualDescription,
      mustKeep: splitAssetLines(editor.mustKeep),
      avoid: splitAssetLines(editor.avoid),
      storyReferences: splitAssetLines(editor.storyReferences),
      sourceAssetId: editingAsset?.sourceAssetId,
    };
    setWorking("save");
    setError("");
    try {
      const saved = editingAsset
        ? await updateVisualAsset(projectId, editingAsset.resourceId, input)
        : await createVisualAsset(projectId, input);
      setAssets((current) => editingAsset
        ? current.map((item) => item.resourceId === saved.resourceId ? saved : item)
        : [...current, saved]);
      setSelectedId(saved.resourceId);
      setEditorOpen(false);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "资产保存失败。");
    } finally {
      setWorking(null);
    }
  };

  const generateReferencePrompt = async (instruction?: string, basedOnCurrent = false) => {
    if (!selected) return;
    setWorking("prompt");
    setError("");
    setBatchMessage("");
    try {
      const referencePrompt = await generateVisualReferencePrompt(
        projectId,
        selected.resourceId,
        instruction,
        basedOnCurrent,
      );
      setAssets((current) => current.map((item) => item.resourceId === selected.resourceId
        ? { ...item, referencePrompt }
        : item));
      setPromptCopied(false);
      setReferenceFeedbackOpen(false);
      setReferenceFeedback("");
    } catch (generateError) {
      setError(generateError instanceof Error ? generateError.message : "提示词生成失败。");
    } finally {
      setWorking(null);
    }
  };

  const generateReferenceImage = async () => {
    if (!selected?.referencePrompt) return;
    setWorking("image");
    setError("");
    setBatchMessage("");
    try {
      const referenceImage = await generateVisualReferenceImage(projectId, selected.resourceId);
      setAssets((current) => current.map((item) => item.resourceId === selected.resourceId
        ? { ...item, referenceImage }
        : item));
    } catch (generateError) {
      setError(generateError instanceof Error ? generateError.message : "参考图生成失败。");
    } finally {
      setWorking(null);
    }
  };

  const generateBatch = async (target: "prompt" | "image") => {
    setWorking(target === "prompt" ? "batch-prompt" : "batch-image");
    setError("");
    setBatchMessage("");
    try {
      const result = target === "prompt"
        ? await generateMissingVisualReferencePrompts(projectId, kindConfig.kind)
        : await generateMissingVisualReferenceImages(projectId, kindConfig.kind);
      const loaded = await listVisualAssets(projectId);
      setAssets(loaded);
      setBatchMessage(`${target === "prompt" ? "提示词" : "图片"}批量完成：生成 ${result.generated}，跳过 ${result.skipped}，失败 ${result.failed}`);
      if (result.errors.length > 0) setError(result.errors.join("；"));
    } catch (batchError) {
      setError(batchError instanceof Error ? batchError.message : "批量生成失败。");
    } finally {
      setWorking(null);
    }
  };

  const uploadReference = async (file: File) => {
    if (!selected) return;
    setWorking("upload-reference");
    setError("");
    try {
      const referenceImage = await uploadVisualReference(projectId, selected.resourceId, file);
      setAssets((current) => current.map((item) => item.resourceId === selected.resourceId
        ? { ...item, referenceImage }
        : item));
      setPromptCopied(false);
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : "参考图上传失败。");
    } finally {
      setWorking(null);
      if (referenceUploadInput.current) referenceUploadInput.current.value = "";
    }
  };

  return (
    <div className="page full-height-page asset-bible-page">
      <AssetTabs assetType={assetType} counts={counts} />
      <div className="asset-bible-workspace">
        <header className="asset-bible-toolbar">
          <div className="asset-bible-context"><strong>{kindConfig.label}资产</strong><span>{kindAssets.length}</span></div>
          <div className="asset-bible-search">
            <label>
              <Search size={14} />
              <input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder={`搜索${kindConfig.label}`}
              />
            </label>
          </div>
          <div className="asset-bible-actions">
            <button className="secondary-button" onClick={() => void generateBatch("prompt")} disabled={working !== null || kindAssets.length === 0}>
              <Sparkles size={14} />{working === "batch-prompt" ? "提示词生成中" : "批量提示词"}
            </button>
            <button className="secondary-button" onClick={() => void generateBatch("image")} disabled={working !== null || kindAssets.length === 0}>
              <ImagePlus size={14} />{working === "batch-image" ? "图片生成中" : "批量图片"}
            </button>
            <button
              className="secondary-button"
              onClick={importMaterials}
              disabled={working !== null}
            >
              <WandSparkles size={14} />
              {working === "import" ? "建立中" : "从故事资料建立"}
            </button>
            <button className="primary-button" onClick={openCreate}>
              <Plus size={14} />新建{kindConfig.singular}
            </button>
          </div>
        </header>
        {error && <div className="settings-error asset-error">{error}</div>}
        {batchMessage && <div className="asset-batch-message">{batchMessage}</div>}
        <div className="asset-workspace">
          <section className="asset-list-panel">
          {visibleAssets.map((asset, index) => (
            <button
              className={selected?.resourceId === asset.resourceId ? "asset-row active" : "asset-row"}
              onClick={() => setSelectedId(asset.resourceId)}
              key={asset.resourceId}
            >
              <span className={`asset-thumb portrait-${["a", "b", "c"][index % 3]}`}>
                {asset.referenceImage
                  ? <img src={asset.referenceImage.contentUrl} alt="" />
                  : asset.name.slice(0, 1)}
              </span>
              <span className="asset-row-copy">
                <strong>{asset.name}</strong>
                <small>{asset.summary || "尚未填写叙事定义"}</small>
              </span>
              <span className="asset-row-meta">
                <span className={`state-label ${asset.status}`}>{asset.status === "draft" ? "草稿" : asset.status}</span>
                <small>v{asset.version}</small>
              </span>
            </button>
          ))}
          {!loading && visibleAssets.length === 0 && (
            <div className="asset-list-empty">
              <strong>{search ? "没有匹配资产" : `尚无${kindConfig.label}资产`}</strong>
              <p>{search ? "调整搜索词后重试。" : "可从故事资料建立，或手动创建。"}</p>
            </div>
          )}
          {loading && <div className="asset-list-empty">正在读取资产...</div>}
          </section>
          {selected ? (
            <section className="asset-detail">
            <header className="asset-detail-header">
              <div className="asset-detail-identity">
                <span className="eyebrow">{kindConfig.label} · v{selected.version} · {selected.status === "draft" ? "草稿" : selected.status}</span>
                <h2 title={selected.name}>{selected.name}</h2>
              </div>
              <div className="asset-detail-actions">
                <button
                  className="secondary-button asset-edit-button"
                  onClick={() => openEdit(selected)}
                  title="编辑并保存新版本"
                >
                  <Edit3 size={14} /><span>编辑</span>
                </button>
                <VersionPicker projectId={projectId} assetId={selected.assetId} label={`${kindConfig.singular}版本`} compact />
                <input
                  ref={referenceUploadInput}
                  type="file"
                  accept="image/png,image/jpeg,image/webp"
                  hidden
                  onChange={(event) => {
                    const file = event.target.files?.[0];
                    if (file) void uploadReference(file);
                  }}
                />
                <button
                  className="secondary-button asset-reference-upload-button"
                  type="button"
                  disabled={working !== null}
                  title="上传参考图（PNG、JPEG 或 WebP，最大 10 MB）"
                  aria-label="上传参考图"
                  onClick={() => referenceUploadInput.current?.click()}
                >
                  <Upload size={14} />
                </button>
                <button
                  className="primary-button asset-reference-generate-button"
                  onClick={() => void generateReferenceImage()}
                  disabled={working !== null || !selected.referencePrompt}
                  title={selected.referencePrompt
                    ? `${assetReferenceSpecs[selected.kind].title} · 1024 × 1024 · 纯白背景`
                    : "请先生成提示词"}
                  aria-label={working === "image" ? "正在生成图片" : selected.referenceImage ? "重新生成图片" : "生成图片"}
                >
                  <RefreshCw size={13} />
                  <span>{working === "image"
                    ? "生成中"
                    : selected.referenceImage ? "重新生成图片" : "生成图片"}</span>
                </button>
              </div>
            </header>
            <dl className="detail-grid">
              <div className="asset-detail-summary">
                <dt>叙事定义</dt>
                <dd title={selected.summary || undefined}>{selected.summary || "未填写"}</dd>
              </div>
              <div>
                <dt>视觉定义</dt>
                <dd>{selected.visualDescription || "未填写"}</dd>
              </div>
              <div>
                <dt>故事引用</dt>
                <dd>{selected.storyReferences.join("、") || "尚未关联"}</dd>
              </div>
              {selected.mustKeep.length > 0 && (
                <div>
                  <dt>必须保留</dt>
                  <dd>{selected.mustKeep.join("、")}</dd>
                </div>
              )}
              {selected.avoid.length > 0 && (
                <div>
                  <dt>禁止项</dt>
                  <dd>{selected.avoid.join("、")}</dd>
                </div>
              )}
            </dl>
            <div className="asset-reference-workbench">
              <div className="asset-reference-body">
                {selected.referenceImage ? (
                  <div className="asset-reference-canvas" title="预览设定图">
                    <Image
                      src={selected.referenceImage.contentUrl}
                      alt={`${selected.name}设定图`}
                      width="100%"
                      preview={{ mask: "预览" }}
                    />
                    <span className="asset-reference-version">v{selected.referenceImage.version}</span>
                  </div>
                ) : (
                  <div className="asset-reference-canvas empty">
                    <ImagePlus size={24} />
                    <strong>尚未生成设定图</strong>
                    <span>生成后固定保存为 1024 × 1024 PNG</span>
                  </div>
                )}
                <aside className="asset-prompt-panel">
                  <header>
                    <div className="asset-prompt-title"><span>生成提示词</span><small>{selected.referencePrompt ? `v${selected.referencePrompt.version}` : "待生成"}</small></div>
                    <div className="asset-prompt-actions">
                      <button
                        type="button"
                        className="secondary-button asset-prompt-generate-button"
                        disabled={working !== null}
                        onClick={() => {
                          if (selected.referencePrompt) {
                            setReferenceFeedback("");
                            setUseCurrentReference(Boolean(selected.referenceImage));
                            setReferenceFeedbackOpen(true);
                          } else {
                            void generateReferencePrompt();
                          }
                        }}
                      >
                        <Sparkles size={12} />{working === "prompt" ? "生成中" : selected.referencePrompt ? "重生成提示词" : "生成提示词"}
                      </button>
                      {selected.referencePrompt && (
                        <VersionPicker projectId={projectId} assetId={selected.referencePrompt.assetId} label="提示词版本" compact />
                      )}
                      {selected.referencePrompt?.prompt && (
                      <button
                        type="button"
                        className="icon-button"
                        title="复制提示词"
                        aria-label="复制提示词"
                        onClick={() => {
                          void navigator.clipboard.writeText(selected.referencePrompt?.prompt ?? "");
                          setPromptCopied(true);
                        }}
                      >
                        {promptCopied ? <Check size={14} /> : <Copy size={14} />}
                      </button>
                      )}
                    </div>
                  </header>
                  <pre>{selected.referencePrompt?.prompt || "先生成提示词，确认后再单独生成图片。"}</pre>
                </aside>
              </div>
            </div>
            {selected.kind === "character" && (
              <CharacterVoicePanel
                key={`${projectId}:${selected.resourceId}`}
                projectId={projectId}
                character={selected}
              />
            )}
            </section>
          ) : (
            <section className="asset-detail asset-detail-empty">
            <ImagePlus size={28} />
            <h2>{loading ? "正在读取资产" : `尚无${kindConfig.label}资产`}</h2>
            <p>资产会统一包含视觉定义、设定图、参考与故事引用。</p>
            {!loading && <button className="primary-button" onClick={openCreate}><Plus size={14} />新建{kindConfig.singular}</button>}
            </section>
          )}
        </div>
      </div>
      {referenceFeedbackOpen && selected && (
        <div className="modal-backdrop" onMouseDown={() => working !== "prompt" && setReferenceFeedbackOpen(false)}>
          <form
            className="dialog asset-reference-feedback-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              if (referenceFeedback.trim()) void generateReferencePrompt(referenceFeedback.trim(), useCurrentReference);
            }}
          >
            <span className="eyebrow">{kindConfig.label}提示词 / 重新生成</span>
            <h2>生成新版提示词</h2>
            <p>这里只更新提示词，不会立即生成图片。确认提示词后再点击“生成图片”。</p>
            <div className="asset-reference-basis">
              <div>
                <strong>基于当前参考图修改</strong>
                <span>{useCurrentReference ? "保持当前主体、造型和构图连续性" : "仅按资产定义与本轮意见重新绘制"}</span>
              </div>
              <Switch
                checked={useCurrentReference}
                onChange={setUseCurrentReference}
                disabled={working === "prompt" || !selected.referenceImage}
                aria-label="基于当前参考图修改"
              />
            </div>
            <label>
              <span>修改意见</span>
              <textarea
                autoFocus
                required
                maxLength={2000}
                rows={5}
                value={referenceFeedback}
                onChange={(event) => setReferenceFeedback(event.target.value)}
                placeholder="例如：牛角更短，披风改为深蓝色；保持服装结构与四视图布局不变。"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" disabled={working === "prompt"} onClick={() => setReferenceFeedbackOpen(false)}>取消</button>
              <button className="primary-button" type="submit" disabled={working === "prompt" || !referenceFeedback.trim()}>
                <Sparkles size={13} />{working === "prompt" ? "生成中" : "生成新版提示词"}
              </button>
            </div>
          </form>
        </div>
      )}
      {editorOpen && (
        <div
          className="modal-backdrop"
          onMouseDown={() => setEditorOpen(false)}
        >
          <form
            className="dialog asset-editor-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={saveAsset}
          >
            <span className="eyebrow">{editingAsset ? `v${editingAsset.version} → v${editingAsset.version + 1}` : "创建草稿"}</span>
            <h2>{editingAsset ? `编辑${kindConfig.singular}资产` : `新建${kindConfig.singular}资产`}</h2>
            <label>
              <span>名称</span>
              <input
                autoFocus
                required
                maxLength={100}
                value={editor.name}
                onChange={(event) => setEditor((current) => ({ ...current, name: event.target.value }))}
                placeholder={`输入${kindConfig.singular}名称`}
              />
            </label>
            <label>
              <span>叙事定义</span>
              <textarea value={editor.summary} onChange={(event) => setEditor((current) => ({ ...current, summary: event.target.value }))} placeholder="身份、功能、目标或场景用途" />
            </label>
            <label>
              <span>视觉定义</span>
              <textarea value={editor.visualDescription} onChange={(event) => setEditor((current) => ({ ...current, visualDescription: event.target.value }))} placeholder="形态、服装、材质、色彩、光线等稳定视觉特征" />
            </label>
            <div className="asset-editor-grid">
              <label>
                <span>必须保留（每行一项）</span>
                <textarea value={editor.mustKeep} onChange={(event) => setEditor((current) => ({ ...current, mustKeep: event.target.value }))} />
              </label>
              <label>
                <span>禁止项（每行一项）</span>
                <textarea value={editor.avoid} onChange={(event) => setEditor((current) => ({ ...current, avoid: event.target.value }))} />
              </label>
            </div>
            <label>
              <span>故事引用（每行一项）</span>
              <textarea value={editor.storyReferences} onChange={(event) => setEditor((current) => ({ ...current, storyReferences: event.target.value }))} placeholder="章节、场次或剧情节点" />
            </label>
            <div>
              <button
                type="button"
                className="secondary-button"
                onClick={() => setEditorOpen(false)}
              >
                取消
              </button>
              <button
                type="submit"
                className="primary-button"
                disabled={working !== null || !editor.name.trim()}
              >
                <Save size={14} />{working === "save" ? "保存中" : editingAsset ? "保存新版本" : "创建草稿"}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

export function ScriptPage() {
  const { projectId = "", productionEpisodeId = "" } = useParams();
  const navigate = useNavigate();
  const [scriptPackage, setScriptPackage] = useState<ProductionScriptPackage | null>(null);
  const [activeSceneNumber, setActiveSceneNumber] = useState(1);
  const [loadError, setLoadError] = useState<{ episodeId: string; message: string } | null>(null);
  const [regenerating, setRegenerating] = useState(false);
  const [regenerateError, setRegenerateError] = useState("");
  const [productionEpisodes, setProductionEpisodes] = useState<ProductionEpisodeRecord[]>([]);
  const [sceneEditor, setSceneEditor] = useState<ProductionScriptSceneDraft | null>(null);
  const [sceneSaving, setSceneSaving] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    listProductionEpisodes(projectId, controller.signal)
      .then(setProductionEpisodes)
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        console.warn("正式剧本生产集列表加载失败", loadError);
      });
    return () => controller.abort();
  }, [projectId]);

  useEffect(() => {
    const controller = new AbortController();
    getProductionScriptPackage(projectId, productionEpisodeId, controller.signal)
      .then((loadedScript) => {
        setScriptPackage(loadedScript);
        setLoadError(null);
        setActiveSceneNumber(loadedScript.episode.scenes[0]?.sceneNumber ?? 1);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setLoadError({
          episodeId: productionEpisodeId,
          message: loadError instanceof Error ? loadError.message : "正式剧本加载失败。",
        });
      });
    return () => controller.abort();
  }, [productionEpisodeId, projectId]);

  const error = loadError?.episodeId === productionEpisodeId ? loadError.message : "";
  if (!scriptPackage || scriptPackage.productionEpisodeId !== productionEpisodeId) {
    return (
      <div className="page full-height-page">
        <ScriptStageTabs
          projectId={projectId}
          active="production"
          productionEpisodeId={productionEpisodeId}
        />
        <div className="production-episode-switcher">
          <span>正式剧本</span>
          <Select
            aria-label="切换正式剧本生产集"
            value={productionEpisodeId || undefined}
            disabled={productionEpisodes.length === 0}
            onChange={(episodeId) => navigate(getScriptWorkspacePath(projectId, episodeId, "production"))}
            options={productionEpisodes.map((episode) => ({
              value: episode.id,
              label: `E${String(episode.episodeNumber).padStart(2, "0")} · ${episode.title}`,
            }))}
          />
        </div>
        <div className="source-empty-state development-empty-state">
          <span className="eyebrow">剧本 / 正式资产</span>
          <h1>{error || "正在读取正式剧本"}</h1>
          <p>{error ? "请返回改编方案检查确认状态。" : "正在按生产集读取锁定的剧本包。"}</p>
        </div>
      </div>
    );
  }

  const episode = scriptPackage.episode;
  const activeScene = episode.scenes.find((item) => item.sceneNumber === activeSceneNumber)
    ?? episode.scenes[0];
  const sceneHooks = [
    ...(episode.smallHooks ?? []).map((hook, index, items) => ({
      hook,
      tone: "small" as const,
      position: (index + 1) / (items.length + 1),
    })),
    ...(episode.bigHooks ?? []).map((hook, index, items) => ({
      hook,
      tone: "big" as const,
      position: (index + 1) / (items.length + 1),
    })),
  ]
    .sort((left, right) => left.position - right.position || left.tone.localeCompare(right.tone))
    .map((item) => ({
      ...item,
      sceneNumber: episode.scenes[
        Math.round(item.position * Math.max(0, episode.scenes.length - 1))
      ]?.sceneNumber,
    }));
  const activeSceneHooks = sceneHooks.filter((item) => item.sceneNumber === activeScene?.sceneNumber);
  const activeSceneShots = [...(activeScene?.shotPlan ?? [])]
    .sort((left, right) => left.shotNumber - right.shotNumber);
  const sceneDurationSeconds = activeScene?.targetSeconds
    ?? activeSceneShots.reduce((total, shot) => total + shot.durationSeconds, 0);
  const averageShotSeconds = activeSceneShots.length > 0
    ? sceneDurationSeconds / activeSceneShots.length
    : 0;
  const rhythmLabel = activeScene?.rhythm?.trim() || "旧版未规划";
  const shotSizeCount = new Set(activeSceneShots.map((shot) => shot.shotSize).filter(Boolean)).size;
  const cameraMovementCount = new Set(activeSceneShots.map((shot) => shot.cameraMovement).filter(Boolean)).size;
  const firstShotSize = activeSceneShots[0]?.shotSize;
  const lastShotSize = activeSceneShots.at(-1)?.shotSize;
  const shotSizeChange = firstShotSize && lastShotSize
    ? firstShotSize === lastShotSize ? `${firstShotSize}贯穿` : `${firstShotSize} → ${lastShotSize}`
    : "—";

  const handleRegenerate = async () => {
    setRegenerating(true);
    setRegenerateError("");
    try {
      const regenerated = await regenerateProductionScript(projectId, productionEpisodeId);
      setScriptPackage(regenerated);
      setActiveSceneNumber(regenerated.episode.scenes[0]?.sceneNumber ?? 1);
    } catch (regenerateError) {
      setRegenerateError(regenerateError instanceof Error
        ? regenerateError.message
        : "正式剧本重新生成失败。");
    } finally {
      setRegenerating(false);
    }
  };

  const openSceneEditor = () => {
    if (!activeScene || scriptPackage.isLegacyOutline) return;
    setRegenerateError("");
    setSceneEditor(structuredClone(activeScene));
  };

  const saveScene = async (event: FormEvent) => {
    event.preventDefault();
    if (!sceneEditor) return;
    setSceneSaving(true);
    setRegenerateError("");
    try {
      const normalizedScene = {
        ...sceneEditor,
        characters: normalizeEditorLines(sceneEditor.characters),
        props: normalizeEditorLines(sceneEditor.props),
        dialogues: sceneEditor.dialogues.map((dialogue) => ({
          ...dialogue,
          lines: normalizeEditorLines(dialogue.lines),
        })),
      };
      const updated = await updateProductionScriptScene(
        projectId,
        productionEpisodeId,
        sceneEditor.sceneNumber,
        normalizedScene,
      );
      setScriptPackage(updated);
      setActiveSceneNumber(sceneEditor.sceneNumber);
      setSceneEditor(null);
    } catch (saveError) {
      setRegenerateError(saveError instanceof Error ? saveError.message : "正式剧本场次保存失败。");
    } finally {
      setSceneSaving(false);
    }
  };

  return (
    <div className="page full-height-page production-script-page">
      <ScriptStageTabs
        projectId={projectId}
        active="production"
        sourceId={scriptPackage.sourceResourceId}
        productionEpisodeId={productionEpisodeId}
      />
      <div className="production-script-workspace">
        <header className="production-script-toolbar">
          <div className="production-script-context">
            <span>{scriptPackage.isLegacyOutline ? "历史大纲" : "正式剧本"}</span>
            <Select
              aria-label="切换正式剧本生产集"
              value={productionEpisodeId}
              onChange={(episodeId) => navigate(getScriptWorkspacePath(projectId, episodeId, "production"))}
              options={productionEpisodes.map((episode) => ({
                value: episode.id,
                label: `E${String(episode.episodeNumber).padStart(2, "0")} · ${episode.title}`,
              }))}
            />
            <small>v{scriptPackage.version} · {scriptPackage.targetSeconds ?? episode.targetSeconds} 秒 · {episode.scenes.length} 场</small>
          </div>
          <div className="production-script-header-actions">
            <VersionPicker compact projectId={projectId} assetId={scriptPackage.assetId} label="正式剧本版本" />
            <button type="button" className="primary-button" disabled={regenerating} onClick={handleRegenerate}>
              <RefreshCw size={13} />{regenerating ? "重新生成中" : "重新生成正式剧本"}
            </button>
          </div>
        </header>
        {regenerateError && <div className="settings-error production-script-message">{regenerateError}</div>}
        {scriptPackage.isLegacyOutline && (
          <div className="production-legacy-notice">
            <strong>这份版本保存的是历史改编大纲，不是正式影视剧本。</strong>
            <span>剧情节点和对白意图仅供追溯。点击“重新生成正式剧本”后，会在同一资源下创建包含动作、真实对白和摄影骨架的新版本。</span>
          </div>
        )}
        <div className="production-script-shell">
        <aside className="production-scene-list">
          <header><strong>场次</strong><span>{episode.scenes.length}</span></header>
          <nav aria-label="场次目录">
            {episode.scenes.map((scene) => {
              const hooks = sceneHooks.filter((item) => item.sceneNumber === scene.sceneNumber);
              const shots = scene.shotPlan ?? [];
              const duration = scene.targetSeconds ?? shots.reduce((total, shot) => total + shot.durationSeconds, 0);
              return (
                <button
                  className={scene.sceneNumber === activeScene?.sceneNumber ? "active" : ""}
                  onClick={() => setActiveSceneNumber(scene.sceneNumber)}
                  key={scene.sceneNumber}
                >
                  <span>S{String(scene.sceneNumber).padStart(2, "0")}</span>
                  <div className="production-scene-list-copy">
                    <strong>{scene.heading}</strong>
                    <small>{shots.length > 0 ? `${duration.toFixed(1)} 秒 · ${shots.length} 镜` : "待分镜"}</small>
                  </div>
                  <div className="production-scene-hook-dots" aria-label={`${hooks.length} 个爆点`}>
                    {hooks.map((item, index) => <i className={item.tone} title={item.tone === "small" ? "小爆点" : "大爆点"} key={`${item.tone}-${index}`} />)}
                  </div>
                </button>
              );
            })}
          </nav>
        </aside>
        {activeScene && (
          <article className="production-script-editor">
            <header className="production-scene-header">
              <div className="production-scene-heading">
                <div className="production-scene-kicker">
                  <span>S{String(activeScene.sceneNumber).padStart(2, "0")}</span>
                  <small>第 {activeScene.sceneNumber} / {episode.scenes.length} 场</small>
                </div>
                <div className="production-scene-title-row">
                  <h2>{activeScene.heading}</h2>
                  {!scriptPackage.isLegacyOutline && (
                    <button className="secondary-button" type="button" onClick={openSceneEditor}>
                      <Edit3 size={13} />编辑场次
                    </button>
                  )}
                </div>
                <p>{activeScene.storyFunction}</p>
              </div>
              <div className="production-scene-facts" aria-label="场次摘要">
                <div><span>时长</span><strong>{activeSceneShots.length > 0 ? `${sceneDurationSeconds.toFixed(1)}s` : "—"}</strong></div>
                <div><span>镜头</span><strong>{activeSceneShots.length > 0 ? `${activeSceneShots.length} 镜` : "—"}</strong></div>
                <div><span>人物</span><strong>{activeScene.characters.length}</strong></div>
                <div><span>道具</span><strong>{activeScene.props.length}</strong></div>
              </div>
              <div className="production-scene-assets">
                <div><span>人物</span><p>{activeScene.characters.join(" · ") || "无明确人物"}</p></div>
                <div><span>道具</span><p>{activeScene.props.join(" · ") || "无关键道具"}</p></div>
              </div>
            </header>
            <section className="production-script-document">
              <header className="production-section-heading">
                <div><span>01</span><div><h3>剧本正文</h3><p>动作与对白</p></div></div>
              </header>
              <div className="production-action-block">
                <span>{scriptPackage.isLegacyOutline ? "剧情节点" : "动作"}</span>
                <p>{scriptPackage.isLegacyOutline ? activeScene.summary : activeScene.action}</p>
              </div>
              {activeSceneHooks.length > 0 && (
                <div className="production-story-beats">
                  {activeSceneHooks.map((item, index) => (
                    <div className={item.tone} key={`${item.tone}-${index}-${item.hook}`}>
                      <span><i />{item.tone === "small" ? "小爆点" : "大爆点"}</span>
                      <p>{item.hook}</p>
                    </div>
                  ))}
                </div>
              )}
              {scriptPackage.isLegacyOutline ? (
                <div className="production-dialogue-intent">
                  <span>对白意图</span>
                  <p>{activeScene.dialogueIntent || "旧版大纲未记录对白意图。"}</p>
                </div>
              ) : (
                <div className="production-screenplay-dialogues">
                  <header><span>对白</span><small>{activeScene.dialogues.length} 组</small></header>
                  {activeScene.dialogues.length > 0 ? activeScene.dialogues.map((dialogue, index) => (
                    <div className="screenplay-dialogue" key={`${dialogue.character}-${index}`}>
                      <div className="screenplay-dialogue-cue">
                        <strong>{dialogue.character}</strong>
                        {dialogue.parenthetical && <small>（{dialogue.parenthetical}）</small>}
                      </div>
                      <div className="screenplay-dialogue-lines">
                        {dialogue.lines.map((line, lineIndex) => <p key={lineIndex}>{line}</p>)}
                      </div>
                    </div>
                  )) : <p>本场无对白。</p>}
                </div>
              )}
            </section>
            <section className="production-execution-plan">
              <header className="production-section-heading production-execution-heading">
                <div><span>02</span><div><h3>制作规划</h3><p>节奏、视觉与执行镜头</p></div></div>
                <button type="button" onClick={() => navigate(activeSceneShots.length > 0 ? `/projects/${projectId}/storyboard/episodes/${productionEpisodeId}` : `/projects/${projectId}/script`)}>
                  {activeSceneShots.length > 0 ? "进入分镜" : "返回方案"}
                </button>
              </header>
              <div className="production-direction-grid">
                <div><span>节奏设计</span><strong>{rhythmLabel}</strong><small>{averageShotSeconds > 0 ? `平均 ${averageShotSeconds.toFixed(1)}s / 镜` : "重新生成方案后补齐"}</small></div>
                <div><span>视觉对比</span><strong>{shotSizeChange}</strong><small>{activeScene?.visualContrast?.trim() || (activeSceneShots.length > 0 ? `${shotSizeCount} 种景别 · ${cameraMovementCount} 种运镜` : "重新生成方案后补齐")}</small></div>
              </div>
              {activeSceneShots.length > 0 && (
                <div className="production-rhythm-track" aria-label="镜头节奏时间轴">
                  <header><span>0s</span><strong>镜头节奏</strong><span>{sceneDurationSeconds.toFixed(1)}s</span></header>
                  <div>
                    {activeSceneShots.map((shot) => (
                      <button
                        type="button"
                        style={{ flexGrow: shot.durationSeconds }}
                        title={`S${String(activeScene.sceneNumber).padStart(2, "0")}-${String(shot.shotNumber).padStart(2, "0")} · ${shot.durationSeconds}s · ${shot.shotSize}`}
                        onClick={() => navigate(`/projects/${projectId}/storyboard/episodes/${productionEpisodeId}`)}
                        key={shot.shotNumber}
                      >
                        <span>{String(shot.shotNumber).padStart(2, "0")}</span>
                        <small>{shot.durationSeconds}s</small>
                      </button>
                    ))}
                  </div>
                </div>
              )}
              {activeSceneShots.length > 0 ? (
                <div className="production-shot-list">
                  {activeSceneShots.map((shot) => (
                    <button
                      type="button"
                      className="production-shot-row"
                      onClick={() => navigate(`/projects/${projectId}/storyboard/episodes/${productionEpisodeId}`)}
                      key={shot.shotNumber}
                    >
                      <div className="production-shot-code">
                        <strong>S{String(activeScene.sceneNumber).padStart(2, "0")}-{String(shot.shotNumber).padStart(2, "0")}</strong>
                        <span>{shot.durationSeconds}s</span>
                      </div>
                      <div className="production-shot-camera">
                        <span>{shot.shotSize} · {shot.cameraAngle}</span>
                        <small>{shot.cameraMovement || "固定机位"}</small>
                      </div>
                      <div className="production-shot-action">
                        <strong>{shot.purpose}</strong>
                        <p>构图、动作、对白与声音由下游分镜在此摄影骨架内细化。</p>
                      </div>
                    </button>
                  ))}
                </div>
              ) : (
                <div className="production-shot-empty">
                  <strong>这版正式剧本没有拍摄计划</strong>
                  <p>从改编大纲重新生成正式剧本后，会确定场次、动作、对白和摄影骨架，再由分镜继续细化。</p>
                </div>
              )}
            </section>
          </article>
        )}
        </div>
      </div>
      {sceneEditor && (
        <div className="modal-backdrop" onMouseDown={() => !sceneSaving && setSceneEditor(null)}>
          <form className="dialog production-scene-editor-dialog" onMouseDown={(event) => event.stopPropagation()} onSubmit={saveScene}>
            <header className="production-scene-editor-heading">
              <div>
                <span className="eyebrow">正式剧本 / S{String(sceneEditor.sceneNumber).padStart(2, "0")}</span>
                <h2>编辑场次</h2>
                <p>保存后创建正式剧本 v{scriptPackage.version + 1}，已有分镜会识别为旧版本。</p>
              </div>
              <button className="secondary-button icon-button" type="button" title="关闭" aria-label="关闭场次编辑" disabled={sceneSaving} onClick={() => setSceneEditor(null)}>
                <X size={14} />
              </button>
            </header>
            <div className="production-scene-editor-body">
              <div className="production-scene-editor-grid">
                <label className="wide"><span>场景标题</span><input autoFocus required maxLength={200} value={sceneEditor.heading} onChange={(event) => setSceneEditor({ ...sceneEditor, heading: event.target.value })} /></label>
                <label className="wide"><span>场次摘要</span><textarea required value={sceneEditor.summary} onChange={(event) => setSceneEditor({ ...sceneEditor, summary: event.target.value })} /></label>
                <label className="wide"><span>故事功能</span><textarea required value={sceneEditor.storyFunction} onChange={(event) => setSceneEditor({ ...sceneEditor, storyFunction: event.target.value })} /></label>
                <label className="wide"><span>对白意图</span><textarea value={sceneEditor.dialogueIntent ?? ""} onChange={(event) => setSceneEditor({ ...sceneEditor, dialogueIntent: event.target.value || null })} /></label>
                <label className="wide"><span>动作正文</span><textarea className="scene-action-input" required value={sceneEditor.action} onChange={(event) => setSceneEditor({ ...sceneEditor, action: event.target.value })} /></label>
                <label><span>人物（每行一个）</span><textarea value={sceneEditor.characters.join("\n")} onChange={(event) => setSceneEditor({ ...sceneEditor, characters: splitEditorLines(event.target.value) })} /></label>
                <label><span>道具（每行一个）</span><textarea value={sceneEditor.props.join("\n")} onChange={(event) => setSceneEditor({ ...sceneEditor, props: splitEditorLines(event.target.value) })} /></label>
                <label><span>目标时长（秒）</span><input type="number" required min={1} step={0.1} value={sceneEditor.targetSeconds} onChange={(event) => setSceneEditor({ ...sceneEditor, targetSeconds: Number(event.target.value) })} /></label>
                <label><span>节奏设计</span><input required value={sceneEditor.rhythm} onChange={(event) => setSceneEditor({ ...sceneEditor, rhythm: event.target.value })} /></label>
                <label className="wide"><span>视觉对比</span><textarea required value={sceneEditor.visualContrast} onChange={(event) => setSceneEditor({ ...sceneEditor, visualContrast: event.target.value })} /></label>
              </div>
              <section className="production-scene-editor-section">
                <header><div><strong>对白</strong><span>{sceneEditor.dialogues.length} 组</span></div><button className="secondary-button" type="button" onClick={() => setSceneEditor({ ...sceneEditor, dialogues: [...sceneEditor.dialogues, { character: "", parenthetical: null, lines: [""] }] })}><Plus size={12} />添加对白</button></header>
                {sceneEditor.dialogues.map((dialogue, index) => (
                  <div className="production-dialogue-editor-row" key={index}>
                    <input required placeholder="角色" value={dialogue.character} onChange={(event) => setSceneEditor({ ...sceneEditor, dialogues: sceneEditor.dialogues.map((item, itemIndex) => itemIndex === index ? { ...item, character: event.target.value } : item) })} />
                    <input placeholder="表演提示（可选）" value={dialogue.parenthetical ?? ""} onChange={(event) => setSceneEditor({ ...sceneEditor, dialogues: sceneEditor.dialogues.map((item, itemIndex) => itemIndex === index ? { ...item, parenthetical: event.target.value || null } : item) })} />
                    <textarea required placeholder="每行一句对白" value={dialogue.lines.join("\n")} onChange={(event) => setSceneEditor({ ...sceneEditor, dialogues: sceneEditor.dialogues.map((item, itemIndex) => itemIndex === index ? { ...item, lines: splitEditorLines(event.target.value) } : item) })} />
                    <button className="icon-button" type="button" title="删除对白" aria-label={`删除第 ${index + 1} 组对白`} onClick={() => setSceneEditor({ ...sceneEditor, dialogues: sceneEditor.dialogues.filter((_, itemIndex) => itemIndex !== index) })}><Trash2 size={13} /></button>
                  </div>
                ))}
              </section>
              <section className="production-scene-editor-section">
                <header><div><strong>镜头规划</strong><span>{sceneEditor.shotPlan.length} 镜</span></div></header>
                {sceneEditor.shotPlan.map((shot, index) => (
                  <div className="production-shot-editor-row" key={index}>
                    <strong>{String(index + 1).padStart(2, "0")}</strong>
                    <input required placeholder="镜头目的" value={shot.purpose} onChange={(event) => setSceneEditor({ ...sceneEditor, shotPlan: sceneEditor.shotPlan.map((item, itemIndex) => itemIndex === index ? { ...item, purpose: event.target.value } : item) })} />
                    <input required placeholder="景别" value={shot.shotSize} onChange={(event) => setSceneEditor({ ...sceneEditor, shotPlan: sceneEditor.shotPlan.map((item, itemIndex) => itemIndex === index ? { ...item, shotSize: event.target.value } : item) })} />
                    <input required placeholder="机位" value={shot.cameraAngle} onChange={(event) => setSceneEditor({ ...sceneEditor, shotPlan: sceneEditor.shotPlan.map((item, itemIndex) => itemIndex === index ? { ...item, cameraAngle: event.target.value } : item) })} />
                    <input required placeholder="运镜" value={shot.cameraMovement} onChange={(event) => setSceneEditor({ ...sceneEditor, shotPlan: sceneEditor.shotPlan.map((item, itemIndex) => itemIndex === index ? { ...item, cameraMovement: event.target.value } : item) })} />
                    <input type="number" required min={0.1} step={0.1} title="时长（秒）" value={shot.durationSeconds} onChange={(event) => setSceneEditor({ ...sceneEditor, shotPlan: sceneEditor.shotPlan.map((item, itemIndex) => itemIndex === index ? { ...item, durationSeconds: Number(event.target.value) } : item) })} />
                  </div>
                ))}
              </section>
            </div>
            {regenerateError && <div className="settings-error">{regenerateError}</div>}
            <footer>
              <button className="secondary-button" type="button" disabled={sceneSaving} onClick={() => setSceneEditor(null)}>取消</button>
              <button className="primary-button" type="submit" disabled={sceneSaving}><Save size={13} />{sceneSaving ? "保存中" : "保存新版本"}</button>
            </footer>
          </form>
        </div>
      )}
    </div>
  );
}

function splitEditorLines(value: string) {
  return value.split(/\r?\n/);
}

function normalizeEditorLines(lines: string[]) {
  return lines.map((item) => item.trim()).filter(Boolean);
}

export function ScriptDemoPage() {
  const { productionEpisodeId } = useParams();
  const episode =
    episodes.find((item) => item.id === productionEpisodeId) ?? episodes[0];
  const [scene, setScene] = useState("S03");
  const [proposal, setProposal] = useState(true);
  return (
    <div className="page full-height-page">
      <PageTitle
        eyebrow="剧本 / 分集剧本"
        title={`${episode.code} · ${episode.title}`}
        description="98.4 秒 · Script v4 draft · 生产集与原文集独立"
        action={
          <div className="button-group">
            <EpisodeSelect value={productionEpisodeId} />
            <button className="primary-button">创建版本</button>
          </div>
        }
      />
      <div className="duration-bar">
        <strong>目标 90–110s</strong>
        <span>
          当前 <b>98.4s</b>
        </span>
        <span>声音 46.2s</span>
        <span>动作 37.0s</span>
        <span>停顿 / 转场 15.2s</span>
        <span className="pass">
          <Check size={13} />
          PASS
        </span>
      </div>
      <div className="script-workspace">
        <aside className="scene-list">
          <header>
            <div>
              <strong>{episode.code} 场次</strong>
              <small>5 场</small>
            </div>
            <button className="icon-button" aria-label="新增场次">
              <Plus size={14} />
            </button>
          </header>
          <div className="scene-tabs">
            {[
              ["S01", "18.0s", "done"],
              ["S02", "24.0s", "done"],
              ["S03", "21.8s", "blocked"],
              ["S04", "19.0s", "done"],
              ["S05", "15.6s", "done"],
            ].map((item) => (
              <button
                className={scene === item[0] ? "active" : ""}
                onClick={() => setScene(item[0])}
                key={item[0]}
              >
                <span>{item[0]}</span>
                <small>{item[1]}</small>
                <i className={item[2]}>{item[2] === "done" ? "✓" : "!"}</i>
              </button>
            ))}
          </div>
        </aside>
        <article className="script-editor">
          <header>
            <div>
              <span className="eyebrow">S03</span>
              <h2>内景 · 天桥食堂二楼 · 凌晨</h2>
              <p>目标 18 秒 · 当前 21.8 秒</p>
            </div>
            <span className="overload">
              <CircleAlert size={14} />
              超出 3.8 秒
            </span>
          </header>
          <div className="script-block action">
            <span>ACTION</span>
            <p>
              林墨推开虚掩的门。桌上的台灯亮着，红色文件袋的封口已经被撕开。
            </p>
            <small>3.2s</small>
          </div>
          <div className="script-block dialogue">
            <span>林墨</span>
            <p>“你让我把文件带来，现在又叫我别相信你。”</p>
            <small>3.8s</small>
          </div>
          <div className="script-block action">
            <span>ACTION</span>
            <p>周岚没有回头。她把一张旧照片推到灯下。</p>
            <small>2.8s</small>
          </div>
          <div className="script-block dialogue selected">
            <span>周岚</span>
            <p>
              “我叫你来，是因为三年前也有人拿着同样的文件袋走进这里。那个人后来再也没有出去。”
            </p>
            <small>8.4s</small>
          </div>
          {proposal && (
            <div className="inline-proposal">
              <header>
                <Sparkles size={14} />
                <strong>Agent 压缩提议</strong>
                <button onClick={() => setProposal(false)}>×</button>
              </header>
              <del>我叫你来，是因为三年前也有人拿着同样的文件袋走进这里。</del>
              <ins>三年前，也有人拿着同样的文件袋来过。</ins>
              <p>预计减少 2.6 秒，信息保持完整。</p>
              <div>
                <button
                  className="secondary-button"
                  onClick={() => setProposal(false)}
                >
                  拒绝
                </button>
                <button
                  className="primary-button"
                  onClick={() => setProposal(false)}
                >
                  接受到草稿
                </button>
              </div>
            </div>
          )}
        </article>
      </div>
    </div>
  );
}

export function StoryboardPage() {
  const { projectId = "", productionEpisodeId = "" } = useParams();
  const navigate = useNavigate();
  const [productionEpisodes, setProductionEpisodes] = useState<ProductionEpisodeRecord[]>([]);
  const [storyboard, setStoryboard] = useState<Storyboard | null>(null);
  const [loadedEpisodeId, setLoadedEpisodeId] = useState("");
  const [generating, setGenerating] = useState(false);
  const [batchAction, setBatchAction] = useState("");
  const [batchFeedback, setBatchFeedback] = useState<{ label: string; result: BatchStoryboardMediaResult } | null>(null);
  const [framePreviewIndex, setFramePreviewIndex] = useState(0);
  const [framePreviewOpen, setFramePreviewOpen] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    listProductionEpisodes(projectId, controller.signal)
      .then(setProductionEpisodes)
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "生产集加载失败。");
      });
    return () => controller.abort();
  }, [projectId]);

  useEffect(() => {
    if (!productionEpisodeId) return;
    const controller = new AbortController();
    getStoryboard(projectId, productionEpisodeId, controller.signal)
      .then((loaded) => {
        setStoryboard(loaded);
        setLoadedEpisodeId(productionEpisodeId);
        setError("");
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setLoadedEpisodeId(productionEpisodeId);
        setError(loadError instanceof Error ? loadError.message : "分镜加载失败。");
      });
    return () => controller.abort();
  }, [productionEpisodeId, projectId]);

  if (!productionEpisodeId && productionEpisodes.length > 0) {
    return <Navigate to={`episodes/${productionEpisodes[0].id}`} replace />;
  }

  const loading = productionEpisodeId !== "" && loadedEpisodeId !== productionEpisodeId;
  const currentStoryboard = storyboard?.productionEpisodeId === productionEpisodeId ? storyboard : null;
  const framePreviews = (currentStoryboard?.shots ?? [])
    .flatMap((shot) => {
      const shotCode = `S${String(shot.sceneNumber).padStart(2, "0")}-${String(shot.shotNumber).padStart(2, "0")}`;
      return [
        shot.production?.outputUrl ? { src: shot.production.outputUrl, alt: `${shotCode} 首帧` } : null,
        shot.production?.lastFrameUrl ? { src: shot.production.lastFrameUrl, alt: `${shotCode} 尾帧` } : null,
      ];
    })
    .filter((item): item is { src: string; alt: string } => item !== null);
  const openFramePreview = (src: string, alt: string) => {
    const index = framePreviews.findIndex((item) => item.src === src && item.alt === alt);
    if (index < 0) return;
    setFramePreviewIndex(index);
    setFramePreviewOpen(true);
  };
  const generate = async () => {
    setGenerating(true);
    setError("");
    try {
      const generated = await generateStoryboard(projectId, productionEpisodeId);
      setStoryboard(generated);
      setLoadedEpisodeId(productionEpisodeId);
    } catch (generateError) {
      setError(generateError instanceof Error ? generateError.message : "分镜生成失败。");
    } finally {
      setGenerating(false);
    }
  };
  const runBatch = async (
    action: string,
    label: string,
    operation: () => Promise<BatchStoryboardMediaResult>,
  ) => {
    setBatchAction(action);
    setBatchFeedback(null);
    setError("");
    try {
      const result = await operation();
      setBatchFeedback({ label, result });
      setStoryboard(await getStoryboard(projectId, productionEpisodeId));
    } catch (batchError) {
      setError(batchError instanceof Error ? batchError.message : `${label}失败。`);
    } finally {
      setBatchAction("");
    }
  };
  return (
    <div className="page full-height-page storyboard-page">
      <div className="storyboard-workspace">
        <header className="storyboard-toolbar">
          <div className="storyboard-toolbar-actions">
            <Select
              aria-label="切换分镜生产集"
              value={productionEpisodeId}
              onChange={(episodeId) => navigate(`../${episodeId}`, { relative: "path" })}
              options={productionEpisodes.map((item) => ({
                value: item.id,
                label: `E${String(item.episodeNumber).padStart(2, "0")} · ${item.title}`,
              }))}
            />
            <button className="primary-button" onClick={generate} disabled={generating || !productionEpisodeId}>
              <WandSparkles size={14} />
              {generating ? "正在设计分镜" : currentStoryboard ? "重新生成草稿" : "生成分镜草稿"}
            </button>
            <div className="storyboard-batch-actions" aria-label="分镜批量生成">
              <Tooltip title={batchAction === "image-prompts" ? "正在批量生成图片提示词" : "批量生成图片提示词"}>
                <button className="secondary-button storyboard-batch-button" aria-label="批量生成图片提示词" disabled={Boolean(batchAction) || !currentStoryboard} onClick={() => void runBatch("image-prompts", "批量图片提示词", () => generateMissingStoryboardImagePrompts(projectId, productionEpisodeId))}>
                  <WandSparkles size={14} />
                </button>
              </Tooltip>
              <Tooltip title={batchAction === "images" ? "正在批量生成图片" : "批量生成图片"}>
                <button className="secondary-button storyboard-batch-button" aria-label="批量生成图片" disabled={Boolean(batchAction) || !currentStoryboard} onClick={() => void runBatch("images", "批量图片", () => generateMissingStoryboardImages(projectId, productionEpisodeId))}>
                  <ImagePlus size={14} />
                </button>
              </Tooltip>
              <Tooltip title={batchAction === "video-prompts" ? "正在批量生成视频提示词" : "批量生成视频提示词"}>
                <button className="secondary-button storyboard-batch-button" aria-label="批量生成视频提示词" disabled={Boolean(batchAction) || !currentStoryboard} onClick={() => void runBatch("video-prompts", "批量视频提示词", () => generateMissingStoryboardVideoPrompts(projectId, productionEpisodeId))}>
                  <Sparkles size={14} />
                </button>
              </Tooltip>
              <Tooltip title={batchAction === "videos" ? "正在批量生成视频" : "批量生成视频"}>
                <button className="secondary-button storyboard-batch-button" aria-label="批量生成视频" disabled={Boolean(batchAction) || !currentStoryboard} onClick={() => void runBatch("videos", "批量视频", () => generateMissingStoryboardVideos(projectId, productionEpisodeId))}>
                  <Play size={14} />
                </button>
              </Tooltip>
            </div>
          </div>
        </header>
      {batchFeedback && (
        <div className={`storyboard-batch-feedback${batchFeedback.result.failed ? " has-errors" : ""}`} role="status">
          <strong>{batchFeedback.label}</strong>
          <span>生成 {batchFeedback.result.generated} · 跳过 {batchFeedback.result.skipped} · 失败 {batchFeedback.result.failed}</span>
          {batchFeedback.result.errors.length > 0 && <small>{batchFeedback.result.errors.join("；")}</small>}
        </div>
      )}
      {error && <div className="source-empty-state development-empty-state"><strong>{error}</strong></div>}
      {!error && (loading || !currentStoryboard) && (
        <div className="source-empty-state development-empty-state">
          <span className="eyebrow">{loading ? "正在读取" : "尚无分镜"}</span>
          <h2>{loading ? "正在读取当前生产集分镜" : "基于正式剧本生成第一版分镜"}</h2>
          {!loading && <p>生成结果会保存为版本化草稿，镜头时长将对齐该集目标时长。</p>}
          {!loading && <button className="primary-button" onClick={generate} disabled={generating}><WandSparkles size={14} />生成分镜草稿</button>}
        </div>
      )}
      {currentStoryboard?.isStale && (
        <div className="storyboard-stale-notice" role="status">
          <CircleAlert size={16} />
          <div>
            <strong>正式剧本已更新，当前显示旧版分镜</strong>
            <span>重新生成会创建镜头新版本，历史版本仍保留。</span>
          </div>
          <button className="primary-button" onClick={generate} disabled={generating}>
            <RefreshCw size={13} />{generating ? "正在重新生成" : "重新生成新分镜"}
          </button>
        </div>
      )}
      {currentStoryboard && (
        <div className="data-table storyboard-table">
          <Image.PreviewGroup
            items={framePreviews}
            preview={{
              current: framePreviewIndex,
              visible: framePreviewOpen,
              onChange: setFramePreviewIndex,
              onVisibleChange: setFramePreviewOpen,
            }}
          />
          <div className="table-row table-head">
            <span>镜号</span>
            <span>爆点</span>
            <span>帧策略</span>
            <span>首帧</span>
            <span>尾帧</span>
            <span>景别 / 机位</span>
            <span>时长</span>
            <span>状态</span>
          </div>
          {currentStoryboard.shots.map((shot) => (
            <button
              className="table-row"
              key={shot.resourceId}
              onClick={() => navigate(`shots/${shot.resourceId}`)}
            >
              <strong>S{String(shot.sceneNumber).padStart(2, "0")}-{String(shot.shotNumber).padStart(2, "0")}</strong>
              <span className="shot-hook-badges">
                {(shot.hooks ?? []).map((hook, index) => (
                  <b className={hook.type} title={hook.description} key={`${hook.type}-${index}`}>
                    {hook.type === "big" ? "大爆点" : "小爆点"}
                  </b>
                ))}
                {!shot.hooks?.length && <small>—</small>}
              </span>
              <span className={`shot-frame-strategy ${shot.productionMode === "first-last-continuous" ? "continuous" : "direct"}`}>
                {shot.productionMode === "first-last-continuous" ? "首帧 + 尾帧" : "仅首帧"}
              </span>
              <span className={`shot-frame-thumbnail ${shot.production?.outputUrl ? "ready" : "empty"}`}>
                {shot.production?.outputUrl
                  ? <img
                      src={shot.production.outputUrl}
                      alt={`S${String(shot.sceneNumber).padStart(2, "0")}-${String(shot.shotNumber).padStart(2, "0")} 首帧`}
                      onClick={(event) => {
                        event.stopPropagation();
                        openFramePreview(shot.production!.outputUrl!, `S${String(shot.sceneNumber).padStart(2, "0")}-${String(shot.shotNumber).padStart(2, "0")} 首帧`);
                      }}
                    />
                  : <small>未生成</small>}
              </span>
              <span className={`shot-frame-thumbnail ${shot.production?.lastFrameUrl ? "ready" : "empty"}`}>
                {shot.production?.lastFrameUrl
                  ? <img
                      src={shot.production.lastFrameUrl}
                      alt={`S${String(shot.sceneNumber).padStart(2, "0")}-${String(shot.shotNumber).padStart(2, "0")} 尾帧`}
                      onClick={(event) => {
                        event.stopPropagation();
                        openFramePreview(shot.production!.lastFrameUrl!, `S${String(shot.sceneNumber).padStart(2, "0")}-${String(shot.shotNumber).padStart(2, "0")} 尾帧`);
                      }}
                    />
                  : <small>{shot.productionMode === "first-last-continuous" ? "未生成" : "不需要"}</small>}
              </span>
              <span>{shot.shotSize} · {shot.cameraAngle}</span>
              <span>{shot.durationSeconds}s</span>
              <span className="storyboard-stage-status">
                <small className={shot.imagePrompt ? "ready" : ""}>图词</small>
                <small className={shot.production?.status === "completed" ? "ready" : ""}>图片</small>
                <small className={shot.videoPrompt ? "ready" : ""}>视词</small>
                <small className={shot.videoProduction?.status === "completed" ? "ready" : shot.videoProduction?.status ?? ""}>视频</small>
              </span>
            </button>
          ))}
        </div>
      )}
      </div>
    </div>
  );
}

const shotTextFieldLabels: Record<StoryboardShotTextField, string> = {
  visualDescription: "镜头描述",
  firstFrameDescription: "首帧描述",
  lastFrameDescription: "尾帧描述",
  cutDescription: "CUT 执行描述",
  dialogue: "对白",
  sound: "声音",
};

function EditableShotText({
  field,
  value,
  editingField,
  editingValue,
  saving,
  shotContext,
  rows = 4,
  emptyText = "暂无内容",
  onEdit,
  onEditingValueChange,
  onSave,
  onCancel,
}: {
  field: StoryboardShotTextField;
  value: string;
  editingField: StoryboardShotTextField | null;
  editingValue: string;
  saving: boolean;
  shotContext: unknown;
  rows?: number;
  emptyText?: string;
  onEdit: (field: StoryboardShotTextField, value: string) => void;
  onEditingValueChange: (value: string) => void;
  onSave: () => void;
  onCancel: () => void;
}) {
  const editing = editingField === field;
  const label = shotTextFieldLabels[field];
  return (
    <div className={`shot-editable-text${editing ? " editing" : ""}`}>
      {editing
        ? (
          <>
            <AgentTextArea
              agentId={builtInAgentIds.storyboardShotTextWriter}
              agentLabel={builtInAgentLabels.storyboardShotTextWriter}
              rows={rows}
              autoFocus
              value={editingValue}
              onChange={onEditingValueChange}
              context={{ targetField: field, targetLabel: label, shot: shotContext }}
              disabled={saving}
              maxLength={8000}
              aria-label={`编辑${label}`}
            />
            <div className="shot-text-edit-actions">
              <button className="secondary-button" type="button" disabled={saving} onClick={onCancel}>取消</button>
              <button className="primary-button" type="button" disabled={saving || editingValue.trim() === value.trim()} onClick={onSave}>
                <Save size={13} />{saving ? "保存中" : "保存"}
              </button>
            </div>
          </>
        )
        : (
          <>
            <p className={value ? "" : "empty"}>{value || emptyText}</p>
            <div className="shot-text-field-actions">
              <button className="icon-button" type="button" title={`编辑${label}`} aria-label={`编辑${label}`} onClick={() => onEdit(field, value)}>
                <Edit3 size={13} />
              </button>
            </div>
          </>
        )}
    </div>
  );
}

export function StoryboardShotPage() {
  const { projectId = "", productionEpisodeId = "", shotResourceId = "" } = useParams();
  const navigate = useNavigate();
  const [storyboard, setStoryboard] = useState<Storyboard | null>(null);
  const [visualAssets, setVisualAssets] = useState<VisualAsset[]>([]);
  const [associationDraft, setAssociationDraft] = useState<string[]>([]);
  const [assetToAdd, setAssetToAdd] = useState("");
  const [loaded, setLoaded] = useState(false);
  const [savingAssociations, setSavingAssociations] = useState(false);
  const [startingProduction, setStartingProduction] = useState(false);
  const [productionConfirmation, setProductionConfirmation] = useState(false);
  const [productionInstruction, setProductionInstruction] = useState("");
  const [video, setVideo] = useState<ShotVideoProduction | null>(null);
  const [videoConfirmation, setVideoConfirmation] = useState(false);
  const [videoInstruction, setVideoInstruction] = useState("");
  const [startingVideo, setStartingVideo] = useState(false);
  const [savingMode, setSavingMode] = useState(false);
  const [editingTextField, setEditingTextField] = useState<StoryboardShotTextField | null>(null);
  const [editingTextValue, setEditingTextValue] = useState("");
  const [savingText, setSavingText] = useState(false);
  const [textRewriteOpen, setTextRewriteOpen] = useState(false);
  const [textRewriteInstruction, setTextRewriteInstruction] = useState("");
  const [rewritingText, setRewritingText] = useState(false);
  const [framePreview, setFramePreview] = useState<{ url: string; label: string } | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      getStoryboard(projectId, productionEpisodeId, controller.signal),
      listVisualAssets(projectId, controller.signal),
      getShotVideo(projectId, productionEpisodeId, shotResourceId, controller.signal),
    ])
      .then(([loadedStoryboard, loadedAssets, loadedVideo]) => {
        setStoryboard(loadedStoryboard);
        setVisualAssets(loadedAssets);
        setVideo(loadedVideo);
        const shot = loadedStoryboard?.shots.find((item) => item.resourceId === shotResourceId);
        setAssociationDraft(shot?.linkedAssets.map((item) => item.resourceId) ?? []);
        setLoaded(true);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "镜头详情加载失败。");
        setLoaded(true);
      });
    return () => controller.abort();
  }, [productionEpisodeId, projectId, shotResourceId]);

  useEffect(() => {
    if (!video || !["queued", "running"].includes(video.status)) return;
    const controller = new AbortController();
    const timer = window.setInterval(() => {
      getShotVideo(projectId, productionEpisodeId, shotResourceId, controller.signal)
        .then((updated) => setVideo(updated))
        .catch((pollError: unknown) => {
          if (pollError instanceof DOMException && pollError.name === "AbortError") return;
          setError(pollError instanceof Error ? pollError.message : "视频状态更新失败。");
        });
    }, 2000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [productionEpisodeId, projectId, shotResourceId, video]);

  useEffect(() => {
    if (!framePreview) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setFramePreview(null);
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [framePreview]);

  const shot = storyboard?.shots.find((item) => item.resourceId === shotResourceId);
  const savedAssociations = shot?.linkedAssets.map((item) => item.resourceId) ?? [];
  const associationsDirty = [...associationDraft].sort().join("|")
    !== [...savedAssociations].sort().join("|");
  const associationItems = associationDraft
    .map((resourceId) => visualAssets.find((asset) => asset.resourceId === resourceId))
    .filter((asset): asset is VisualAsset => Boolean(asset));
  const availableAssets = visualAssets.filter((asset) => !associationDraft.includes(asset.resourceId));
  const addAssociation = () => {
    if (!assetToAdd) return;
    setAssociationDraft((current) => [...current, assetToAdd]);
    setAssetToAdd("");
  };
  const removeAssociation = (resourceId: string) => {
    setAssociationDraft((current) => current.filter((item) => item !== resourceId));
  };
  const saveAssociations = async () => {
    if (!shot) return;
    setSavingAssociations(true);
    setError("");
    try {
      const updated = await updateStoryboardShotAssets(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        associationDraft,
      );
      setStoryboard(updated);
      const updatedShot = updated.shots.find((item) => item.resourceId === shot.resourceId);
      setAssociationDraft(updatedShot?.linkedAssets.map((item) => item.resourceId) ?? []);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "镜头资产关联保存失败。");
    } finally {
      setSavingAssociations(false);
    }
  };
  const saveFrameMode = async (requiresLastFrame: boolean) => {
    if (!shot) return;
    setSavingMode(true);
    setError("");
    try {
      setStoryboard(await updateStoryboardShotMode(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        requiresLastFrame,
      ));
      setVideo(null);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "镜头帧策略保存失败。");
    } finally {
      setSavingMode(false);
    }
  };
  const rewriteShotText = async () => {
    if (!shot || !textRewriteInstruction.trim()) return;
    setRewritingText(true);
    setError("");
    try {
      setStoryboard(await rewriteStoryboardShotText(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        textRewriteInstruction.trim(),
      ));
      setVideo(null);
      setTextRewriteOpen(false);
      setTextRewriteInstruction("");
    } catch (rewriteError) {
      setError(rewriteError instanceof Error ? rewriteError.message : "镜头文本重新生成失败。");
    } finally {
      setRewritingText(false);
    }
  };
  const beginTextEdit = (field: StoryboardShotTextField, value: string) => {
    setEditingTextField(field);
    setEditingTextValue(value);
    setError("");
  };
  const saveShotText = async () => {
    if (!shot || !editingTextField) return;
    setSavingText(true);
    setError("");
    try {
      setStoryboard(await updateStoryboardShotText(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        editingTextField,
        editingTextValue,
      ));
      setVideo(null);
      setEditingTextField(null);
      setEditingTextValue("");
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "镜头文本保存失败。");
    } finally {
      setSavingText(false);
    }
  };
  const openTextRewrite = () => {
    setTextRewriteInstruction("");
    setTextRewriteOpen(true);
  };
  const createProductionPrompt = async () => {
    if (!shot) return;
    setStartingProduction(true);
    setError("");
    try {
      const imagePrompt = await generateStoryboardImagePrompt(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        productionInstruction,
      );
      setStoryboard((current) => current ? {
        ...current,
        shots: current.shots.map((item) => item.resourceId === shot.resourceId
          ? { ...item, imagePrompt }
          : item),
      } : current);
      setProductionConfirmation(false);
      setProductionInstruction("");
    } catch (promptError) {
      setError(promptError instanceof Error ? promptError.message : "图片提示词生成失败。");
    } finally {
      setStartingProduction(false);
    }
  };
  const startProduction = async () => {
    if (!shot || !shot.imagePrompt) return;
    setStartingProduction(true);
    setError("");
    try {
      const production = await generateStoryboardImage(
        projectId,
        productionEpisodeId,
        shot.resourceId,
      );
      setStoryboard((current) => current ? {
        ...current,
        shots: current.shots.map((item) => item.resourceId === shot.resourceId
          ? { ...item, production }
          : item),
      } : current);
    } catch (startError) {
      setError(startError instanceof Error ? startError.message : "镜头图片生成失败。");
    } finally {
      setStartingProduction(false);
    }
  };
  const createVideoPrompt = async () => {
    if (!shot) return;
    setStartingVideo(true);
    setError("");
    try {
      const videoPrompt = await generateStoryboardVideoPrompt(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        videoInstruction,
      );
      setStoryboard((current) => current ? {
        ...current,
        shots: current.shots.map((item) => item.resourceId === shot.resourceId
          ? { ...item, videoPrompt }
          : item),
      } : current);
      setVideoConfirmation(false);
      setVideoInstruction("");
    } catch (promptError) {
      setError(promptError instanceof Error ? promptError.message : "视频提示词生成失败。");
    } finally {
      setStartingVideo(false);
    }
  };
  const createVideo = async () => {
    if (!shot || !shot.videoPrompt) return;
    setStartingVideo(true);
    setError("");
    try {
      const started = await generateStoryboardVideo(
        projectId,
        productionEpisodeId,
        shot.resourceId,
      );
      setVideo(started);
    } catch (startError) {
      setError(startError instanceof Error ? startError.message : "镜头视频任务创建失败。");
    } finally {
      setStartingVideo(false);
    }
  };

  if (!loaded) {
    return <div className="source-empty-state development-empty-state"><strong>正在读取镜头详情</strong></div>;
  }
  if (!shot) {
    return (
      <div className="page">
        <div className="source-empty-state development-empty-state">
          <strong>{error || "未找到这个镜头。"}</strong>
          <button className="secondary-button" onClick={() => navigate("../..", { relative: "path" })}>返回镜头表</button>
        </div>
      </div>
    );
  }

  const shotCode = `S${String(shot.sceneNumber).padStart(2, "0")}-${String(shot.shotNumber).padStart(2, "0")}`;
  const shotIndex = storyboard?.shots.findIndex((item) => item.resourceId === shotResourceId) ?? -1;
  const previousShot = shotIndex > 0 ? storyboard?.shots[shotIndex - 1] : undefined;
  const nextShot = shotIndex >= 0 && shotIndex < (storyboard?.shots.length ?? 0) - 1
    ? storyboard?.shots[shotIndex + 1]
    : undefined;
  return (
    <div className="page full-height-page shot-detail-page">
      <WorkspaceHeaderExtension
        label={shotCode}
        actions={(
          <>
            <button
              className="secondary-button"
              type="button"
              disabled={!previousShot}
              onClick={() => previousShot && navigate(`../${previousShot.resourceId}`, { relative: "path" })}
            >
              <ChevronLeft size={14} /><span>上一个</span>
            </button>
            <button
              className="secondary-button"
              type="button"
              disabled={!nextShot}
              onClick={() => nextShot && navigate(`../${nextShot.resourceId}`, { relative: "path" })}
            >
              <span>下一个</span><ChevronRight size={14} />
            </button>
            <VersionPicker projectId={projectId} assetId={shot.assetId} label="镜头版本" />
            <button className="secondary-button" type="button" onClick={() => navigate("../..", { relative: "path" })}><List size={14} /><span>镜头表</span></button>
          </>
        )}
      />
      <div className="shot-detail-workspace">
        <div className="shot-detail-scroll">
          {error && <div className="source-empty-state development-empty-state"><strong>{error}</strong></div>}
          <section className="shot-workbench">
        <header>
          <div>
            <h2>{shotCode}</h2>
            <EditableShotText
              field="visualDescription"
              value={shot.visualDescription || shot.composition}
              editingField={editingTextField}
              editingValue={editingTextValue}
              saving={savingText}
              shotContext={shot}
              rows={3}
              onEdit={beginTextEdit}
              onEditingValueChange={setEditingTextValue}
              onSave={() => void saveShotText()}
              onCancel={() => setEditingTextField(null)}
            />
          </div>
          <div className="shot-production-actions">
            <button
              className="secondary-button"
              onClick={() => { setProductionInstruction(shot.imagePrompt?.instruction ?? ""); setProductionConfirmation(true); }}
              disabled={startingProduction || associationsDirty || associationDraft.length === 0}
            >
              <Sparkles size={14} />
              {startingProduction ? "处理中" : shot.imagePrompt ? "重生成图片提示词" : "生成图片提示词"}
            </button>
            <button
              className="primary-button"
              onClick={() => void startProduction()}
              disabled={!shot.imagePrompt
                || startingProduction
                || associationsDirty
                || associationDraft.length === 0
                || ["queued", "running"].includes(shot.production?.status ?? "")}
            >
              <ImagePlus size={14} />
              {startingProduction ? "处理中" : shot.production?.outputUrl ? "重新生成图片" : "生成图片"}
            </button>
          </div>
        </header>
        <section className={`shot-directing-analysis ${shot.productionMode === "first-last-continuous" ? "continuous" : "direct"}`} aria-label="镜头执行分析">
          <div className="shot-directing-toolbar">
            <div className="shot-frame-strategy-control">
              <div>
                <span className="eyebrow">帧策略</span>
                <strong>{shot.productionMode === "first-last-continuous" ? "需要首帧与尾帧" : "只需要首帧"}</strong>
                <small>{shot.frameStrategyReason}</small>
              </div>
              <label>
                <span>需要尾帧</span>
                <Switch
                  size="small"
                  checked={shot.productionMode === "first-last-continuous"}
                  loading={savingMode}
                  disabled={savingMode || rewritingText}
                  onChange={(checked) => void saveFrameMode(checked)}
                />
              </label>
            </div>
            <button
              className="secondary-button"
              type="button"
              onClick={openTextRewrite}
              disabled={rewritingText || savingMode}
            >
              <WandSparkles size={13} />按意见重写
            </button>
          </div>
          <div className="shot-frame-brief">
            <span className="eyebrow">首帧</span>
            <EditableShotText field="firstFrameDescription" value={shot.firstFrameDescription || shot.visualDescription} editingField={editingTextField} editingValue={editingTextValue} saving={savingText} shotContext={shot} rows={5} onEdit={beginTextEdit} onEditingValueChange={setEditingTextValue} onSave={() => void saveShotText()} onCancel={() => setEditingTextField(null)} />
          </div>
          {shot.productionMode === "first-last-continuous" && (
            <div className="shot-frame-brief last">
              <span className="eyebrow">尾帧</span>
              <EditableShotText field="lastFrameDescription" value={shot.lastFrameDescription} editingField={editingTextField} editingValue={editingTextValue} saving={savingText} shotContext={shot} rows={5} onEdit={beginTextEdit} onEditingValueChange={setEditingTextValue} onSave={() => void saveShotText()} onCancel={() => setEditingTextField(null)} />
            </div>
          )}
          <div className="shot-cut-description">
            <span className="eyebrow">CUT 执行描述</span>
            <EditableShotText field="cutDescription" value={shot.cutDescription || shot.action} editingField={editingTextField} editingValue={editingTextValue} saving={savingText} shotContext={shot} rows={5} onEdit={beginTextEdit} onEditingValueChange={setEditingTextValue} onSave={() => void saveShotText()} onCancel={() => setEditingTextField(null)} />
          </div>
          <div className="shot-audio-cues">
            <div><span className="eyebrow">对白</span><EditableShotText field="dialogue" value={shot.dialogue} editingField={editingTextField} editingValue={editingTextValue} saving={savingText} shotContext={shot} rows={4} onEdit={beginTextEdit} onEditingValueChange={setEditingTextValue} onSave={() => void saveShotText()} onCancel={() => setEditingTextField(null)} /></div>
            <div><span className="eyebrow">声音</span><EditableShotText field="sound" value={shot.sound} editingField={editingTextField} editingValue={editingTextValue} saving={savingText} shotContext={shot} rows={4} onEdit={beginTextEdit} onEditingValueChange={setEditingTextValue} onSave={() => void saveShotText()} onCancel={() => setEditingTextField(null)} /></div>
          </div>
        </section>
        {shot.hooks?.length > 0 && (
          <section className="shot-hook-details" aria-label="本镜头爆点">
            <header><span className="eyebrow">本镜头落实的爆点</span><strong>{shot.hooks.length} 项</strong></header>
            {shot.hooks.map((hook, index) => (
              <article className={hook.type} key={`${hook.type}-${index}`}>
                <b>{hook.type === "big" ? "大爆点" : "小爆点"}</b>
                <p>{hook.description}</p>
              </article>
            ))}
          </section>
        )}
        {(shot.production?.outputUrl || (shot.productionMode === "first-last-continuous" && shot.production?.lastFrameUrl)) && (
          <section className="shot-output-gallery" aria-label="已生成镜头帧">
            {shot.production.outputUrl && (
              <div className="shot-output-item">
                <div className="shot-output-frame-shell">
                  <button className="shot-output-frame" type="button" onClick={() => setFramePreview({ url: shot.production!.outputUrl!, label: `${shotCode} 首帧` })} title="预览首帧">
                    <img src={shot.production.outputUrl} alt={`${shotCode} 首帧`} />
                    <span>当前首帧</span>
                  </button>
                  <button
                    className="media-regenerate-button"
                    type="button"
                    onClick={() => void startProduction()}
                    disabled={!shot.imagePrompt || startingProduction}
                  >
                    <RefreshCw size={13} />{shot.productionMode === "first-last-continuous"
                      ? shot.production.lastFrameUrl ? "重新生成尾帧" : "生成尾帧"
                      : "重新生成"}
                  </button>
                </div>
                <div className="shot-output-meta">
                  {shot.production.outputAssetId && <VersionPicker projectId={projectId} assetId={shot.production.outputAssetId} label="首帧版本" />}
                  {shot.production.outputPrompt && (
                    <details><summary>首帧提示词</summary><textarea readOnly rows={6} value={shot.production.outputPrompt} /></details>
                  )}
                </div>
              </div>
            )}
            {shot.productionMode === "first-last-continuous" && shot.production.lastFrameUrl && (
              <div className="shot-output-item">
                <div className="shot-output-frame-shell">
                  <button className="shot-output-frame" type="button" onClick={() => setFramePreview({ url: shot.production!.lastFrameUrl!, label: `${shotCode} 尾帧` })} title="预览尾帧">
                    <img src={shot.production.lastFrameUrl} alt={`${shotCode} 尾帧`} />
                    <span>当前尾帧</span>
                  </button>
                  <button
                    className="media-regenerate-button"
                    type="button"
                    onClick={() => void startProduction()}
                    disabled={!shot.imagePrompt || startingProduction}
                  >
                    <RefreshCw size={13} />重新生成尾帧
                  </button>
                </div>
                <div className="shot-output-meta">
                  {shot.production.lastFrameAssetId && <VersionPicker projectId={projectId} assetId={shot.production.lastFrameAssetId} label="尾帧版本" />}
                  {shot.production.lastFramePrompt && (
                    <details><summary>尾帧提示词</summary><textarea readOnly rows={6} value={shot.production.lastFramePrompt} /></details>
                  )}
                </div>
              </div>
            )}
          </section>
        )}
        <section className="shot-video-panel" aria-label="镜头视频">
          <header>
            <div>
              <span className="eyebrow">MINIMAX H3 / TURBO 4-STEP</span>
              <strong>镜头视频</strong>
              <p>{video?.status === "failed" ? video.error : video?.status === "completed" ? `当前视频 v${video.version}` : video ? productionStatusLabel(video.status) : "尚未生成"}</p>
            </div>
            <div className="shot-production-actions">
              {video && <span className={`state-label ${video.status}`}>{productionStatusLabel(video.status)}</span>}
              <button
                className="secondary-button"
                type="button"
                onClick={() => { setVideoInstruction(shot.videoPrompt?.instruction ?? ""); setVideoConfirmation(true); }}
                disabled={!shot.production?.outputAssetId || startingVideo}
              >
                <Sparkles size={14} />{startingVideo ? "处理中" : shot.videoPrompt ? "重生成视频提示词" : "生成视频提示词"}
              </button>
              <button
                className="primary-button"
                type="button"
                onClick={() => void createVideo()}
                disabled={!shot.videoPrompt || startingVideo || ["queued", "running"].includes(video?.status ?? "")}
              >
                <Play size={14} />
                {startingVideo ? "处理中" : ["queued", "running"].includes(video?.status ?? "") ? "正在生成" : video?.url ? "重新生成视频" : "生成视频"}
              </button>
              {video && <NavLink className="secondary-button" to={`/projects/${projectId}/production/runs/${video.runId}`}>查看运行</NavLink>}
            </div>
          </header>
          {shot.videoPrompt && !video?.url && (
            <details className="shot-saved-prompt" open={!video?.url}>
              <summary>当前视频提示词 · v{shot.videoPrompt.version}</summary>
              <textarea readOnly rows={8} value={shot.videoPrompt.prompt} />
            </details>
          )}
          {video?.url && (
            <div className="shot-video-output">
              <div className="shot-video-shell">
                <video controls preload="metadata" src={video.url}>
                  <track kind="captions" />
                </video>
                <button
                  className="media-regenerate-button"
                  type="button"
                  onClick={() => void createVideo()}
                  disabled={!shot.videoPrompt || startingVideo}
                >
                  <RefreshCw size={13} />重新生成
                </button>
              </div>
              <div className="shot-output-meta">
                {video.assetId && <VersionPicker projectId={projectId} assetId={video.assetId} label="视频版本" />}
                <details><summary>视频提示词</summary><textarea readOnly rows={8} value={video.prompt} /></details>
              </div>
            </div>
          )}
        </section>
        <div className="shot-associations">
          <div className="shot-association-heading">
            <div>
              <span className="eyebrow">当前镜头所需资产</span>
              <strong>{associationItems.length} 项</strong>
            </div>
            {associationsDirty && <span className="state-label waiting">有未保存更改</span>}
          </div>
          <div className="shot-association-list">
            {associationItems.map((asset) => (
              <article className="shot-association-card" key={asset.resourceId}>
                {asset.referenceImage
                  ? (
                    <button
                      className={`shot-association-thumb previewable ${asset.kind}`}
                      type="button"
                      title={`预览${asset.name}参考图`}
                      onClick={() => setFramePreview({ url: asset.referenceImage!.contentUrl, label: `${asset.name}参考图` })}
                    >
                      <img src={asset.referenceImage.contentUrl} alt={`${asset.name}参考图`} />
                    </button>
                  )
                  : <span className={`shot-association-thumb ${asset.kind}`}><b>{asset.name.slice(0, 1)}</b></span>}
                <div>
                  <strong>{asset.name}</strong>
                  <small>{visualAssetKindLabel(asset.kind)} · {asset.referenceImage ? `参考图 v${asset.referenceImage.version}` : "暂无参考图"}</small>
                  <p>{asset.summary || asset.visualDescription}</p>
                </div>
                <button
                  className="icon-button"
                  type="button"
                  title={associationDraft.length === 1 ? "每个镜头至少保留一个资产" : `移除${asset.name}`}
                  aria-label={`移除${asset.name}`}
                  disabled={associationDraft.length === 1}
                  onClick={() => removeAssociation(asset.resourceId)}
                >
                  <X size={14} />
                </button>
              </article>
            ))}
          </div>
          <div className="shot-association-actions">
            <label className="select-control">
              <span>添加资产</span>
              <select value={assetToAdd} onChange={(event) => setAssetToAdd(event.target.value)}>
                <option value="">选择人物、场景或特殊道具</option>
                {(["character", "scene", "prop"] as const).map((kind) => (
                  <optgroup label={visualAssetKindLabel(kind)} key={kind}>
                    {availableAssets.filter((asset) => asset.kind === kind).map((asset) => (
                      <option value={asset.resourceId} key={asset.resourceId}>{asset.name}</option>
                    ))}
                  </optgroup>
                ))}
              </select>
              <ChevronDown size={13} />
            </label>
            <button className="secondary-button" type="button" onClick={addAssociation} disabled={!assetToAdd}>
              <Plus size={14} />
              添加
            </button>
            <button className="secondary-button" onClick={saveAssociations} disabled={!associationsDirty || savingAssociations || associationDraft.length === 0}>
              <Save size={14} />
              {savingAssociations ? "正在保存" : "保存更改"}
            </button>
          </div>
        </div>
          </section>
        </div>
      </div>
      {framePreview && (
        <div className="modal-backdrop shot-frame-preview-backdrop" role="presentation" onMouseDown={() => setFramePreview(null)}>
          <div className="shot-frame-preview-dialog" role="dialog" aria-modal="true" aria-label={`${framePreview.label}预览`} onMouseDown={(event) => event.stopPropagation()}>
            <header>
              <strong>{framePreview.label}</strong>
              <button className="icon-button" type="button" aria-label="关闭预览" title="关闭预览" autoFocus onClick={() => setFramePreview(null)}><X size={18} /></button>
            </header>
            <img src={framePreview.url} alt={`${framePreview.label}大图预览`} />
          </div>
        </div>
      )}
      {textRewriteOpen && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => !rewritingText && setTextRewriteOpen(false)}>
          <form
            className="dialog ai-assist-dialog shot-feedback-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => { event.preventDefault(); void rewriteShotText(); }}
          >
            <span className="eyebrow">分镜 Agent / {shotCode}</span>
            <h2>按意见重写镜头文本</h2>
            <p>只调整当前镜头的画面、首尾帧、CUT、对白与声音描述，不改变镜号、时长、景别、人物和道具。</p>
            <label>
              <span>修改意见</span>
              <textarea
                rows={5}
                autoFocus
                value={textRewriteInstruction}
                onChange={(event) => setTextRewriteInstruction(event.target.value)}
                placeholder="例如：首帧更突出人物迟疑，CUT 节奏放缓，并明确结尾视线落点"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" disabled={rewritingText} onClick={() => setTextRewriteOpen(false)}>取消</button>
              <button className="primary-button" type="submit" disabled={rewritingText || !textRewriteInstruction.trim()}>
                <WandSparkles size={13} />{rewritingText ? "正在重写" : "生成新版本"}
              </button>
            </div>
          </form>
        </div>
      )}
      {videoConfirmation && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setVideoConfirmation(false)}>
          <form
            className="dialog ai-assist-dialog generation-confirmation-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              void createVideoPrompt();
            }}
          >
            <span className="eyebrow">MINIMAX H3 / {shotCode}</span>
            <h2>{shot.videoPrompt ? "重新生成视频提示词" : "生成视频提示词"}</h2>
            <p>根据当前镜头文本、项目设定和已生成关键帧保存一版视频提示词。此步骤不会启动视频生成。</p>
            <label className="generation-instruction-field">
              <span>本次修改意见（可选）</span>
              <textarea
                rows={4}
                value={videoInstruction}
                onChange={(event) => setVideoInstruction(event.target.value)}
                placeholder="例如：动作更克制，固定机位，结尾多停留半秒"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" onClick={() => setVideoConfirmation(false)}>取消</button>
              <button className="primary-button" type="submit" disabled={startingVideo}>
                <Sparkles size={13} />
                {startingVideo ? "正在生成" : "生成提示词"}
              </button>
            </div>
          </form>
        </div>
      )}
      {productionConfirmation && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setProductionConfirmation(false)}>
          <form
            className="dialog ai-assist-dialog generation-confirmation-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              void createProductionPrompt();
            }}
          >
            <span className="eyebrow">图像模型 / {shotCode}</span>
            <h2>{shot.imagePrompt ? "重新生成图片提示词" : "生成图片提示词"}</h2>
            <p>根据当前镜头、项目设定和关联资产保存一版图片提示词。此步骤不会生成图片。</p>
            <label className="generation-instruction-field">
              <span>本次修改意见（可选）</span>
              <textarea
                rows={4}
                value={productionInstruction}
                onChange={(event) => setProductionInstruction(event.target.value)}
                placeholder="例如：人物表情更克制，晨光更柔和，保持当前场景构图"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" onClick={() => setProductionConfirmation(false)}>取消</button>
              <button className="primary-button" type="submit" disabled={startingProduction}>
                <Sparkles size={13} />
                {startingProduction ? "正在生成" : "生成提示词"}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

function GenerationPreviewDetails({ preview }: { preview: ImageGenerationPreview }) {
  const { parameters } = preview;
  return (
    <div className="generation-preview">
      <label>
        <span>完整提示词</span>
        <textarea readOnly rows={9} value={preview.prompt} />
      </label>
      <dl className="generation-parameters">
        <div><dt>模型</dt><dd>{parameters.deployment}</dd></div>
        <div><dt>质量</dt><dd>{parameters.quality}</dd></div>
        <div><dt>模型尺寸</dt><dd>{parameters.modelSize}</dd></div>
        <div><dt>输出</dt><dd>{parameters.outputWidth} × {parameters.outputHeight} · {parameters.outputFormat}</dd></div>
        {parameters.productionMode && <div><dt>模式</dt><dd>{productionModeLabel(parameters.productionMode)}</dd></div>}
        {parameters.durationSeconds != null && <div><dt>时长</dt><dd>{parameters.durationSeconds} 秒</dd></div>}
        {parameters.stages?.length ? <div><dt>阶段</dt><dd>{parameters.stages.map(productionStageLabel).join("、")}</dd></div> : null}
      </dl>
      <section className="generation-references">
        <strong>输入资产与锁定版本</strong>
        {preview.references.map((reference) => (
          <article key={`${reference.assetId}-${reference.role}`}>
            {reference.contentUrl ? <img src={reference.contentUrl} alt="" /> : <span>{reference.name.slice(0, 1)}</span>}
            <div><b>{reference.name}</b><small>{reference.type} · v{reference.version} · {reference.role}</small></div>
          </article>
        ))}
      </section>
    </div>
  );
}

function visualAssetKindLabel(kind: VisualAssetKind) {
  return ({ character: "人物", scene: "场景", prop: "特殊道具" } as const)[kind];
}

function productionStatusLabel(status: string) {
  return ({
    draft: "未制作",
    queued: "排队中",
    running: "制作中",
    completed: "已完成",
    failed: "失败",
    waiting: "等待",
  } as Record<string, string>)[status] ?? status;
}

function productionStageLabel(stage: string) {
  return ({
    "first-frame": "首帧",
    "last-frame": "尾帧",
    "shot-video": "镜头视频",
  } as Record<string, string>)[stage] ?? stage;
}

function productionModeLabel(mode: string) {
  return mode === "direct-first-frame" ? "直接首帧" : mode === "first-last-continuous" ? "首尾帧连续" : mode;
}

function localDateTime(value: string) {
  return new Intl.DateTimeFormat("zh-CN", { dateStyle: "short", timeStyle: "medium" }).format(new Date(value));
}

export function ProductionPage() {
  const { projectId = "", productionEpisodeId = "" } = useParams();
  const navigate = useNavigate();
  const [runs, setRuns] = useState<ProductionRun[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    listProductionRuns(projectId, productionEpisodeId || undefined, controller.signal)
      .then((loadedRuns) => {
        setRuns(loadedRuns);
        setLoaded(true);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "生产运行加载失败。");
        setLoaded(true);
      });
    return () => controller.abort();
  }, [productionEpisodeId, projectId]);

  const columns: ProColumns<ProductionRun>[] = [
    {
      title: "生产集",
      dataIndex: "episodeNumber",
      width: 72,
      render: (_, run) => <strong className="production-table-episode">E{String(run.episodeNumber).padStart(2, "0")}</strong>,
    },
    {
      title: "镜头 / 剧集",
      dataIndex: "episodeTitle",
      width: 360,
      ellipsis: true,
      render: (_, run) => (
        <span className="production-table-subject">
          <strong>{[...new Set(run.items.map((item) => item.shotName).filter(Boolean))].join("、") || run.episodeTitle}</strong>
          <small>{run.episodeTitle}</small>
        </span>
      ),
    },
    {
      title: "状态",
      dataIndex: "status",
      width: 100,
      render: (_, run) => <span className={`state-label ${run.status}`}>{productionStatusLabel(run.status)}</span>,
    },
    {
      title: "创建时间",
      dataIndex: "createdAtUtc",
      width: 150,
      render: (_, run) => localDateTime(run.createdAtUtc),
    },
  ];
  return (
    <div className="page production-page">
      {error && <div className="source-empty-state development-empty-state"><strong>{error}</strong></div>}
      <ProTable<ProductionRun>
        className="production-pro-table"
        rowKey="id"
        columns={columns}
        dataSource={runs}
        loading={!loaded}
        size="small"
        search={false}
        options={false}
        cardProps={{ bordered: false }}
        scroll={{ x: 682 }}
        pagination={{
          pageSize: 20,
          showSizeChanger: false,
          showTotal: (total) => `共 ${total} 条`,
          size: "small",
        }}
        locale={{ emptyText: error || "尚无生产运行。" }}
        onRow={(run) => ({
          onClick: () => navigate(`/projects/${projectId}/production/runs/${run.id}`),
        })}
      />
    </div>
  );
}

export function ProductionRunPage() {
  const { projectId = "", runId = "" } = useParams();
  const navigate = useNavigate();
  const [run, setRun] = useState<ProductionRun | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getProductionRun(projectId, runId, controller.signal)
      .then((loadedRun) => {
        setRun(loadedRun);
        if (loadedRun) {
          window.dispatchEvent(new CustomEvent("alex:production-run-episode", {
            detail: { productionEpisodeId: loadedRun.productionEpisodeId },
          }));
        }
        setLoaded(true);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "生产运行详情加载失败。");
        setLoaded(true);
      });
    return () => controller.abort();
  }, [projectId, runId]);

  if (!loaded) return <div className="source-empty-state development-empty-state"><strong>正在读取生产运行</strong></div>;
  if (!run) {
    return <div className="source-empty-state development-empty-state"><strong>{error || "未找到生产运行。"}</strong></div>;
  }

  return (
    <div className="page">
      <PageTitle
        eyebrow={`媒体生产 / E${String(run.episodeNumber).padStart(2, "0")}`}
        title={run.episodeTitle}
        description={`${productionModeLabel(run.mode)} · 创建于 ${localDateTime(run.createdAtUtc)}`}
        action={<button className="secondary-button" onClick={() => navigate(`/projects/${projectId}/production/episodes/${run.productionEpisodeId}`)}>返回生产中心</button>}
      />
      {(error || run.lastError) && <div className="source-empty-state development-empty-state"><strong>{error || run.lastError}</strong></div>}
      <div className="production-summary">
        <div><span className="eyebrow">状态</span><strong>{productionStatusLabel(run.status)}</strong><small>Run</small></div>
        <div><span className="eyebrow">当前阶段</span><strong>{productionStageLabel(run.currentStage)}</strong><small>阶段</small></div>
        <div><span className="eyebrow">任务项</span><strong>{run.items.length}</strong><small>镜头阶段</small></div>
        <div><span className="eyebrow">完成</span><strong className="online">{run.items.filter((item) => item.status === "completed").length}</strong><small>任务项</small></div>
      </div>
      <section className="production-table panel">
        <header className="panel-header"><h2>运行任务</h2><span>{run.originalInstruction}</span></header>
        {run.items.map((item) => (
          <div className="production-run-item" key={item.id}>
            <div className="production-row">
              <span className="episode-code large">{productionStageLabel(item.stage)}</span>
              <span className="production-title">
                <strong>{item.shotName}</strong>
                <small>尝试 {item.attempt} 次 · {localDateTime(item.createdAtUtc)}</small>
              </span>
              <span className={`state-label ${item.status}`}>{productionStatusLabel(item.status)}</span>
              <NavLink className="secondary-button" to={`/projects/${projectId}/storyboard/episodes/${run.productionEpisodeId}/shots/${item.shotResourceId}`}>镜头详情</NavLink>
            </div>
            {item.errorDetail && <div className="source-empty-state"><strong>{item.errorCode || "制作失败"}</strong><span>{item.errorDetail}</span></div>}
            {item.outputUrl && (
              <a className="shot-output-frame" href={item.outputUrl} target="_blank" rel="noreferrer" title="打开生产输出">
                <img src={item.outputUrl} alt={`${item.shotName} ${productionStageLabel(item.stage)}`} />
                <span>{productionStageLabel(item.stage)}输出</span>
              </a>
            )}
          </div>
        ))}
      </section>
    </div>
  );
}

export function ReferencesPage() {
  const [filter, setFilter] = useState("全部");
  const references = [
    { name: "林墨 · 标准像", type: "人物" },
    { name: "周岚 · 标准像", type: "人物" },
    { name: "天桥食堂 · 外景", type: "场景" },
    { name: "二楼房间 · 全景", type: "场景" },
    { name: "红色文件袋 · 正面", type: "道具" },
    { name: "旧照片 · 展开", type: "道具" },
  ].map((item, index) => ({
    ...item,
    index,
    status: index % 3 !== 1 ? "可用" : "待生成",
  }));
  const visibleReferences = references.filter(
    (item) => filter === "全部" || item.type === filter,
  );
  return (
    <div className="page">
      <PageTitle
        eyebrow="项目共享资产"
        title="视觉参考"
        description="人物、场景和道具参考图在各生产集之间共享"
        action={
          <div className="button-group">
            <label className="secondary-button file-button">
              <Upload size={14} />
              上传设定图
              <input type="file" />
            </label>
            <button className="primary-button">
              <WandSparkles size={14} />
              生成缺失参考
            </button>
          </div>
        }
      />
      <div className="workspace-toolbar">
        <div className="segmented">
          {["全部", "人物", "场景", "道具"].map((item) => (
            <button
              className={filter === item ? "active" : ""}
              onClick={() => setFilter(item)}
              key={item}
            >
              {item}
            </button>
          ))}
        </div>
        <span className="coverage">覆盖 <strong>14 / 18</strong></span>
      </div>
      <div className="reference-grid">
        {visibleReferences.map((item) => (
          <button
            className="reference-card"
            key={item.name}
          >
            <div className={`reference-visual ref-${item.index}`}>
              <span>{item.status}</span>
            </div>
            <strong>{item.name}</strong>
            <small>
              {item.type} · 被 {item.index + 4} 个 shot 使用
            </small>
          </button>
        ))}
      </div>
    </div>
  );
}

export function ReviewPage() {
  const { projectId = "", productionEpisodeId = "" } = useParams();
  const navigate = useNavigate();
  const videoRef = useRef<HTMLVideoElement>(null);
  const [productionEpisodes, setProductionEpisodes] = useState<ProductionEpisodeRecord[]>([]);
  const [storyboard, setStoryboard] = useState<Storyboard | null>(null);
  const [videos, setVideos] = useState<Record<string, ShotVideoProduction | null>>({});
  const [activeShotId, setActiveShotId] = useState("");
  const [playIntent, setPlayIntent] = useState(false);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [videoAspectRatio, setVideoAspectRatio] = useState("9 / 16");
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");
  const [exporting, setExporting] = useState(false);
  const [exportProgress, setExportProgress] = useState(0);
  const [exportResult, setExportResult] = useState<{ url: string; fileName: string } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    listProductionEpisodes(projectId, controller.signal)
      .then((items) => {
        setProductionEpisodes(items);
        if (!productionEpisodeId && items[0]) {
          navigate(`/projects/${projectId}/review/episodes/${items[0].id}`, { replace: true });
        }
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "生产剧集加载失败。");
        setLoaded(true);
      });
    return () => controller.abort();
  }, [navigate, productionEpisodeId, projectId]);

  useEffect(() => {
    if (!productionEpisodeId) return;
    const controller = new AbortController();
    getStoryboard(projectId, productionEpisodeId, controller.signal)
      .then(async (loadedStoryboard) => {
        if (!loadedStoryboard) throw new Error("当前剧集还没有分镜，无法开始审阅。");
        const shotVideos = await Promise.all(loadedStoryboard.shots.map(async (shot) => [
          shot.resourceId,
          await getShotVideo(projectId, productionEpisodeId, shot.resourceId, controller.signal),
        ] as const));
        if (controller.signal.aborted) return;
        const loadedVideos = Object.fromEntries(shotVideos);
        const firstPlayable = loadedStoryboard.shots.find((shot) => loadedVideos[shot.resourceId]?.url);
        setStoryboard(loadedStoryboard);
        setVideos(loadedVideos);
        setActiveShotId(firstPlayable?.resourceId ?? "");
        setError("");
        setElapsedSeconds(0);
        setLoaded(true);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "审阅数据加载失败。");
        setLoaded(true);
      });
    return () => controller.abort();
  }, [productionEpisodeId, projectId]);

  const currentStoryboard = storyboard?.productionEpisodeId === productionEpisodeId ? storyboard : null;
  const reviewShots = (currentStoryboard?.shots ?? [])
    .map((shot) => ({ shot, video: videos[shot.resourceId] }))
    .filter((item): item is typeof item & { video: ShotVideoProduction & { url: string } } => Boolean(item.video?.url));
  const activeIndex = reviewShots.findIndex((item) => item.shot.resourceId === activeShotId);
  const activeItem = activeIndex >= 0 ? reviewShots[activeIndex] : reviewShots[0];
  const totalDurationSeconds = reviewShots.reduce((total, item) => total + item.shot.durationSeconds, 0);
  const activeOffsetSeconds = activeIndex > 0
    ? reviewShots.slice(0, activeIndex).reduce((total, item) => total + item.shot.durationSeconds, 0)
    : 0;
  const progress = totalDurationSeconds > 0 ? Math.min(100, (elapsedSeconds / totalDurationSeconds) * 100) : 0;
  const activeCode = activeItem
    ? `S${String(activeItem.shot.sceneNumber).padStart(2, "0")}-${String(activeItem.shot.shotNumber).padStart(2, "0")}`
    : "";
  const episode = productionEpisodes.find((item) => item.id === productionEpisodeId);

  const changeEpisode = (episodeId: string) => {
    setLoaded(false);
    setError("");
    setStoryboard(null);
    setVideos({});
    setPlayIntent(false);
    setExportResult((current) => {
      if (current) URL.revokeObjectURL(current.url);
      return null;
    });
    navigate(`/projects/${projectId}/review/episodes/${episodeId}`);
  };

  useEffect(() => {
    if (!playIntent || !activeItem) return;
    void videoRef.current?.play().catch(() => setPlayIntent(false));
  }, [activeItem, playIntent]);

  const selectShot = (shotResourceId: string, shouldPlay = false) => {
    const index = reviewShots.findIndex((item) => item.shot.resourceId === shotResourceId);
    const offset = index > 0
      ? reviewShots.slice(0, index).reduce((total, item) => total + item.shot.durationSeconds, 0)
      : 0;
    setActiveShotId(shotResourceId);
    setElapsedSeconds(offset);
    setPlayIntent(shouldPlay);
  };

  const playNext = () => {
    const next = reviewShots[activeIndex + 1];
    if (!next) {
      setPlayIntent(false);
      setElapsedSeconds(totalDurationSeconds);
      return;
    }
    selectShot(next.shot.resourceId, true);
  };

  const exportEpisode = async () => {
    if (!episode || reviewShots.length === 0) return;
    setExporting(true);
    setExportProgress(0);
    setError("");
    setExportResult((current) => {
      if (current) URL.revokeObjectURL(current.url);
      return null;
    });
    try {
      const { exportEpisodeVideo } = await import("../features/review/exportEpisodeVideo");
      const blob = await exportEpisodeVideo(
        reviewShots.map((item) => ({ url: item.video.url })),
        setExportProgress,
      );
      setExportResult({
        url: URL.createObjectURL(blob),
        fileName: `E${String(episode.episodeNumber).padStart(2, "0")}-${episode.title}.mp4`,
      });
    } catch (exportError) {
      setError(exportError instanceof Error ? exportError.message : "成片导出失败。");
    } finally {
      setExporting(false);
    }
  };

  return (
    <div className="page review-page">
      <div className="review-toolbar">
        <div className="button-group">
            <label className="select-control review-episode-select">
              <span>剧集</span>
              <select
                value={productionEpisodeId}
                onChange={(event) => changeEpisode(event.target.value)}
              >
                {productionEpisodes.map((item) => (
                  <option key={item.id} value={item.id}>
                    E{String(item.episodeNumber).padStart(2, "0")} · {item.title}
                  </option>
                ))}
              </select>
              <ChevronDown size={13} />
            </label>
            <button
              className="primary-button"
              type="button"
              disabled={exporting || reviewShots.length === 0}
              onClick={() => void exportEpisode()}
            >
              <Download size={14} />
              {exporting ? `正在生成 ${exportProgress}%` : exportResult ? "重新生成" : "生成成片"}
            </button>
            {exportResult && (
              <a className="primary-button" href={exportResult.url} download={exportResult.fileName}>
                <Download size={14} />
                下载成片
              </a>
            )}
        </div>
      </div>
      {error && <div className="source-empty-state development-empty-state"><CircleAlert size={22} /><strong>{error}</strong></div>}
      {!error && !loaded && <div className="source-empty-state development-empty-state"><RefreshCw className="spin" size={22} /><strong>正在加载审阅视频...</strong></div>}
      {!error && loaded && reviewShots.length === 0 && (
        <div className="source-empty-state development-empty-state">
          <CircleAlert size={22} />
          <strong>当前剧集还没有已生成的视频</strong>
          <span>请先进入分镜镜头生成视频，再回到这里连续审阅。</span>
        </div>
      )}
      {!error && activeItem && (
        <div className="review-layout">
          <section className="player-panel">
            <div className="review-video-frame" style={{ aspectRatio: videoAspectRatio }}>
              <video
                key={activeItem.shot.resourceId}
                ref={videoRef}
                controls
                preload="auto"
                poster={activeItem.shot.production?.outputUrl ?? undefined}
                src={activeItem.video.url}
                onLoadedMetadata={(event) => {
                  const { videoWidth, videoHeight } = event.currentTarget;
                  if (videoWidth > 0 && videoHeight > 0) setVideoAspectRatio(`${videoWidth} / ${videoHeight}`);
                }}
                onPlay={() => setPlayIntent(true)}
                onPause={(event) => {
                  if (!event.currentTarget.ended) setPlayIntent(false);
                }}
                onTimeUpdate={(event) => setElapsedSeconds(activeOffsetSeconds + event.currentTarget.currentTime)}
                onEnded={playNext}
              />
            </div>
            <div className="review-now-playing">
              <div>
                <strong>{activeCode}</strong>
                <span>{activeItem.shot.visualDescription}</span>
              </div>
              <button
                className="secondary-button"
                type="button"
                onClick={() => navigate(`/projects/${projectId}/storyboard/episodes/${productionEpisodeId}/shots/${activeItem.shot.resourceId}`)}
              >
                <RefreshCw size={13} />
                进入镜头重新生成
              </button>
            </div>
            <div className="review-timebar">
              <span>{formatReviewTime(elapsedSeconds)}</span>
              <div className="review-timebar-track">
                <i style={{ width: `${progress}%` }} />
                <b style={{ left: `${progress}%` }} />
              </div>
              <span>{formatReviewTime(totalDurationSeconds)}</span>
            </div>
            <div className="review-shot-progress" aria-label="镜头进度">
              {reviewShots.map((item) => {
                const code = `S${String(item.shot.sceneNumber).padStart(2, "0")}-${String(item.shot.shotNumber).padStart(2, "0")}`;
                return (
                  <button
                    className={item.shot.resourceId === activeItem.shot.resourceId ? "active" : ""}
                    style={{ flexGrow: Math.max(1, item.shot.durationSeconds) }}
                    type="button"
                    title={`${code} · ${item.shot.durationSeconds.toFixed(1)} 秒`}
                    onClick={() => selectShot(item.shot.resourceId)}
                    key={item.shot.resourceId}
                  >
                    <span>{code}</span>
                  </button>
                );
              })}
            </div>
            <div className="review-shot-list">
              {reviewShots.map((item) => {
                const code = `S${String(item.shot.sceneNumber).padStart(2, "0")}-${String(item.shot.shotNumber).padStart(2, "0")}`;
                return (
                  <button
                    className={item.shot.resourceId === activeItem.shot.resourceId ? "active" : ""}
                    type="button"
                    onClick={() => selectShot(item.shot.resourceId, true)}
                    key={item.shot.resourceId}
                  >
                    <span className="review-shot-thumbnail" style={{ aspectRatio: videoAspectRatio }}>
                      {item.shot.production?.outputUrl && <img src={item.shot.production.outputUrl} alt="" />}
                    </span>
                    <strong>{code}</strong>
                    <small>{formatReviewTime(item.shot.durationSeconds)}</small>
                  </button>
                );
              })}
            </div>
          </section>
        </div>
      )}
    </div>
  );
}

function formatReviewTime(seconds: number) {
  const safeSeconds = Number.isFinite(seconds) ? Math.max(0, Math.floor(seconds)) : 0;
  const minutes = Math.floor(safeSeconds / 60);
  return `${String(minutes).padStart(2, "0")}:${String(safeSeconds % 60).padStart(2, "0")}`;
}
