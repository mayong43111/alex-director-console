import { Fragment, useEffect, useRef, useState, type FormEvent } from "react";
import {
  Navigate,
  NavLink,
  useLocation,
  useNavigate,
  useParams,
} from "react-router-dom";
import { Image, Select } from "antd";
import {
  Check,
  ChevronDown,
  CircleAlert,
  Copy,
  Edit3,
  Eye,
  AudioLines,
  ImagePlus,
  MoreHorizontal,
  Play,
  Plus,
  RefreshCw,
  Save,
  Search,
  Sparkles,
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
  generateVoiceReference,
  generateVisualReference,
  getVoiceProfile,
  importStoryMaterialAssets,
  listAudioMaterials,
  listVisualAssets,
  saveVoiceProfile,
  uploadAudioMaterial,
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
  generateStoryboard,
  getShotVideo,
  getStoryboard,
  previewShotProduction,
  previewShotVideo,
  startShotProduction,
  startShotVideo,
  type Storyboard,
  type ShotVideoPreview,
  type ShotVideoProduction,
  updateStoryboardShotAssets,
} from "../api/storyboards";
import type { ImageGenerationPreview } from "../api/generation";
import {
  getProductionRun,
  listProductionRuns,
  type ProductionRun,
} from "../api/production";
import {
  analyzeStoryMaterial,
  appendAdaptationEpisode,
  appendProjectSourceChapters,
  confirmAdaptationScript,
  createProjectSource,
  generateAdaptationScript,
  getAdaptationScript,
  getProductionScriptPackage,
  getStoryMaterialAnalysis,
  listProjectSources,
  regenerateAdaptationEpisode,
  regenerateProductionScript,
  type AdaptationScript,
  type ProductionScriptPackage,
  type ProjectSource,
  type StoryMaterialAnalysis,
} from "../api/projectSources";
import { VersionPicker } from "../components/VersionPicker";
import { RelationGraph } from "../components/RelationGraph";

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
      navigate(`${projectBase}/script/episodes/${episodeId}`);
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
  protagonistSpecies: "主角物种",
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
  const [error, setError] = useState<string | null>(null);

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
        title={hasContent ? "使用 GPT-5.4 调整当前内容" : "使用 GPT-5.4 生成内容"}
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
      <header className="page-header settings-page-header">
        <div>
          <div className="settings-title-line">
            <h1>项目设定</h1>
            <small>{settings.version === 0 ? "草稿" : `v${settings.version}`}</small>
            {settings.assetId && <VersionPicker compact projectId={projectId} assetId={settings.assetId} label="项目设定版本" />}
          </div>
        </div>
        <button
          className="primary-button settings-header-save"
          type="submit"
          form="project-settings-form"
          disabled={status === "saving" || status === "idle" || status === "saved"}
        >
          <Save size={14} />
          {status === "saving" ? "正在保存" : `保存为 v${settings.version + 1}`}
        </button>
      </header>
      <div className="settings-layout">
        <aside className="subnav">
          {settingSections.map((item, index) => (
            <button
              type="button"
              className={activeSection === item.id ? "active" : ""}
              key={item.id}
              onClick={() => openSection(item.id)}
            >
              <b>{String(index + 1).padStart(2, "0")}</b>
              {item.label}
            </button>
          ))}
        </aside>
        <form className="editor-surface settings-editor" id="project-settings-form" onSubmit={submitSettings}>
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
              <span className={`saved-state ${status}`}>
                <Check size={13} />
                {status === "dirty" ? "有未保存修改" : status === "saved" ? "新版本已保存" : `v${settings.version} 已同步`}
              </span>
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
                <textarea rows={3} maxLength={4000} value={settings.description} onChange={(event) => updateField("description", event.target.value)} />
              </label>
              <label className="span-2">
                <span>目标受众</span>
                <input required maxLength={300} value={settings.targetAudience} onChange={(event) => updateField("targetAudience", event.target.value)} />
              </label>
              <label>
                <span>计划生产集数</span>
                <input required type="number" min={1} max={1000} value={settings.plannedEpisodeCount} onChange={(event) => updateField("plannedEpisodeCount", Number(event.target.value))} />
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
              <label>
                <span>主角物种</span>
                <div className="ai-field-control">
                  <input required maxLength={200} value={settings.protagonistSpecies} onChange={(event) => updateField("protagonistSpecies", event.target.value)} />
                  {renderAiFieldAction("protagonistSpecies")}
                </div>
              </label>
              <label className="span-2">
                <span>美术方向</span>
                <div className="ai-field-control">
                  <textarea rows={4} maxLength={2000} value={settings.artDirection} onChange={(event) => updateField("artDirection", event.target.value)} />
                  {renderAiFieldAction("artDirection")}
                </div>
              </label>
              <label className="span-2">
                <span>角色造型硬约束</span>
                <div className="ai-field-control">
                  <textarea required rows={4} maxLength={1000} value={settings.characterDesign} onChange={(event) => updateField("characterDesign", event.target.value)} />
                  {renderAiFieldAction("characterDesign")}
                </div>
                <small>主人公的物种、体型、服装和拟人化程度必须在这里明确。</small>
              </label>
              <label className="span-2">
                <span>色彩策略</span>
                <div className="ai-field-control">
                  <textarea rows={3} maxLength={1000} value={settings.colorPalette} onChange={(event) => updateField("colorPalette", event.target.value)} />
                  {renderAiFieldAction("colorPalette")}
                </div>
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
                <div className="ai-field-control">
                  <textarea rows={4} maxLength={2000} value={settings.cameraLanguage} onChange={(event) => updateField("cameraLanguage", event.target.value)} />
                  {renderAiFieldAction("cameraLanguage")}
                </div>
              </label>
              <label className="span-2">
                <span>声音策略</span>
                <div className="ai-field-control">
                  <textarea rows={4} maxLength={2000} value={settings.soundStrategy} onChange={(event) => updateField("soundStrategy", event.target.value)} />
                  {renderAiFieldAction("soundStrategy")}
                </div>
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
            <div className="ai-field-control">
              <textarea
                className="prompt-prefix"
                rows={6}
                maxLength={4000}
                value={settings.imagePromptPrefix}
                onChange={(event) => updateField("imagePromptPrefix", event.target.value)}
                placeholder="例如：法式彩色冒险漫画，拟人犬角色，清晰墨线……"
              />
              {renderAiFieldAction("imagePromptPrefix")}
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
            <span className="eyebrow">GPT-IMAGE-2 / {settings.cover ? "重新生成" : "首次生成"}</span>
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
            <span className="eyebrow">GPT-5.4 / 智能调整</span>
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

export function SourcePage() {
  const { projectId = "", sourceEpisodeId } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const workspaceView: "source" | "analysis" | "script" = location.pathname.includes("/story/material")
    ? "analysis"
    : location.pathname.includes("/script/adaptation") || location.pathname.includes("/script/draft")
      ? "script"
      : "source";
  const sourceRouteBase = workspaceView === "script"
    ? `/projects/${projectId}/script/adaptation`
    : `/projects/${projectId}/story/${workspaceView === "source" ? "source" : "material"}`;
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
  const fileModeRef = useRef<"create" | "append">("create");
  const [analysis, setAnalysis] = useState<StoryMaterialAnalysis | null>(null);
  const [script, setScript] = useState<AdaptationScript | null>(null);
  const [projectSettings, setProjectSettings] = useState<ProjectSettings | null>(null);
  const [working, setWorking] = useState<"analysis" | "script" | "append" | "regenerate" | "confirm" | null>(null);
  const analysisControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    listProjectSources(projectId, controller.signal)
      .then((items) => {
        setSources(items);
        setError("");
        const requested = items.find((item) => item.id === sourceEpisodeId);
        const first = requested ?? items[0];
        if (first) {
          setSelectedChapterId(first.chapters[0]?.id);
          if (!requested) {
            navigate(`${sourceRouteBase}/${first.id}`, { replace: true });
          }
        } else if (sourceEpisodeId) {
          navigate(sourceRouteBase, { replace: true });
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
  }, [navigate, projectId, sourceEpisodeId, sourceRouteBase]);

  const activeSource = sources.find((item) => item.id === sourceEpisodeId) ?? sources[0];
  const selectedChapter = activeSource?.chapters.find((item) => item.id === selectedChapterId)
    ?? activeSource?.chapters[0];
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const filteredChapters = activeSource?.chapters.filter((chapter) =>
    !normalizedSearch
    || chapter.title.toLocaleLowerCase().includes(normalizedSearch)
    || chapter.content.toLocaleLowerCase().includes(normalizedSearch)) ?? [];
  const activeSourceId = activeSource?.id;

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
      setScript(loadedScript);
      setProjectSettings(loadedSettings);
    }).catch((loadError: unknown) => {
      if (!controller.signal.aborted) {
        setError(loadError instanceof Error ? loadError.message : "故事开发资料加载失败。");
      }
    });
    return () => controller.abort();
  }, [activeSourceId, projectId]);

  const selectSource = (source: ProjectSource) => {
    setSelectedChapterId(source.chapters[0]?.id);
    navigate(`${sourceRouteBase}/${source.id}`);
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
      navigate(`/projects/${projectId}/story/source/${source.id}`);
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "原文资料导入失败。");
    } finally {
      setImporting(false);
    }
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

  const requestScript = async () => {
    if (!activeSource) return;
    setWorking("script");
    setError("");
    try {
      const result = await generateAdaptationScript(projectId, activeSource.id, {
        instruction: "严格参考项目设定规划完整剧集，原文仅作为参考；每集建立清晰冲突、大小爆点和集尾追看动力。",
      });
      setScript(result);
      navigate(`/projects/${projectId}/script/adaptation/${activeSource.id}`);
    } catch (scriptError) {
      setError(scriptError instanceof Error ? scriptError.message : "改编大纲生成失败。");
    } finally {
      setWorking(null);
    }
  };

  const appendScriptEpisode = async () => {
    if (!activeSource) return;
    setWorking("append");
    setError("");
    try {
      setScript(await appendAdaptationEpisode(projectId, activeSource.id));
    } catch (appendError) {
      setError(appendError instanceof Error ? appendError.message : "添加剧集失败。");
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

  const confirmScript = async () => {
    if (!activeSource) return;
    setWorking("confirm");
    setError("");
    try {
      const confirmed = await confirmAdaptationScript(projectId, activeSource.id);
      setScript(confirmed);
      window.dispatchEvent(new Event("alex:production-episodes-updated"));
      if (confirmed.productionEpisodeIds[0]) {
        navigate(`/projects/${projectId}/script/episodes/${confirmed.productionEpisodeIds[0]}`);
      }
    } catch (confirmError) {
      setError(confirmError instanceof Error ? confirmError.message : "正式剧本生成失败。");
    } finally {
      setWorking(null);
    }
  };

  return (
    <div className="page full-height-page">
      <PageTitle
        title={workspaceView === "source" ? "原文资料" : workspaceView === "analysis" ? "素材图谱" : "改编方案"}
        action={workspaceView === "source" ? (
          <div className="button-group">
            <input
              ref={fileInputRef}
              className="visually-hidden"
              type="file"
              accept=".txt,.md,.markdown,text/plain,text/markdown"
              onChange={(event) => void chooseFile(event.target.files?.[0])}
            />
            <button className="secondary-button" type="button" onClick={() => {
              fileModeRef.current = activeSource ? "append" : "create";
              fileInputRef.current?.click();
            }}>
              <Upload size={14} />
              {activeSource ? "上传追加" : "上传文本"}
            </button>
            {activeSource && (
              <button className="secondary-button" type="button" onClick={() => openImport("create")}>
                <Plus size={14} />新建资料
              </button>
            )}
            <button className="primary-button" type="button" onClick={() => openImport(activeSource ? "append" : "create")}>
              <Plus size={14} />
              {activeSource ? "追加章节" : "粘贴原文"}
            </button>
          </div>
        ) : undefined}
      />
      {workspaceView === "script" && (
        <ScriptStageTabs projectId={projectId} active="adaptation" />
      )}
      {sources.length > 0 && workspaceView !== "script" && (
        <div className="story-stage-tabs" role="tablist" aria-label="原文开发视图">
          <button className={workspaceView === "source" ? "active" : ""} type="button" role="tab" aria-selected={workspaceView === "source"} onClick={() => navigate(`/projects/${projectId}/story/source/${activeSource?.id ?? ""}`)}>
            <span>01</span><strong>原文章节</strong>
          </button>
          <button className={workspaceView === "analysis" ? "active" : ""} type="button" role="tab" aria-selected={workspaceView === "analysis"} onClick={() => navigate(`/projects/${projectId}/story/material/${activeSource?.id ?? ""}`)}>
            <span>02</span><strong>素材图谱</strong>
          </button>
        </div>
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
              {filteredChapters.map((chapter) => (
                <button
                  type="button"
                  className={selectedChapter?.id === chapter.id ? "tree-section active" : "tree-section"}
                  onClick={() => setSelectedChapterId(chapter.id)}
                  key={chapter.id}
                >
                  <span>{String(chapter.number).padStart(2, "0")}</span>
                  <strong title={chapter.title}>{chapter.title}</strong>
                </button>
              ))}
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
          projectId={projectId}
          source={activeSource}
          analysis={analysis}
        />
      ) : (
        <AdaptationScriptWorkspace
          projectId={projectId}
          analysis={analysis}
          script={script}
          plannedEpisodeCount={projectSettings?.plannedEpisodeCount}
          working={working}
          onGenerate={() => void requestScript()}
          onAppend={() => void appendScriptEpisode()}
          onRegenerate={regenerateScriptEpisode}
          onConfirm={() => void confirmScript()}
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
    </div>
  );
}

function StoryMaterialWorkspace({
  projectId,
  source,
  analysis,
}: {
  projectId: string;
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
    <div className="development-workspace">
      <header>
        <div>
          <span className="eyebrow">素材图谱 / 来源 v{analysis.sourceVersion}</span>
        </div>
        <div className="button-group">
          <VersionPicker compact projectId={projectId} assetId={analysis.assetId} label="素材分析版本" />
          <span className="status-chip">
            已分析 {analysis.analyzedChapterIds?.length ?? 0}/{source?.chapterCount ?? 0} 章
          </span>
          <span className={analysis.isStale ? "status-chip warning" : "status-chip"}>
            {analysis.isStale ? "原文已有新版本" : "与当前原文同步"}
          </span>
        </div>
      </header>
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

function AdaptationScriptWorkspace({
  projectId,
  analysis,
  script,
  plannedEpisodeCount,
  working,
  onGenerate,
  onAppend,
  onRegenerate,
  onConfirm,
}: {
  projectId: string;
  analysis: StoryMaterialAnalysis | null;
  script: AdaptationScript | null;
  plannedEpisodeCount?: number;
  working: "analysis" | "script" | "append" | "regenerate" | "confirm" | null;
  onGenerate: () => void;
  onAppend: () => void;
  onRegenerate: (episodeNumber: number, instruction: string) => Promise<boolean>;
  onConfirm: () => void;
}) {
  const [activeEpisodeNumber, setActiveEpisodeNumber] = useState(1);
  const [regenerateEpisodeNumber, setRegenerateEpisodeNumber] = useState<number | null>(null);
  const [regenerateInstruction, setRegenerateInstruction] = useState("");

  if (!analysis) {
    return (
      <div className="source-empty-state development-empty-state">
        <span className="eyebrow">顺序门槛</span>
        <h2>先完成素材分析</h2>
        <p>剧本改写必须锁定一版素材图谱，不能从尚未分析的原文直接生成正式资产。</p>
      </div>
    );
  }
  if (!script) {
    return (
      <div className="source-empty-state development-empty-state">
        <span className="eyebrow">改编草案 / 来源 v{analysis.sourceVersion}</span>
        <h2>按项目设定规划{plannedEpisodeCount ? ` ${plannedEpisodeCount} ` : ""}集</h2>
        <p>模型会从素材图谱提取原故事主线，按网剧节奏删减支线、合并事件并补充必要连接，输出新的主线和分集大纲。此阶段不写正式对白和镜头。</p>
        <button className="primary-button" type="button" disabled={working !== null || analysis.isStale} onClick={onGenerate}>
          <WandSparkles size={14} />{working === "script" ? "生成中" : analysis.isStale ? "请先分析新增章节" : `生成${plannedEpisodeCount ? `${plannedEpisodeCount}集` : "完整"}草案`}
        </button>
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

  return (
    <div className="development-workspace script-draft-workspace">
      <header>
        <div>
          <span className="eyebrow">改编大纲 / v{script.version} / 原文 v{script.sourceVersion}</span>
          <h2>{script.title}</h2>
        </div>
        <div className="button-group">
          <VersionPicker compact projectId={projectId} assetId={script.assetId} label="改编方案版本" />
          {script.status === "draft" && (
            <>
              <button className="secondary-button" type="button" disabled={working !== null} onClick={onGenerate}>
                <WandSparkles size={13} />{working === "script" ? "重新生成中" : `按设定重新生成${plannedEpisodeCount ? ` ${plannedEpisodeCount} 集` : "全部"}`}
              </button>
              <button className="secondary-button" type="button" disabled={working !== null || script.episodes.length >= 6} onClick={onAppend}>
                <Plus size={13} />{working === "append" ? "添加中" : "添加剧集"}
              </button>
              <button className="primary-button" type="button" disabled={working !== null} onClick={onConfirm}>
                <Check size={13} />{working === "confirm" ? "正式剧本生成中" : "生成正式剧本"}
              </button>
            </>
          )}
        </div>
      </header>
      {script.hasNewerSourceVersion && <div className="development-warning">原文已有新版本，但此草案仍锁定原文 v{script.sourceVersion}，内容未被自动修改。</div>}
      <section className="adaptation-mainline">
        <span>主线改编策略</span>
        <p>{script.approach}</p>
      </section>
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
                <small>{episode.scenes.length} 个节点 · {episode.targetSeconds}s</small>
              </button>
            ))}
          </div>
        </aside>
        <section className="script-draft-main">
          {activeEpisode && (
            <div className="script-proposal-list single-episode">
              <section>
                <header>
                  <span>E{String(activeEpisode.proposalNumber).padStart(2, "0")}</span>
                  <div><h3>{activeEpisode.title}</h3></div>
                  <div className="episode-draft-actions">
                    <small>{activeEpisode.targetSeconds}s</small>
                    <button
                      className="secondary-button icon-button"
                      type="button"
                      disabled={working !== null}
                      aria-label="重新生成本集"
                      title="重新生成本集"
                      onClick={() => {
                        setRegenerateInstruction("");
                        setRegenerateEpisodeNumber(activeEpisode.proposalNumber);
                      }}
                    >
                      <RefreshCw size={14} />
                    </button>
                  </div>
                </header>
                <div>
                  {activeEpisode.scenes.map((scene, sceneIndex) => (
                    <Fragment key={scene.sceneNumber}>
                      <article>
                        <b>{String(scene.sceneNumber).padStart(2, "0")}</b>
                        <div><strong>{scene.heading}</strong><p>{scene.summary}</p><small>主线作用：{scene.storyFunction}</small></div>
                        <div><span>{scene.characters.join(" · ")}</span><small>{scene.props.length ? `道具线索：${scene.props.join(" · ")}` : "无关键道具"}</small></div>
                      </article>
                      {activeEpisodeHooks
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
}: {
  projectId: string;
  active: "adaptation" | "production";
}) {
  const navigate = useNavigate();
  return (
    <div className="story-stage-tabs" role="tablist" aria-label="剧本开发视图">
      <button
        className={active === "adaptation" ? "active" : ""}
        type="button"
        role="tab"
        aria-selected={active === "adaptation"}
        onClick={() => navigate(`/projects/${projectId}/script/adaptation`)}
      >
        <span>01</span><strong>改编方案</strong>
      </button>
      <button
        className={active === "production" ? "active" : ""}
        type="button"
        role="tab"
        aria-selected={active === "production"}
        onClick={() => navigate(`/projects/${projectId}/script`)}
      >
        <span>02</span><strong>正式剧本</strong>
      </button>
    </div>
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
    return <Navigate to={`/projects/${projectId}/script/episodes/${episodes[0].id}`} replace />;
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
    setLoading(true);
    setError("");
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
    setLoading(true);
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
      <PageTitle
        eyebrow="项目共享资产"
        title="音频素材"
        description="角色参考音、对白、环境声与音乐统一作为可追踪素材管理"
        action={
          <button className="primary-button" onClick={() => setUploadOpen(true)}>
            <Upload size={14} />上传音频
          </button>
        }
      />
      {error && <div className="settings-error asset-error">{error}</div>}
      <AssetTabs assetType="audio" counts={{ audio: materials.length }} />
      <div className="asset-workspace audio-asset-workspace">
        <section className="asset-list-panel">
          <div className="table-tools">
            <label><Search size={14} /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="搜索音频" /></label>
          </div>
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
    ? <AudioAssetsPage projectId={projectId} />
    : <VisualAssetsPage projectId={projectId} assetType={assetType} />;
}

function VisualAssetsPage({ projectId, assetType }: { projectId: string; assetType: string }) {
  const kindConfig = assetKinds[assetType] ?? assetKinds.characters;
  const [assets, setAssets] = useState<VisualAsset[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState<"import" | "save" | "reference" | null>(null);
  const [error, setError] = useState("");
  const [editorOpen, setEditorOpen] = useState(false);
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

  const generateReference = async () => {
    if (!selected) return;
    setWorking("reference");
    setError("");
    try {
      const referenceImage = await generateVisualReference(projectId, selected.resourceId);
      setAssets((current) => current.map((item) => item.resourceId === selected.resourceId
        ? { ...item, referenceImage }
        : item));
      setPromptCopied(false);
    } catch (generateError) {
      setError(generateError instanceof Error ? generateError.message : "参考图生成失败。");
    } finally {
      setWorking(null);
    }
  };

  return (
    <div className="page full-height-page asset-bible-page">
      <PageTitle
        eyebrow="项目共享资产"
        title="资产圣经"
        description="人物、场景、道具的视觉定义、设定图与参考统一在资产内版本化管理"
        action={
          <div className="button-group">
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
        }
      />
      {error && <div className="settings-error asset-error">{error}</div>}
      <AssetTabs assetType={assetType} counts={counts} />
      <div className="asset-workspace">
        <section className="asset-list-panel">
          <div className="table-tools">
            <label>
              <Search size={14} />
              <input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder={`搜索${kindConfig.label}`}
              />
            </label>
          </div>
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
                <h2>{selected.name}</h2>
                <p>{selected.summary || "尚未填写叙事定义"}</p>
              </div>
              <div className="asset-detail-actions">
                <button className="secondary-button" onClick={() => openEdit(selected)}>
                  <Edit3 size={14} />编辑并保存新版本
                </button>
                <VersionPicker projectId={projectId} assetId={selected.assetId} label={`${kindConfig.singular}版本`} />
              </div>
            </header>
            <dl className="detail-grid">
              <div>
                <dt>叙事定义</dt>
                <dd>{selected.summary || "未填写"}</dd>
              </div>
              <div>
                <dt>视觉定义</dt>
                <dd>{selected.visualDescription || "未填写"}</dd>
              </div>
              <div>
                <dt>必须保留</dt>
                <dd>{selected.mustKeep.join("、") || "未填写"}</dd>
              </div>
              <div>
                <dt>禁止项</dt>
                <dd>{selected.avoid.join("、") || "未填写"}</dd>
              </div>
              <div>
                <dt>故事引用</dt>
                <dd>{selected.storyReferences.join("、") || "尚未关联"}</dd>
              </div>
              <div>
                <dt>来源</dt>
                <dd>{selected.sourceAssetId ? "素材图谱" : "手动创建"}</dd>
              </div>
            </dl>
            <div className="asset-reference-workbench">
              <header className="asset-reference-header">
                <div>
                  <span className="eyebrow">PRODUCTION DESIGN</span>
                  <strong>{assetReferenceSpecs[selected.kind].title}</strong>
                  <p>{assetReferenceSpecs[selected.kind].layout}</p>
                </div>
                <div className="asset-reference-actions">
                  <span>1024 × 1024</span>
                  <span>纯白背景</span>
                  <button
                    className="primary-button"
                    onClick={generateReference}
                    disabled={working !== null}
                  >
                    <RefreshCw size={13} />
                    {working === "reference"
                      ? "正在生成"
                      : selected.referenceImage ? "重试生成" : "生成设定图"}
                  </button>
                </div>
              </header>
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
                    <div><span>本版生成提示词</span><small>{selected.referenceImage ? `图片 v${selected.referenceImage.version}` : "等待生成"}</small></div>
                    {selected.referenceImage?.prompt && (
                      <button
                        type="button"
                        className="icon-button"
                        title="复制提示词"
                        aria-label="复制提示词"
                        onClick={() => {
                          void navigator.clipboard.writeText(selected.referenceImage?.prompt ?? "");
                          setPromptCopied(true);
                        }}
                      >
                        {promptCopied ? <Check size={14} /> : <Copy size={14} />}
                      </button>
                    )}
                  </header>
                  <pre>{selected.referenceImage?.prompt || "提示词会在首次生成后随图片版本保存，并在这里完整显示。"}</pre>
                  <footer>
                    {selected.referenceImage ? (
                      <VersionPicker projectId={projectId} assetId={selected.referenceImage.assetId} label="设定图版本" />
                    ) : <span>暂无版本</span>}
                  </footer>
                </aside>
              </div>
            </div>
            {selected.kind === "character" && (
              <CharacterVoicePanel projectId={projectId} character={selected} />
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
        <ScriptStageTabs projectId={projectId} active="production" />
        <div className="production-episode-switcher">
          <span>正式剧本</span>
          <Select
            aria-label="切换正式剧本生产集"
            value={productionEpisodeId || undefined}
            disabled={productionEpisodes.length === 0}
            onChange={(episodeId) => navigate(`/projects/${projectId}/script/episodes/${episodeId}`)}
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

  return (
    <div className="page full-height-page production-script-page">
      <header className="production-script-header">
        <div>
          <span className="eyebrow">{scriptPackage.isLegacyOutline ? "历史大纲快照" : "正式剧本"} / E{String(scriptPackage.episodeNumber).padStart(2, "0")} / v{scriptPackage.version}</span>
          <h1>{scriptPackage.title}</h1>
          <p>{scriptPackage.targetSeconds ?? episode.targetSeconds} 秒 · {episode.scenes.length} 场</p>
        </div>
        <div className="production-script-header-actions">
          <VersionPicker compact projectId={projectId} assetId={scriptPackage.assetId} label="正式剧本版本" />
          <button type="button" className="primary-button" disabled={regenerating} onClick={handleRegenerate}>
            <RefreshCw size={13} />{regenerating ? "重新生成中" : "重新生成正式剧本"}
          </button>
        </div>
      </header>
      <ScriptStageTabs projectId={projectId} active="production" />
      <div className="production-episode-switcher">
        <span>正式剧本</span>
        <Select
          aria-label="切换正式剧本生产集"
          value={productionEpisodeId}
          onChange={(episodeId) => navigate(`/projects/${projectId}/script/episodes/${episodeId}`)}
          options={productionEpisodes.map((episode) => ({
            value: episode.id,
            label: `E${String(episode.episodeNumber).padStart(2, "0")} · ${episode.title}`,
          }))}
        />
      </div>
      {regenerateError && <div className="settings-error">{regenerateError}</div>}
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
            <header>
              <span>S{String(activeScene.sceneNumber).padStart(2, "0")}</span>
              <div><h2>{activeScene.heading}</h2><p>{activeScene.storyFunction}</p></div>
            </header>
            <section className="production-plan-overview" aria-label="场次拍摄指标">
              <div><span>计划时长</span><strong>{activeSceneShots.length > 0 ? `${sceneDurationSeconds.toFixed(1)}s` : "—"}</strong></div>
              <div><span>镜头数量</span><strong>{activeSceneShots.length > 0 ? `${activeSceneShots.length} 镜` : "—"}</strong></div>
              <div><span>节奏设计</span><strong>{rhythmLabel}</strong><small>{averageShotSeconds > 0 ? `平均 ${averageShotSeconds.toFixed(1)}s / 镜` : "重新生成方案后补齐"}</small></div>
              <div><span>视觉对比</span><strong>{shotSizeChange}</strong><small>{activeScene?.visualContrast?.trim() || (activeSceneShots.length > 0 ? `${shotSizeCount} 种景别 · ${cameraMovementCount} 种运镜` : "重新生成方案后补齐")}</small></div>
            </section>
            {activeSceneShots.length > 0 && (
              <section className="production-rhythm-track" aria-label="镜头节奏时间轴">
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
              </section>
            )}
            {activeSceneHooks.length > 0 && (
              <div className="production-scene-hooks">
                {activeSceneHooks.map((item, index) => (
                  <div className={item.tone} key={`${item.tone}-${index}-${item.hook}`}>
                    <span><i />{item.tone === "small" ? "小爆点" : "大爆点"}</span>
                    <p>{item.hook}</p>
                  </div>
                ))}
              </div>
            )}
            <section className="production-script-block primary">
              <span>{scriptPackage.isLegacyOutline ? "剧情节点" : "动作"}</span>
              <p>{scriptPackage.isLegacyOutline ? activeScene.summary : activeScene.action}</p>
            </section>
            {scriptPackage.isLegacyOutline ? (
              <section className="production-script-block production-dialogue-intent">
                <span>对白意图</span>
                <p>{activeScene.dialogueIntent || "旧版大纲未记录对白意图。"}</p>
              </section>
            ) : (
              <section className="production-screenplay-dialogues">
                <span>对白</span>
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
              </section>
            )}
            <section className="production-shot-plan">
              <header>
                <div><span>镜头计划</span><strong>{activeSceneShots.length > 0 ? `${activeSceneShots.length} 个执行镜头` : "尚未生成分镜"}</strong></div>
                <button type="button" onClick={() => navigate(activeSceneShots.length > 0 ? `/projects/${projectId}/storyboard/episodes/${productionEpisodeId}` : `/projects/${projectId}/script`)}>
                  {activeSceneShots.length > 0 ? "进入分镜" : "返回方案"}
                </button>
              </header>
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
            <footer className="production-scene-meta">
              <div><span>人物</span><p>{activeScene.characters.join(" · ") || "无明确人物"}</p></div>
              <div><span>道具</span><p>{activeScene.props.join(" · ") || "无关键道具"}</p></div>
            </footer>
          </article>
        )}
      </div>
    </div>
  );
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

  const activeEpisode = productionEpisodes.find((item) => item.id === productionEpisodeId);
  const loading = productionEpisodeId !== "" && loadedEpisodeId !== productionEpisodeId;
  const currentStoryboard = storyboard?.productionEpisodeId === productionEpisodeId ? storyboard : null;
  const episodeCode = activeEpisode
    ? `E${String(activeEpisode.episodeNumber).padStart(2, "0")}`
    : "生产集";
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
  return (
    <div className="page">
      <PageTitle
        eyebrow={`分镜 / ${episodeCode}${currentStoryboard ? ` / v${currentStoryboard.revision}` : ""}`}
        title={currentStoryboard?.title ?? activeEpisode?.title ?? "分镜工作区"}
        description={currentStoryboard
          ? `${currentStoryboard.shots.length} 个镜头 · ${currentStoryboard.totalDurationSeconds} / ${currentStoryboard.targetSeconds} 秒 · ${currentStoryboard.model}`
          : "从当前生产集的正式剧本和资产圣经生成结构化分镜草稿"}
        action={
          <div className="button-group">
            <label className="select-control">
              <span>生产集</span>
              <select
                value={productionEpisodeId}
                onChange={(event) => navigate(`../${event.target.value}`, { relative: "path" })}
              >
                {productionEpisodes.map((item) => (
                  <option key={item.id} value={item.id}>
                    E{String(item.episodeNumber).padStart(2, "0")} · {item.title}
                  </option>
                ))}
              </select>
              <ChevronDown size={13} />
            </label>
            <button className="primary-button" onClick={generate} disabled={generating || !productionEpisodeId}>
              <WandSparkles size={14} />
              {generating ? "正在设计分镜" : currentStoryboard ? "重新生成草稿" : "生成分镜草稿"}
            </button>
          </div>
        }
      />
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
          <div className="table-row table-head">
            <span>镜号</span>
            <span>爆点</span>
            <span>帧策略</span>
            <span>首帧</span>
            <span>主体</span>
            <span>景别 / 机位</span>
            <span>动作</span>
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
                  ? <img src={shot.production.outputUrl} alt={`S${shot.sceneNumber}-${shot.shotNumber} 首帧`} />
                  : <small>未生成</small>}
              </span>
              <span>{shot.characters.join("、") || shot.props.join("、") || "场景"}</span>
              <span>{shot.shotSize} · {shot.cameraAngle}</span>
              <span>{shot.action}</span>
              <span>{shot.durationSeconds}s</span>
              <span className={`state-label ${shot.production?.status ?? "draft"}`}>
                {productionStatusLabel(shot.production?.status ?? "draft")}
              </span>
            </button>
          ))}
        </div>
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
  const [productionPreview, setProductionPreview] = useState<ImageGenerationPreview | null>(null);
  const [video, setVideo] = useState<ShotVideoProduction | null>(null);
  const [videoPreview, setVideoPreview] = useState<ShotVideoPreview | null>(null);
  const [videoConfirmation, setVideoConfirmation] = useState(false);
  const [startingVideo, setStartingVideo] = useState(false);
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
  const previewProduction = async () => {
    if (!shot) return;
    setStartingProduction(true);
    setError("");
    try {
      setProductionPreview(await previewShotProduction(projectId, productionEpisodeId, shot.resourceId));
    } catch (previewError) {
      setError(previewError instanceof Error ? previewError.message : "首帧生成规格加载失败。");
    } finally {
      setStartingProduction(false);
    }
  };
  const startProduction = async () => {
    if (!shot || !productionPreview) return;
    if (!shot) return;
    setStartingProduction(true);
    setError("");
    try {
      const production = await startShotProduction(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        productionPreview.prompt,
      );
      setStoryboard((current) => current ? {
        ...current,
        shots: current.shots.map((item) => item.resourceId === shot.resourceId
          ? { ...item, production }
          : item),
      } : current);
      setProductionConfirmation(false);
      setProductionPreview(null);
    } catch (startError) {
      setError(startError instanceof Error ? startError.message : "镜头开始制作失败。");
    } finally {
      setStartingProduction(false);
    }
  };
  const previewVideo = async () => {
    if (!shot) return;
    setStartingVideo(true);
    setError("");
    try {
      setVideoPreview(await previewShotVideo(projectId, productionEpisodeId, shot.resourceId));
    } catch (previewError) {
      setError(previewError instanceof Error ? previewError.message : "视频生成规格加载失败。");
    } finally {
      setStartingVideo(false);
    }
  };
  const createVideo = async () => {
    if (!shot || !videoPreview) return;
    setStartingVideo(true);
    setError("");
    try {
      const started = await startShotVideo(
        projectId,
        productionEpisodeId,
        shot.resourceId,
        videoPreview.prompt,
        videoPreview.previewHash,
      );
      setVideo(started);
      setVideoConfirmation(false);
      setVideoPreview(null);
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
  return (
    <div className="page">
      <PageTitle
        eyebrow={`分镜 / ${storyboard?.title ?? "生产集"} / ${shotCode}`}
        title={shot.action}
        description={`${shot.shotSize} · ${shot.cameraAngle} · ${shot.cameraMovement} · ${shot.durationSeconds} 秒`}
        action={<div className="button-group"><VersionPicker projectId={projectId} assetId={shot.assetId} label="镜头版本" /><button className="secondary-button" onClick={() => navigate("../..", { relative: "path" })}>返回镜头表</button></div>}
      />
      {error && <div className="source-empty-state development-empty-state"><strong>{error}</strong></div>}
      <section className="shot-workbench">
        <header>
          <div>
            <span className="eyebrow">镜头制作</span>
            <h2>{shotCode}</h2>
            <p>{shot.visualDescription || shot.composition}</p>
          </div>
          <div className="shot-production-actions">
            <span className="production-mode">
              {productionModeLabel(shot.productionMode)}
            </span>
            <button
              className="primary-button"
              onClick={() => { setProductionPreview(null); setProductionConfirmation(true); }}
              disabled={startingProduction
                || associationsDirty
                || associationDraft.length === 0
                || ["queued", "running"].includes(shot.production?.status ?? "")}
            >
              <Play size={14} />
              {startingProduction
                ? shot.productionMode === "first-last-continuous" ? "正在生成首尾帧" : "正在生成首帧"
                : shot.production?.status === "completed"
                  ? shot.productionMode === "first-last-continuous" ? "重新生成首尾帧" : "重新生成首帧"
                  : ["queued", "running"].includes(shot.production?.status ?? "")
                    ? "正在制作"
                    : shot.productionMode === "first-last-continuous" ? "开始制作首尾帧" : "开始制作首帧"}
            </button>
            {shot.production && (
              <NavLink className="secondary-button" to={`/projects/${projectId}/production/runs/${shot.production.runId}`}>
                查看生产运行
              </NavLink>
            )}
          </div>
        </header>
        <section className="shot-directing-analysis" aria-label="镜头执行分析">
          <div className="shot-strategy-reason">
            <span className="eyebrow">帧策略判断</span>
            <strong>{shot.productionMode === "first-last-continuous" ? "需要首帧与尾帧" : "只需要首帧"}</strong>
            <p>{shot.frameStrategyReason}</p>
          </div>
          <div className="shot-frame-brief">
            <span className="eyebrow">首帧</span>
            <p>{shot.firstFrameDescription || shot.visualDescription}</p>
          </div>
          {shot.productionMode === "first-last-continuous" && (
            <div className="shot-frame-brief last">
              <span className="eyebrow">尾帧</span>
              <p>{shot.lastFrameDescription}</p>
            </div>
          )}
          <div className="shot-cut-description">
            <span className="eyebrow">CUT 执行描述</span>
            <p>{shot.cutDescription || shot.action}</p>
          </div>
          {(shot.dialogue || shot.sound) && (
            <dl className="shot-audio-cues">
              {shot.dialogue && <div><dt>对白</dt><dd>{shot.dialogue}</dd></div>}
              {shot.sound && <div><dt>声音</dt><dd>{shot.sound}</dd></div>}
            </dl>
          )}
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
        {(shot.production?.outputUrl || shot.production?.lastFrameUrl) && (
          <section className="shot-output-gallery" aria-label="已生成镜头帧">
            {shot.production.outputUrl && (
              <div className="shot-output-item">
                <button className="shot-output-frame" type="button" onClick={() => setFramePreview({ url: shot.production!.outputUrl!, label: `${shotCode} 首帧` })} title="预览首帧">
                  <img src={shot.production.outputUrl} alt={`${shotCode} 首帧`} />
                  <span>当前首帧</span>
                </button>
                <div className="shot-output-meta">
                  {shot.production.outputAssetId && <VersionPicker projectId={projectId} assetId={shot.production.outputAssetId} label="首帧版本" />}
                  {shot.production.outputPrompt && (
                    <details><summary>首帧提示词</summary><textarea readOnly rows={6} value={shot.production.outputPrompt} /></details>
                  )}
                </div>
              </div>
            )}
            {shot.production.lastFrameUrl && (
              <div className="shot-output-item">
                <button className="shot-output-frame" type="button" onClick={() => setFramePreview({ url: shot.production!.lastFrameUrl!, label: `${shotCode} 尾帧` })} title="预览尾帧">
                  <img src={shot.production.lastFrameUrl} alt={`${shotCode} 尾帧`} />
                  <span>当前尾帧</span>
                </button>
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
                className="primary-button"
                type="button"
                onClick={() => { setVideoPreview(null); setVideoConfirmation(true); }}
                disabled={!shot.production?.outputAssetId || startingVideo || ["queued", "running"].includes(video?.status ?? "")}
              >
                <Play size={14} />
                {startingVideo ? "处理中" : ["queued", "running"].includes(video?.status ?? "") ? "正在生成" : video?.status === "completed" ? "重新生成视频" : "生成视频"}
              </button>
              {video && <NavLink className="secondary-button" to={`/projects/${projectId}/production/runs/${video.runId}`}>查看运行</NavLink>}
            </div>
          </header>
          {video?.url && (
            <div className="shot-video-output">
              <video controls preload="metadata" src={video.url}>
                <track kind="captions" />
              </video>
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
      {videoConfirmation && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setVideoConfirmation(false)}>
          <form
            className="dialog ai-assist-dialog generation-confirmation-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              void (videoPreview ? createVideo() : previewVideo());
            }}
          >
            <span className="eyebrow">MINIMAX H3 / {shotCode}</span>
            <h2>{videoPreview ? "核对视频生成规格" : video?.status === "completed" ? "重新生成镜头视频" : "生成镜头视频"}</h2>
            <p>{videoPreview ? "确认后任务进入后台队列，关闭页面不会中断生成。" : "先锁定当前项目分辨率、关键帧版本、提示词和 H3 参数。"}</p>
            {videoPreview && (
              <div className="generation-preview">
                <label><span>完整提示词</span><textarea readOnly rows={10} value={videoPreview.prompt} /></label>
                <dl className="generation-parameters">
                  <div><dt>输出</dt><dd>{videoPreview.width} × {videoPreview.height}</dd></div>
                  <div><dt>时长</dt><dd>{videoPreview.durationSeconds} 秒</dd></div>
                  <div><dt>采样</dt><dd>{videoPreview.frameCount} 帧 · {videoPreview.fps} FPS</dd></div>
                  <div><dt>关键帧</dt><dd>{videoPreview.lastFrameAssetId ? "首帧 + 尾帧" : "仅首帧"}</dd></div>
                  <div><dt>Workflow</dt><dd>{videoPreview.workflowProfile}</dd></div>
                  <div><dt>参数</dt><dd>euler · simple · 4 steps</dd></div>
                </dl>
              </div>
            )}
            <div>
              <button className="secondary-button" type="button" onClick={() => { setVideoConfirmation(false); setVideoPreview(null); }}>取消</button>
              <button className="primary-button" type="submit" disabled={startingVideo}>
                <Play size={13} />
                {startingVideo ? "处理中" : videoPreview ? "确认并加入队列" : "预览生成规格"}
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
              void (productionPreview ? startProduction() : previewProduction());
            }}
          >
            <span className="eyebrow">GPT-IMAGE-2 / {shotCode}</span>
            <h2>{productionPreview ? "核对镜头帧生成规格" : `${shot.production?.status === "completed" ? "重新制作" : "制作"}${shot.productionMode === "first-last-continuous" ? "首帧与尾帧" : "首帧"}`}</h2>
            <p>{productionPreview ? `确认后将按当前输入版本生成${shot.productionMode === "first-last-continuous" ? "首帧，再以首帧为连续性锚点生成尾帧" : "首帧"}。` : "先预览完整生成规格；每张图片、输入资产版本和对应提示词都会随结果保存。"}</p>
            {productionPreview && <GenerationPreviewDetails preview={productionPreview} />}
            <div>
              <button className="secondary-button" type="button" onClick={() => { setProductionConfirmation(false); setProductionPreview(null); }}>取消</button>
              <button className="primary-button" type="submit" disabled={startingProduction}>
                <Sparkles size={13} />
                {startingProduction ? "处理中" : productionPreview ? "确认并开始制作" : "预览生成规格"}
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
  const [productionEpisodes, setProductionEpisodes] = useState<ProductionEpisodeRecord[]>([]);
  const [runs, setRuns] = useState<ProductionRun[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      listProductionEpisodes(projectId, controller.signal),
      listProductionRuns(projectId, productionEpisodeId || undefined, controller.signal),
    ])
      .then(([episodes, loadedRuns]) => {
        setProductionEpisodes(episodes);
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

  const itemCount = runs.reduce((total, run) => total + run.items.length, 0);
  const runningCount = runs.filter((run) => ["queued", "running"].includes(run.status)).length;
  const failedCount = runs.filter((run) => run.status === "failed").length;
  const completedCount = runs.filter((run) => run.status === "completed").length;
  return (
    <div className="page">
      <PageTitle
        eyebrow="媒体生产 / 真实运行"
        title="生产中心"
        description="查看从镜头详情创建的真实生产运行、阶段和输出"
        action={
          <label className="select-control">
            <span>生产集</span>
            <select
              value={productionEpisodeId}
              onChange={(event) => navigate(event.target.value
                ? `/projects/${projectId}/production/episodes/${event.target.value}`
                : `/projects/${projectId}/production`)}
            >
              <option value="">全部生产集</option>
              {productionEpisodes.map((episode) => (
                <option key={episode.id} value={episode.id}>E{String(episode.episodeNumber).padStart(2, "0")} · {episode.title}</option>
              ))}
            </select>
            <ChevronDown size={13} />
          </label>
        }
      />
      {error && <div className="source-empty-state development-empty-state"><strong>{error}</strong></div>}
      <div className="production-summary">
        <div>
          <span className="eyebrow">运行记录</span>
          <strong>{runs.length}</strong>
          <small>Run</small>
        </div>
        <div>
          <span className="eyebrow">任务项</span>
          <strong>{itemCount}</strong>
          <small>真实阶段</small>
        </div>
        <div>
          <span className="eyebrow">进行 / 失败</span>
          <strong className={failedCount ? "danger" : ""}>{runningCount} / {failedCount}</strong>
          <small>Run</small>
        </div>
        <div>
          <span className="eyebrow">已完成</span>
          <strong className="online">{completedCount}</strong>
          <small>Run</small>
        </div>
      </div>
      <section className="production-table panel">
        <header className="panel-header">
          <h2>生产运行</h2>
          <span>按创建时间倒序</span>
        </header>
        {!loaded && <div className="source-empty-state"><strong>正在读取生产运行</strong></div>}
        {loaded && !error && runs.length === 0 && (
          <div className="source-empty-state">
            <strong>尚无生产运行</strong>
            <span>从分镜镜头详情开始制作后，运行会出现在这里。</span>
          </div>
        )}
        {runs.map((run) => (
          <button className="production-row" onClick={() => navigate(`/projects/${projectId}/production/runs/${run.id}`)} key={run.id}>
            <span className="episode-code large">E{String(run.episodeNumber).padStart(2, "0")}</span>
            <span className="production-title">
              <strong>{run.episodeTitle}</strong>
              <small>{productionModeLabel(run.mode)} · {localDateTime(run.createdAtUtc)}</small>
            </span>
            <div className="stage-pipeline">
              {run.items.map((item) => (
                <span className={item.status === "completed" ? "done" : item.status} key={item.id}>
                  {productionStageLabel(item.stage)} <b>{productionStatusLabel(item.status)}</b>
                </span>
              ))}
            </div>
            <span className={`state-label ${run.status}`}>{productionStatusLabel(run.status)}</span>
            <MoreHorizontal size={16} />
          </button>
        ))}
      </section>
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
  const { productionEpisodeId } = useParams();
  const episode =
    episodes.find((item) => item.id === productionEpisodeId) ?? episodes[0];
  const [playing, setPlaying] = useState(false);
  return (
    <div className="page">
      <PageTitle
        eyebrow={`审阅交付 / ${episode.code}`}
        title="成片审阅"
        description={`Final ${episode.code} v2 · 00:01:38 · 3 条开放批注`}
        action={
          <div className="button-group">
            <EpisodeSelect value={productionEpisodeId} />
            <button className="primary-button">导出本集</button>
          </div>
        }
      />
      <div className="review-layout">
        <section className="player-panel">
          <div className="video-frame">
            <div className="video-scene">
              <span>ALEX DIRECTOR · {episode.code} REVIEW</span>
              <strong>天桥食堂</strong>
              <p>凌晨 05:12 · 二楼</p>
            </div>
            <button
              className="play-button"
              onClick={() => setPlaying(!playing)}
            >
              {playing ? "Ⅱ" : <Play size={22} fill="currentColor" />}
            </button>
          </div>
          <div className="timeline">
            <span>00:00</span>
            <div>
              <i style={{ width: playing ? "62%" : "28%" }} />
              <b style={{ left: playing ? "62%" : "28%" }} />
              <em style={{ left: "51%" }}>批注</em>
            </div>
            <span>01:38</span>
          </div>
          <div className="shot-strip">
            {reviewDemoShots.map((shot, index) => (
              <button className={index === 2 ? "active" : ""} key={shot.id}>
                <span className={`mini-frame frame-${index % 4}`} />
                <strong>{shot.id}</strong>
              </button>
            ))}
          </div>
        </section>
        <aside className="comments-panel">
          <header>
            <strong>批注</strong>
            <button className="primary-button">
              <Plus size={13} />
              添加
            </button>
          </header>
          {[
            ["00:24", "节奏偏慢，提前切到文件袋特写", "open"],
            ["00:51", "周岚最后两个字不清楚", "planned"],
            ["01:12", "这里的环境声可以更冷一些", "open"],
          ].map((item) => (
            <button className="comment-row" key={item[0]}>
              <span>{item[0]}</span>
              <div>
                <strong>{item[1]}</strong>
                <small>{item[2]}</small>
              </div>
            </button>
          ))}
        </aside>
      </div>
    </div>
  );
}

const reviewDemoShots = Array.from({ length: 7 }, (_, index) => ({
  id: `S0${Math.floor(index / 4) + 1}-${String((index % 4) + 1).padStart(2, "0")}`,
}));
