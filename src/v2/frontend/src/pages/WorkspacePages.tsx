import { useEffect, useState, type FormEvent } from "react";
import {
  NavLink,
  useLocation,
  useNavigate,
  useParams,
} from "react-router-dom";
import { Image } from "antd";
import {
  Check,
  CheckSquare2,
  ChevronDown,
  CircleAlert,
  Edit3,
  Filter,
  ImagePlus,
  Link2,
  MoreHorizontal,
  Play,
  Plus,
  Save,
  Search,
  Sparkles,
  Upload,
  WandSparkles,
} from "lucide-react";
import {
  assistProjectSettingsField,
  generateProjectCover,
  getProjectSettings,
  saveProjectSettings,
  type ProjectSettings,
  type ProjectSettingsAssistField,
} from "../api/projectSettings";

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

  async function generateCover(instruction?: string) {
    const currentSettings = settings;
    if (!currentSettings || generatingCover || status === "saving") return;
    setGeneratingCover(true);
    setError(null);
    try {
      let current = currentSettings;
      if (status === "dirty" || currentSettings.version === 0) {
        setStatus("saving");
        current = await saveProjectSettings(projectId, currentSettings);
        setSettings(current);
      }
      const cover = await generateProjectCover(projectId, instruction);
      setSettings({ ...current, cover });
      setStatus("saved");
    } catch (coverError) {
      setError(coverError instanceof Error ? coverError.message : "概念封面生成失败。");
      setStatus("error");
    } finally {
      setGeneratingCover(false);
    }
  }

  function requestCoverGeneration() {
    if (!settings?.cover) {
      void generateCover();
      return;
    }
    setCoverInstruction("");
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
        className="ai-field-action"
        type="button"
        onClick={() => requestAssist(field)}
        disabled={Boolean(assistingField)}
        title={hasContent ? "使用 GPT-5.4 优化当前内容" : "使用 GPT-5.4 生成内容"}
      >
        <WandSparkles size={12} />
        {assistingField === field ? "处理中" : hasContent ? "AI 优化" : "AI 生成"}
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
      <div className="settings-layout">
        <aside className="subnav">
          <div className="settings-version-card">
            <span>当前版本</span>
            <strong>{settings.version === 0 ? "草稿" : `v${settings.version}`}</strong>
            <small>{settings.updatedAtUtc ? new Date(settings.updatedAtUtc).toLocaleString("zh-CN") : "首次保存将创建 v1"}</small>
          </div>
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
          <section className="creative-brief" aria-label="当前创作基线">
            <div className="creative-asset-placeholder" style={{ aspectRatio: settings.aspectRatio.replace(":", "/") }}>
              {settings.cover ? (
                <div className="creative-cover-preview">
                  <Image
                    src={`${settings.cover.contentUrl}?v=${settings.cover.version}`}
                    alt={`${settings.projectName}概念封面`}
                    preview={{ mask: "预览封面" }}
                  />
                </div>
              ) : (
                <><ImagePlus size={28} strokeWidth={1.5} /><strong>尚未生成封面</strong></>
              )}
              <button type="button" onClick={requestCoverGeneration} disabled={generatingCover || status === "saving"}>
                <Sparkles size={12} />
                {generatingCover ? "生成中" : settings.cover ? "重新生成" : "生成封面"}
              </button>
              <small>{settings.aspectRatio} · {settings.outputWidth} × {settings.outputHeight}</small>
            </div>
            <div className="creative-brief-copy">
              <span className="eyebrow">项目创作基线 / {settings.version === 0 ? "尚未发布" : `v${settings.version}`}</span>
              <h1>{settings.projectName}</h1>
              <p>{settings.description || "为项目建立清晰、可继承的创作基线。"}</p>
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
                <span className="field-heading">视觉风格 {renderAiFieldAction("visualStyle")}</span>
                <input required maxLength={200} value={settings.visualStyle} onChange={(event) => updateField("visualStyle", event.target.value)} />
              </label>
              <label>
                <span className="field-heading">主角物种 {renderAiFieldAction("protagonistSpecies")}</span>
                <input required maxLength={200} value={settings.protagonistSpecies} onChange={(event) => updateField("protagonistSpecies", event.target.value)} />
              </label>
              <label className="span-2">
                <span className="field-heading">美术方向 {renderAiFieldAction("artDirection")}</span>
                <textarea rows={4} maxLength={2000} value={settings.artDirection} onChange={(event) => updateField("artDirection", event.target.value)} />
              </label>
              <label className="span-2">
                <span className="field-heading">角色造型硬约束 {renderAiFieldAction("characterDesign")}</span>
                <textarea required rows={4} maxLength={1000} value={settings.characterDesign} onChange={(event) => updateField("characterDesign", event.target.value)} />
                <small>主人公的物种、体型、服装和拟人化程度必须在这里明确。</small>
              </label>
              <label className="span-2">
                <span className="field-heading">色彩策略 {renderAiFieldAction("colorPalette")}</span>
                <textarea rows={3} maxLength={1000} value={settings.colorPalette} onChange={(event) => updateField("colorPalette", event.target.value)} />
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
                <span className="field-heading">摄影语言 {renderAiFieldAction("cameraLanguage")}</span>
                <textarea rows={4} maxLength={2000} value={settings.cameraLanguage} onChange={(event) => updateField("cameraLanguage", event.target.value)} />
              </label>
              <label className="span-2">
                <span className="field-heading">声音策略 {renderAiFieldAction("soundStrategy")}</span>
                <textarea rows={4} maxLength={2000} value={settings.soundStrategy} onChange={(event) => updateField("soundStrategy", event.target.value)} />
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
              {renderAiFieldAction("imagePromptPrefix")}
            </div>
            <textarea
              className="prompt-prefix"
              rows={6}
              maxLength={4000}
              value={settings.imagePromptPrefix}
              onChange={(event) => updateField("imagePromptPrefix", event.target.value)}
              placeholder="例如：法式彩色冒险漫画，拟人犬角色，清晰墨线……"
            />
          </section>

          {error && <p className="settings-error">{error}</p>}
          <div className="settings-submit-bar">
            <div>
              <strong>{status === "dirty" ? "设定有修改" : `当前为 v${settings.version}`}</strong>
              <span>每次保存都会保留一个可追溯版本。</span>
            </div>
            <button className="primary-button" type="submit" disabled={status === "saving" || status === "idle" || status === "saved"}>
              <Save size={14} />
              {status === "saving" ? "正在保存" : `保存为 v${settings.version + 1}`}
            </button>
          </div>
        </form>
      </div>
      {coverConfirmation && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={() => setCoverConfirmation(false)}
        >
          <form
            className="dialog ai-assist-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              const instruction = coverInstruction.trim();
              setCoverConfirmation(false);
              void generateCover(instruction || undefined);
            }}
          >
            <span className="eyebrow">GPT-IMAGE-2 / 重新生成</span>
            <h2>确认重新生成概念封面</h2>
            <p>新封面会保存为下一个资产版本，当前版本仍会保留。</p>
            <label>
              <span>本次调整意见（可选）</span>
              <textarea
                autoFocus
                rows={4}
                maxLength={1000}
                value={coverInstruction}
                onChange={(event) => setCoverInstruction(event.target.value)}
                placeholder="例如：强化三位主角的动作姿态，减少背景人物，保留现有漫画风格"
              />
            </label>
            <div>
              <button className="secondary-button" type="button" onClick={() => setCoverConfirmation(false)}>取消</button>
              <button className="primary-button" type="submit">
                <Sparkles size={13} />
                确认重新生成
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
            <span className="eyebrow">GPT-5.4 / AI 优化</span>
            <h2>确认优化“{assistFieldLabels[assistConfirmation]}”</h2>
            <p>AI 将基于当前内容和完整项目设定生成替换文本，结果会先回填到表单，不会自动保存版本。</p>
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
  const [activeSource, setActiveSource] = useState("source-e01");
  const [selectedSection, setSelectedSection] = useState("01 初遇");
  return (
    <div className="page full-height-page">
      <PageTitle
        eyebrow="故事结构 / 原文"
        title="原文分集"
        description="原文集与生产剧本集独立管理，通过改编映射建立关系"
        action={
          <div className="button-group">
            <button className="secondary-button">
              <Upload size={14} />
              上传原文
            </button>
            <button className="primary-button">
              <Plus size={14} />
              新建原文集
            </button>
          </div>
        }
      />
      <div className="document-workspace">
        <aside className="document-tree">
          <div className="tree-search">
            <Search size={14} />
            <input placeholder="搜索原文" />
          </div>
          {[
            {
              id: "source-e01",
              name: "原文 E01",
              sections: ["01 初遇", "02 文件"],
            },
            {
              id: "source-e02",
              name: "原文 E02",
              sections: ["01 追查", "02 误导", "03 转折"],
            },
            {
              id: "source-e03",
              name: "原文 E03",
              sections: ["01 真相", "02 回收"],
            },
          ].map((episode) => (
            <div className="tree-group" key={episode.id}>
              <button
                className={
                  activeSource === episode.id
                    ? "tree-episode active"
                    : "tree-episode"
                }
                onClick={() => setActiveSource(episode.id)}
              >
                <ChevronDown size={13} />
                <strong>{episode.name}</strong>
                <span>{episode.sections.length}</span>
              </button>
              {activeSource === episode.id &&
                episode.sections.map((section) => (
                  <button
                    className={
                      selectedSection === section
                        ? "tree-section active"
                        : "tree-section"
                    }
                    onClick={() => setSelectedSection(section)}
                    key={section}
                  >
                    {section}
                  </button>
                ))}
            </div>
          ))}
        </aside>
        <article className="reader">
          <header>
            <div>
              <span className="eyebrow">原文 E01 / {selectedSection}</span>
              <h2>第一章：雨夜的天桥</h2>
            </div>
            <div>
              <button className="icon-button">
                <Edit3 size={15} />
              </button>
              <button className="icon-button">
                <MoreHorizontal size={16} />
              </button>
            </div>
          </header>
          <div className="source-copy">
            <p>
              凌晨五点，天桥下的路灯还没有熄。林墨提着红色文件袋，穿过刚刚开始冒热气的早点摊。
            </p>
            <p className="evidence-highlight">
              他停在食堂门口。玻璃门上贴着一张褪色的招聘启事，落款日期是三年前。
            </p>
            <p>电话在口袋里震动。屏幕上没有名字，只有一串本地号码。</p>
            <blockquote>“文件不要交给任何人。尤其是周岚。”</blockquote>
            <p>通话戛然而止。林墨抬头时，发现食堂二楼的灯亮了。</p>
          </div>
        </article>
        <aside className="evidence-panel">
          <span className="eyebrow">结构提取</span>
          <h3>当前证据</h3>
          <div className="evidence-card">
            <span>人物</span>
            <strong>林墨</strong>
            <p>持有红色文件袋，收到匿名警告。</p>
          </div>
          <div className="evidence-card">
            <span>场景</span>
            <strong>天桥食堂 · 外部</strong>
            <p>凌晨、路灯、早点摊，二楼亮灯。</p>
          </div>
          <div className="evidence-card">
            <span>道具</span>
            <strong>红色文件袋</strong>
            <p>关键叙事道具，内容未知。</p>
          </div>
          <button className="secondary-button wide">
            <Link2 size={14} />
            加入改编证据
          </button>
        </aside>
      </div>
    </div>
  );
}

export function OutlinePage() {
  const [view, setView] = useState<"outline" | "mapping">("mapping");
  return (
    <div className="page">
      <PageTitle
        eyebrow="故事结构 / 改编"
        title="改编大纲"
        description="明确原文取舍，以及原文集到生产集的多对多映射"
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
            <span>原文集 / Section</span>
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

const assets = [
  {
    name: "林墨",
    type: "人物",
    status: "已批准",
    ref: "3 / 4",
    usage: "3 集 · 22 镜",
    tone: "portrait-a",
  },
  {
    name: "周岚",
    type: "人物",
    status: "待审阅",
    ref: "2 / 3",
    usage: "2 集 · 14 镜",
    tone: "portrait-b",
  },
  {
    name: "食堂老板",
    type: "人物",
    status: "草稿",
    ref: "0 / 2",
    usage: "1 集 · 6 镜",
    tone: "portrait-c",
  },
];

export function AssetsPage() {
  const params = useParams();
  const [selected, setSelected] = useState(assets[0]);
  const [createOpen, setCreateOpen] = useState(false);
  const kind =
    params.assetType === "scenes"
      ? "场景"
      : params.assetType === "props"
        ? "道具"
        : "人物";
  return (
    <div className="page full-height-page">
      <PageTitle
        eyebrow="项目共享资产"
        title="资产圣经"
        description="设定与参考图跨生产集共享，具体版本由各集锁定"
        action={
          <div className="button-group">
            <label className="secondary-button file-button">
              <Upload size={14} />
              上传文件
              <input type="file" />
            </label>
            <button
              className="secondary-button"
              onClick={() => setCreateOpen(true)}
            >
              <Plus size={14} />
              手动创建
            </button>
            <button className="primary-button">
              <WandSparkles size={14} />让 Agent 生成
            </button>
          </div>
        }
      />
      <div className="asset-tabs">
        {[
          ["人物", "characters", 8],
          ["场景", "scenes", 12],
          ["道具", "props", 6],
        ].map(([item, path, count]) => (
          <NavLink
            className={item === kind ? "active" : ""}
            to={`../${path}`}
            relative="path"
            key={item}
          >
            {item}
            <span>{count}</span>
          </NavLink>
        ))}
      </div>
      <div className="asset-workspace">
        <section className="asset-list-panel">
          <div className="table-tools">
            <label>
              <Search size={14} />
              <input placeholder={`搜索${kind}`} />
            </label>
            <button className="icon-button">
              <Filter size={15} />
            </button>
          </div>
          <div className="asset-table-head">
            <span>名称</span>
            <span>设定状态</span>
            <span>参考图</span>
          </div>
          {assets.map((asset) => (
            <button
              className={
                selected.name === asset.name ? "asset-row active" : "asset-row"
              }
              onClick={() => setSelected(asset)}
              key={asset.name}
            >
              <span className={`asset-thumb ${asset.tone}`}>
                {asset.name.slice(0, 1)}
              </span>
              <span>
                <strong>{asset.name}</strong>
                <small>{asset.usage}</small>
              </span>
              <span
                className={`state-label ${asset.status === "已批准" ? "approved" : "waiting"}`}
              >
                {asset.status}
              </span>
              <span>{asset.ref}</span>
            </button>
          ))}
        </section>
        <section className="asset-detail">
          <header>
            <div className={`asset-hero ${selected.tone}`}>
              <span>{selected.name}</span>
            </div>
            <div>
              <span className="eyebrow">{selected.type} · v3 approved</span>
              <h2>{selected.name}</h2>
              <p>主角 · 产品经理 · 30 岁</p>
              <button className="secondary-button">
                <Edit3 size={14} />
                手动编辑
              </button>
            </div>
          </header>
          <div className="detail-tabs">
            <button className="active">设定</button>
            <button>状态变化</button>
            <button>视觉参考</button>
            <button>故事引用</button>
            <button>版本历史</button>
          </div>
          <dl className="detail-grid">
            <div>
              <dt>身份功能</dt>
              <dd>被卷入文件失踪事件的核心行动者</dd>
            </div>
            <div>
              <dt>视觉锚点</dt>
              <dd>窄长脸、短黑发、深灰风衣、克制疲惫</dd>
            </div>
            <div>
              <dt>必须保留</dt>
              <dd>左眉尾浅疤、银色腕表、衣领磨损</dd>
            </div>
            <div>
              <dt>禁止项</dt>
              <dd>夸张妆容、潮牌标识、明亮暖色服装</dd>
            </div>
          </dl>
          <div className="reference-strip">
            <header>
              <strong>设定图</strong>
              <span>3 张已批准 · 1 张缺失</span>
              <button className="text-button">
                <ImagePlus size={14} />
                上传设定图
              </button>
            </header>
            <div>
              <span className="ref-image portrait-a">正面</span>
              <span className="ref-image portrait-b">侧面</span>
              <span className="ref-image portrait-c">全身</span>
              <button className="missing-image">
                <Plus size={17} />
                添加背面
              </button>
            </div>
          </div>
        </section>
      </div>
      {createOpen && (
        <div
          className="modal-backdrop"
          onMouseDown={() => setCreateOpen(false)}
        >
          <div
            className="dialog"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <span className="eyebrow">手动创建</span>
            <h2>新建{kind}</h2>
            <label>
              <span>名称</span>
              <input autoFocus placeholder={`输入${kind}名称`} />
            </label>
            <label>
              <span>简要描述</span>
              <textarea placeholder="输入身份、外观或叙事功能" />
            </label>
            <div>
              <button
                className="secondary-button"
                onClick={() => setCreateOpen(false)}
              >
                取消
              </button>
              <button
                className="primary-button"
                onClick={() => setCreateOpen(false)}
              >
                创建草稿
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export function ScriptPage() {
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

const shots = Array.from({ length: 12 }, (_, index) => ({
  id: `S0${Math.floor(index / 4) + 1}-${String((index % 4) + 1).padStart(2, "0")}`,
  duration: [4.5, 5, 3.2, 6.1][index % 4],
  status: index === 5 ? "blocked" : index < 7 ? "ready" : "draft",
}));

export function StoryboardPage() {
  const { productionEpisodeId } = useParams();
  const episode =
    episodes.find((item) => item.id === productionEpisodeId) ?? episodes[0];
  const [view, setView] = useState<"board" | "table">("board");
  const [selected, setSelected] = useState(shots[1].id);
  const [exceptionOnly, setExceptionOnly] = useState(false);
  const visibleShots = exceptionOnly
    ? shots.filter((shot) => shot.status === "blocked")
    : shots;
  return (
    <div className="page">
      <PageTitle
        eyebrow={`分镜 / ${episode.code}`}
        title="分镜工作区"
        description="18 shots · 98.4 秒 · beat 覆盖 17/18"
        action={
          <div className="button-group">
            <EpisodeSelect value={productionEpisodeId} />
            <button className="primary-button">
              <WandSparkles size={14} />
              生成镜头
            </button>
          </div>
        }
      />
      <div className="workspace-toolbar">
        <div className="segmented">
          <button
            className={view === "board" ? "active" : ""}
            onClick={() => setView("board")}
          >
            分镜板
          </button>
          <button
            className={view === "table" ? "active" : ""}
            onClick={() => setView("table")}
          >
            镜头表
          </button>
          <button>节拍覆盖</button>
          <button>素材轨道</button>
        </div>
        <div>
          <button
            className={`secondary-button ${exceptionOnly ? "active-filter" : ""}`}
            onClick={() => setExceptionOnly(!exceptionOnly)}
          >
            <Filter size={14} />
            {exceptionOnly ? "显示全部" : "只看异常"}
          </button>
        </div>
      </div>
      {view === "board" ? (
        <div className="storyboard-grid">
          {visibleShots.map((shot, index) => (
            <button
              className={`shot-card ${selected === shot.id ? "selected" : ""}`}
              onClick={() => setSelected(shot.id)}
              key={shot.id}
            >
              <div className={`shot-frame frame-${index % 4}`}>
                <span>
                  {shot.status === "blocked" ? "缺少参考图" : "PREVIEW"}
                </span>
                <b>{index + 1}</b>
              </div>
              <div>
                <strong>{shot.id}</strong>
                <span>{shot.duration}s</span>
              </div>
              <p>
                {index % 3 === 0
                  ? "林墨进入食堂，镜头缓慢推进"
                  : index % 3 === 1
                    ? "红色文件袋特写，灯光闪烁"
                    : "周岚转身，保持侧逆光"}
              </p>
              <small className={shot.status}>
                {shot.status === "ready"
                  ? "参考完整"
                  : shot.status === "blocked"
                    ? "阻断"
                    : "草稿"}
              </small>
            </button>
          ))}
        </div>
      ) : (
        <div className="data-table">
          <div className="table-row table-head">
            <span>镜号</span>
            <span>主体</span>
            <span>景别 / 机位</span>
            <span>动作</span>
            <span>时长</span>
            <span>状态</span>
          </div>
          {visibleShots.map((shot, index) => (
            <button className="table-row" key={shot.id}>
              <strong>{shot.id}</strong>
              <span>{index % 2 ? "红色文件袋" : "林墨"}</span>
              <span>{index % 3 ? "中景 · 平视" : "特写 · 俯拍"}</span>
              <span>缓慢推进并停在动作末端</span>
              <span>{shot.duration}s</span>
              <span className={`state-label ${shot.status}`}>
                {shot.status}
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

export function ProductionPage() {
  const [selected, setSelected] = useState("E01");
  return (
    <div className="page">
      <PageTitle
        eyebrow="媒体生产 / 按集总览"
        title="生产中心"
        description="不同生产集独立运行，共享 GPU 仅影响资源排队"
        action={
          <button className="primary-button">
            <Play size={14} />
            创建生产任务
          </button>
        }
      />
      <div className="production-summary">
        <div>
          <span className="eyebrow">运行中</span>
          <strong>2</strong>
          <small>生产集</small>
        </div>
        <div>
          <span className="eyebrow">排队</span>
          <strong>6</strong>
          <small>任务项</small>
        </div>
        <div>
          <span className="eyebrow">失败</span>
          <strong className="danger">2</strong>
          <small>可安全重试</small>
        </div>
        <div>
          <span className="eyebrow">GPU</span>
          <strong className="online">ONLINE</strong>
          <small>运行 00:28:14</small>
        </div>
      </div>
      <section className="production-table panel">
        <header className="panel-header">
          <h2>生产集</h2>
          <span>任务、错误和成片严格按集隔离</span>
        </header>
        {episodes.map((episode, index) => (
          <button
            className={
              selected === episode.code
                ? "production-row active"
                : "production-row"
            }
            onClick={() => setSelected(episode.code)}
            key={episode.code}
          >
            <span className="episode-code large">{episode.code}</span>
            <span className="production-title">
              <strong>{episode.title}</strong>
              <small>
                Run 04{index + 2} · Script {episode.code} v{4 - index}
              </small>
            </span>
            <div className="stage-pipeline">
              <span className="done">
                首帧 <b>{index ? "6/16" : "18/18"}</b>
              </span>
              <i />
              <span className={index < 2 ? "running" : ""}>
                视频 <b>{index ? "0/16" : "11/18"}</b>
              </span>
              <i />
              <span>
                配音 <b>{index ? "0/16" : "18/18"}</b>
              </span>
              <i />
              <span>
                组装 <b>等待</b>
              </span>
            </div>
            <span
              className={`state-label ${index < 2 ? "running" : "waiting"}`}
            >
              {index < 2 ? "运行中" : "待预检"}
            </span>
            <MoreHorizontal size={16} />
          </button>
        ))}
      </section>
      <section className="task-matrix panel">
        <header className="panel-header">
          <h2>{selected} · 逐镜阶段</h2>
          <span>最近活动 12 秒前</span>
        </header>
        <div className="matrix-head">
          <span>镜头</span>
          <span>首帧</span>
          <span>配音</span>
          <span>视频</span>
          <span>组装</span>
        </div>
        {shots.slice(0, 6).map((shot, index) => (
          <div className="matrix-row" key={shot.id}>
            <strong>{shot.id}</strong>
            <span className="done">succeeded</span>
            <span className={index === 2 ? "failed" : "done"}>
              {index === 2 ? "failed" : "succeeded"}
            </span>
            <span
              className={
                index === 0 ? "done" : index === 1 ? "running" : "queued"
              }
            >
              {index === 0 ? "succeeded" : index === 1 ? "running" : "queued"}
            </span>
            <span>waiting</span>
          </div>
        ))}
      </section>
    </div>
  );
}

export function ReferencesPage() {
  const [filter, setFilter] = useState("全部");
  const [reviewOnly, setReviewOnly] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [approved, setApproved] = useState<string[]>([]);
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
    status:
      approved.includes(item.name) || index % 3 !== 1 ? "已批准" : "待审阅",
  }));
  const visibleReferences = references.filter(
    (item) =>
      (filter === "全部" || item.type === filter) &&
      (!reviewOnly || item.status === "待审阅"),
  );
  const toggleReference = (name: string) =>
    setSelected((current) =>
      current.includes(name)
        ? current.filter((item) => item !== name)
        : [...current, name],
    );
  const approveSelected = () => {
    setApproved((current) => [...new Set([...current, ...selected])]);
    setSelected([]);
  };
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
        <div className="button-group">
          <button
            className={`secondary-button ${reviewOnly ? "active-filter" : ""}`}
            onClick={() => setReviewOnly(!reviewOnly)}
          >
            <Filter size={14} />
            {reviewOnly ? "显示全部状态" : "只看待审阅"}
          </button>
          <span className="coverage">覆盖 <strong>14 / 18</strong></span>
        </div>
      </div>
      <div className="reference-grid">
        {visibleReferences.map((item) => (
          <button
            className={`reference-card ${selected.includes(item.name) ? "selected" : ""}`}
            onClick={() => toggleReference(item.name)}
            key={item.name}
          >
            <div className={`reference-visual ref-${item.index}`}>
              <span>{item.status}</span>
              <i className="reference-check"><CheckSquare2 size={15} /></i>
            </div>
            <strong>{item.name}</strong>
            <small>
              {item.type} · 被 {item.index + 4} 个 shot 使用
            </small>
          </button>
        ))}
      </div>
      {selected.length > 0 && (
        <div className="batch-action-bar">
          <span>已选择 <strong>{selected.length}</strong> 项</span>
          <button className="text-button" onClick={() => setSelected([])}>取消选择</button>
          <button className="primary-button" onClick={approveSelected}>
            <Check size={14} />批量批准
          </button>
        </div>
      )}
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
            {shots.slice(0, 7).map((shot, index) => (
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
