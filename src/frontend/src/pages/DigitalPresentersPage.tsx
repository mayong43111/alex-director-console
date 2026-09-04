import { useEffect, useState, type FormEvent } from "react";
import {
  ArrowRight,
  Bot,
  Check,
  Clapperboard,
  Download,
  ImagePlus,
  Images,
  Layers3,
  Mic2,
  Play,
  Plus,
  Save,
  Settings2,
  Sparkles,
  Upload,
  Video,
} from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { builtInAgentIds, invokeAgent } from "../api/agents";
import {
  createDigitalPresenter,
  digitalPresenterMediaUrl,
  generateDigitalPresenterImagePrompt,
  generateDigitalPresenterFirstFrame,
  generateDigitalPresenterVideo,
  generateDigitalPresenterVideoPrompt,
  getFoundryImageConfiguration,
  listDigitalPresenters,
  saveDigitalPresenterEpisode,
  saveDigitalPresenterShot,
  setFoundryImageProvider,
  type DigitalPresenter,
  type DigitalPresenterEpisode,
  type DigitalPresenterShot,
  type FoundryImageConfiguration,
} from "../api/digitalPresenters";

const projectId = "00000000-0000-0000-0000-000000000001";

function usePresenter(presenterId?: string) {
  const [presenter, setPresenter] = useState<DigitalPresenter | null>(null);
  useEffect(() => {
    if (presenterId)
      listDigitalPresenters(projectId)
        .then((items) =>
          setPresenter(items.find((item) => item.id === presenterId) ?? null),
        )
        .catch(() => setPresenter(null));
  }, [presenterId]);
  return presenter;
}

function Frame({
  children,
  eyebrow,
  title,
  description,
}: {
  children: React.ReactNode;
  eyebrow: string;
  title: string;
  description: string;
}) {
  return (
    <div className="digital-page">
      <header className="digital-page-header">
        <div>
          <span>{eyebrow}</span>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
      </header>
      {children}
    </div>
  );
}

export function DigitalPresentersPage() {
  const navigate = useNavigate();
  const [presenters, setPresenters] = useState<DigitalPresenter[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    listDigitalPresenters(projectId)
      .then(setPresenters)
      .catch(() => setPresenters([]));
  }, []);
  const create = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    try {
      const item = await createDigitalPresenter(
        projectId,
        new FormData(event.currentTarget),
      );
      navigate(`/projects/${projectId}/digital-presenters/${item.id}`);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "创建失败。");
    }
  };
  return (
    <Frame
      eyebrow="数字人工作室"
      title="数字人资产"
      description="先建立可复用的数字人，再为它创建多个独立剧集。"
    >
      <div className="digital-create-layout">
        <section className="digital-create-panel">
          <div className="section-heading">
            <div>
              <span>STEP 01</span>
              <h2>新建数字人</h2>
              <p>人物形象和参考声音会成为基础资产。</p>
            </div>
            <Plus size={18} />
          </div>
          <form className="digital-form" onSubmit={create}>
            <label>
              数字人名称
              <input name="name" placeholder="例如：法律讲解员" required />
            </label>
            <label>
              <Images size={15} />
              人物形象参考
              <input name="identity" type="file" accept="image/*" required />
            </label>
            <label>
              <ImagePlus size={15} />
              默认背景参考
              <input name="background" type="file" accept="image/*" />
            </label>
            <label>
              <Sparkles size={15} />
              默认服饰参考
              <input name="outfit" type="file" accept="image/*" />
            </label>
            <label>
              <Mic2 size={15} />
              声音参考
              <input name="voice" type="file" accept="audio/*" required />
            </label>
            <button className="primary-command" type="submit">
              <Upload size={15} />
              创建数字人
            </button>
          </form>
          {error && <p className="digital-presenter-error">{error}</p>}
        </section>
        <section className="digital-list-panel">
          <div className="section-heading">
            <div>
              <span>已创建</span>
              <h2>{presenters.length} 个数字人</h2>
            </div>
          </div>
          {presenters.map((item) => (
            <button
              className="digital-list-row"
              key={item.id}
              onClick={() =>
                navigate(`/projects/${projectId}/digital-presenters/${item.id}`)
              }
            >
              <img
                src={digitalPresenterMediaUrl(
                  projectId,
                  item.identityImageAssetId,
                )}
                alt=""
              />
              <span>
                <b>{item.name}</b>
                <small>{item.episodes.length} 个剧集</small>
              </span>
              <ArrowRight size={16} />
            </button>
          ))}
          {!presenters.length && (
            <div className="digital-empty-line">还没有数字人资产。</div>
          )}
        </section>
      </div>
    </Frame>
  );
}

export function DigitalPresenterProfilePage() {
  const navigate = useNavigate();
  const { presenterId = "" } = useParams();
  const presenter = usePresenter(presenterId);
  const [imageConfig, setImageConfig] = useState<FoundryImageConfiguration | null>(null);
  const [providerBusy, setProviderBusy] = useState(false);
  useEffect(() => {
    getFoundryImageConfiguration().then(setImageConfig).catch(() => setImageConfig(null));
  }, []);
  if (!presenter)
    return (
      <Frame
        eyebrow="数字人配置"
        title="加载数字人"
        description="正在读取资产。"
      >
        <div className="digital-empty-state">找不到这个数字人。</div>
      </Frame>
    );
  return (
    <Frame
      eyebrow="STEP 02 / 数字人配置"
      title={presenter.name}
      description="管理基础参考素材，并设置后续剧集的输出规格。"
    >
      <div className="digital-profile-layout">
        <section className="digital-profile-main">
          <div className="section-heading">
            <div>
              <span>基础参考</span>
              <h2>人物与声音</h2>
              <p>修改参考素材不会影响已经生成的剧集视频。</p>
            </div>
            <button className="secondary-command">
              <Upload size={15} />
              上传 / 替换参考
            </button>
          </div>
          <div className="profile-reference-grid">
            <figure>
              <img
                src={digitalPresenterMediaUrl(
                  projectId,
                  presenter.identityImageAssetId,
                )}
                alt="人物形象"
              />
              <figcaption>人物形象参考</figcaption>
            </figure>
            <div className="profile-voice">
              <Mic2 size={18} />
              <b>参考声音</b>
              <small>提供音色方向，不复制参考音频内容</small>
              <audio
                controls
                src={digitalPresenterMediaUrl(
                  projectId,
                  presenter.voiceAssetId,
                )}
              />
            </div>
          </div>
          <div className="output-settings">
            <div className="section-heading">
              <div>
                <span>输出规格</span>
                <h2>生成设置</h2>
              </div>
              <Settings2 size={18} />
            </div>
            <label>
              分辨率
              <select defaultValue="720p">
                <option>480p</option>
                <option>720p</option>
                <option>1080p</option>
                <option>2K</option>
              </select>
            </label>
            <label>
              画面比例
              <select defaultValue="9:16">
                <option>9:16 竖屏</option>
                <option>16:9 横屏</option>
                <option>1:1 方形</option>
                <option>4:3 横幅</option>
              </select>
            </label>
            <label>
              默认时长
              <select defaultValue="auto">
                <option value="auto">按对白自动计算</option>
                <option>4 秒</option>
                <option>8 秒</option>
                <option>15 秒</option>
              </select>
            </label>
          </div>
          <div className="image-provider-settings">
            <div>
              <span>图片生成引擎</span>
              <small>{imageConfig?.imageConfigured ? `${imageConfig.imageDeployment} · ${imageConfig.imageQuality}` : "尚未配置"}</small>
            </div>
            <select
              value={imageConfig?.imageProvider ?? "azure-foundry"}
              disabled={!imageConfig || providerBusy}
              onChange={async (event) => {
                setProviderBusy(true);
                try {
                  setImageConfig(await setFoundryImageProvider(event.target.value));
                } finally {
                  setProviderBusy(false);
                }
              }}
            >
              <option value="azure-foundry">gpt-image-2</option>
              <option value="comfyui">ComfyUI</option>
            </select>
          </div>
          <button
            className="primary-command next-command"
            onClick={() =>
              navigate(
                `/projects/${projectId}/digital-presenters/${presenter.id}/episodes/new`,
              )
            }
          >
            <Plus size={15} />
            创建剧集 <ArrowRight size={15} />
          </button>
        </section>
        <aside className="digital-profile-side">
          <div className="section-heading">
            <div>
              <span>剧集</span>
              <h2>{presenter.episodes.length} 集</h2>
            </div>
            <Layers3 size={18} />
          </div>
          {presenter.episodes.map((item) => (
            <button
              className="digital-list-row"
              key={item.id}
              onClick={() =>
                navigate(
                  `/projects/${projectId}/digital-presenters/${presenter.id}/episodes/${item.id}`,
                )
              }
            >
              <span>
                <b>
                  E{String(item.episodeNumber).padStart(2, "0")} · {item.title}
                </b>
                <small>{item.shots.length} 个分镜</small>
              </span>
              <ArrowRight size={16} />
            </button>
          ))}
          {!presenter.episodes.length && (
            <div className="digital-empty-line">创建剧集后会显示在这里。</div>
          )}
        </aside>
      </div>
    </Frame>
  );
}

export function DigitalShotEditor({
  projectId,
  presenterId,
  episodeId,
  shot,
}: {
  projectId: string;
  presenterId: string;
  episodeId: string;
  shot: DigitalPresenterShot;
}) {
  const [open, setOpen] = useState(false);
  const [imagePrompt, setImagePrompt] = useState(shot.imagePrompt);
  const [videoPrompt, setVideoPrompt] = useState(shot.videoPrompt);
  const [activeAction, setActiveAction] = useState<string | null>(null);
  const [imageMessage, setImageMessage] = useState("");
  const [videoMessage, setVideoMessage] = useState("");
  const [generatedAssetId, setGeneratedAssetId] = useState<string | null>(shot.firstFrameAssetId);
  const [videoAssetId, setVideoAssetId] = useState<string | null>(shot.videoAssetId);
  const saveImagePrompt = async () => {
    setActiveAction("save-image-prompt");
    setImageMessage("");
    try {
      await saveDigitalPresenterShot(
        projectId,
        presenterId,
        episodeId,
        shot.id,
        { imagePrompt },
      );
      setImageMessage("图片提示词已保存");
    } catch (cause) {
      setImageMessage(cause instanceof Error ? cause.message : "保存失败");
    } finally {
      setActiveAction(null);
    }
  };
  const createImagePrompt = async () => {
    setActiveAction("image-prompt");
    setImageMessage("");
    try {
      const result = await generateDigitalPresenterImagePrompt(projectId, presenterId, episodeId, shot.id);
      setImagePrompt(result.imagePrompt);
      setImageMessage("图片提示词已生成，可继续修改");
    } catch (cause) {
      setImageMessage(cause instanceof Error ? cause.message : "图片提示词生成失败");
    } finally {
      setActiveAction(null);
    }
  };
  const generateFirstFrame = async () => {
    setActiveAction("first-frame");
    setImageMessage("首帧资源生成中...");
    try {
      const result = await generateDigitalPresenterFirstFrame(
        projectId,
        presenterId,
        episodeId,
        shot.id,
        imagePrompt,
      );
      setGeneratedAssetId(result.firstFrameAssetId);
      setImageMessage("首帧资源已生成");
    } catch (cause) {
      setImageMessage(cause instanceof Error ? cause.message : "首帧生成失败");
    } finally {
      setActiveAction(null);
    }
  };
  const createVideoPrompt = async () => {
    setActiveAction("video-prompt");
    setVideoMessage("");
    try {
      const result = await generateDigitalPresenterVideoPrompt(projectId, presenterId, episodeId, shot.id);
      setVideoPrompt(result.videoPrompt);
      setVideoMessage("视频提示词已生成，可继续修改");
    } catch (cause) {
      setVideoMessage(cause instanceof Error ? cause.message : "视频提示词生成失败");
    } finally {
      setActiveAction(null);
    }
  };
  const saveVideoPrompt = async () => {
    setActiveAction("save-video-prompt");
    setVideoMessage("");
    try {
      await saveDigitalPresenterShot(projectId, presenterId, episodeId, shot.id, { videoPrompt });
      setVideoMessage("视频提示词已保存");
    } catch (cause) {
      setVideoMessage(cause instanceof Error ? cause.message : "保存失败");
    } finally {
      setActiveAction(null);
    }
  };
  const generateVideo = async () => {
    setActiveAction("video");
    setVideoMessage("H3 视频生成中，请保持 ComfyUI 在线...");
    try {
      const result = await generateDigitalPresenterVideo(projectId, presenterId, episodeId, shot.id);
      setVideoAssetId(result.videoAssetId);
      setVideoMessage("视频资源已生成");
    } catch (cause) {
      setVideoMessage(cause instanceof Error ? cause.message : "视频生成失败");
    } finally {
      setActiveAction(null);
    }
  };
  return (
    <article className={`digital-shot-card ${open ? "open" : ""}`}>
      <div
        className="digital-shot-summary"
        onClick={() => setOpen((value) => !value)}
      >
        <b>S{String(shot.sortOrder).padStart(2, "0")}</b>
        <p>{shot.dialogue}</p>
        <span>{shot.durationSeconds}s</span>
        <button
          className="secondary-command"
          onClick={(event) => {
            event.stopPropagation();
            setOpen((value) => !value);
          }}
        >
          {open ? "收起制作台" : "打开制作台"}
        </button>
      </div>
      {open && (
        <div className="digital-shot-editor">
          <section className="digital-production-stage">
            <header><div><span>01 / IMAGE</span><h3>首帧图片</h3></div><small>{generatedAssetId ? "资源已生成" : imagePrompt ? "提示词已就绪" : "等待提示词"}</small></header>
            <div className="digital-production-body">
              <div className="digital-resource-preview">
                {generatedAssetId ? <img className="digital-shot-preview" src={digitalPresenterMediaUrl(projectId, generatedAssetId)} alt="首帧预览" /> : <div><Images size={24} /><span>首帧资源尚未生成</span></div>}
              </div>
              <div className="digital-prompt-workspace">
                <label>图片提示词<textarea rows={6} value={imagePrompt} onChange={(event) => setImagePrompt(event.target.value)} placeholder="先生成提示词，或在这里手动输入" /></label>
                <div className="digital-stage-actions">
                  <button className="secondary-command" onClick={createImagePrompt} disabled={activeAction !== null}><Sparkles size={14} />{activeAction === "image-prompt" ? "生成中" : imagePrompt ? "重新生成提示词" : "生成图片提示词"}</button>
                  <button className="secondary-command" onClick={saveImagePrompt} disabled={activeAction !== null || !imagePrompt.trim()}><Save size={14} />保存修改</button>
                  <button className="primary-command" onClick={generateFirstFrame} disabled={activeAction !== null || !imagePrompt.trim()}><ImagePlus size={14} />{activeAction === "first-frame" ? "生成中" : generatedAssetId ? "重新生成首帧" : "生成首帧资源"}</button>
                </div>
                <span className="digital-stage-message">{imageMessage || "提示词生成和首帧资源生成互不触发"}</span>
              </div>
            </div>
          </section>
          <section className="digital-production-stage video-stage">
            <header><div><span>02 / VIDEO</span><h3>镜头视频</h3></div><small>{shot.videoAssetId ? "资源已生成" : videoPrompt ? "提示词已就绪" : "等待提示词"}</small></header>
            <div className="digital-production-body">
              <div className="digital-resource-preview video">{videoAssetId ? <video className="digital-shot-preview" src={digitalPresenterMediaUrl(projectId, videoAssetId)} controls /> : <div><Video size={24} /><span>{generatedAssetId ? "首帧已就绪，等待视频生产" : "请先完成首帧资源"}</span></div>}</div>
              <div className="digital-prompt-workspace">
                <label>视频提示词<textarea rows={6} value={videoPrompt} onChange={(event) => setVideoPrompt(event.target.value)} placeholder="先生成提示词，或在这里手动输入" /></label>
                <div className="digital-stage-actions">
                  <button className="secondary-command" onClick={createVideoPrompt} disabled={activeAction !== null}><Sparkles size={14} />{activeAction === "video-prompt" ? "生成中" : videoPrompt ? "重新生成提示词" : "生成视频提示词"}</button>
                  <button className="secondary-command" onClick={saveVideoPrompt} disabled={activeAction !== null || !videoPrompt.trim()}><Save size={14} />保存修改</button>
                  <button className="primary-command" onClick={generateVideo} disabled={activeAction !== null || !generatedAssetId || !videoPrompt.trim()}><Video size={14} />{activeAction === "video" ? "生成中" : videoAssetId ? "重新生成视频" : "生成视频资源"}</button>
                </div>
                <span className="digital-stage-message">{videoMessage || "视频提示词不会自动启动资源生成"}</span>
              </div>
            </div>
          </section>
        </div>
      )}
    </article>
  );
}

export function DigitalPresenterEpisodePage() {
  const navigate = useNavigate();
  const { presenterId = "", episodeId = "" } = useParams();
  const presenter = usePresenter(presenterId);
  const episode = presenter?.episodes.find((item) => item.id === episodeId);
  const [title, setTitle] = useState("");
  const [dialogue, setDialogue] = useState("");
  const [scene, setScene] = useState("");
  const [sceneFile, setSceneFile] = useState("");
  const [outfitFile, setOutfitFile] = useState("");
  const [busy, setBusy] = useState(false);
  const [aiBusy, setAiBusy] = useState(false);
  useEffect(() => {
    if (episode) {
      setTitle(episode.title);
      setDialogue(episode.dialogue);
    }
  }, [episode]);
  const save = async () => {
    if (!title.trim() || !dialogue.trim() || busy) return;
    setBusy(true);
    try {
      const saved = await saveDigitalPresenterEpisode(
        projectId,
        presenterId,
        episodeId === "new" ? null : episodeId,
        {
          title,
          dialogue,
          backgroundImageAssetId: presenter?.backgroundImageAssetId ?? null,
          outfitImageAssetId: presenter?.outfitImageAssetId ?? null,
        },
      );
      navigate(
        `/projects/${projectId}/digital-presenters/${presenterId}/episodes/${saved.id}`,
      );
    } finally {
      setBusy(false);
    }
  };
  const write = async () => {
    setAiBusy(true);
    try {
      const result = await invokeAgent(
        builtInAgentIds.storyboardShotTextWriter,
        {
          input: dialogue || "请为一个数字人创作自然的法律普及对白。",
          context: {
            purpose: "digital-presenter-dialogue",
            title,
            constraints: ["单人独白", "自然短句"],
          },
          maxLength: 1200,
        },
      );
      setDialogue(result.value);
    } finally {
      setAiBusy(false);
    }
  };
  return (
    <Frame
      eyebrow={`STEP 03 / ${presenter?.name ?? "数字人"} / 剧集`}
      title={
        episode
          ? `E${String(episode.episodeNumber).padStart(2, "0")} · ${episode.title}`
          : "创建新剧集"
      }
      description="设置本集场景、服饰和对白，保存后生成分镜列表。"
    >
      <div className="episode-page-grid">
        <section className="episode-editor">
          <div className="section-heading">
            <div>
              <span>剧集信息</span>
              <h2>主题与对白</h2>
            </div>
            <button
              className="secondary-command"
              onClick={write}
              disabled={aiBusy}
            >
              <Bot size={15} />
              {aiBusy ? "生成中" : "AI 辅助写对白"}
            </button>
          </div>
          <label>
            剧集主题 / 标题
            <input
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              placeholder="例如：法律普及"
            />
          </label>
          <label>
            对白
            <textarea
              rows={12}
              value={dialogue}
              onChange={(event) => setDialogue(event.target.value)}
              placeholder="输入对白，系统会根据 H3 语速自动拆分镜头。"
            />
          </label>
          <div className="episode-actions">
            <span>3.8 字/秒 + 1 秒收口</span>
            <button className="primary-command" onClick={save} disabled={busy}>
              <Check size={15} />
              {busy ? "保存中" : "保存并生成分镜列表"}
            </button>
          </div>
        </section>
        <aside className="episode-assets">
          <div className="section-heading">
            <div>
              <span>本集覆盖</span>
              <h2>场景与服饰</h2>
            </div>
            <ImagePlus size={18} />
          </div>
          <label>
            场景参考图
            <input
              type="file"
              accept="image/*"
              onChange={(event) =>
                setSceneFile(event.target.files?.[0]?.name ?? "")
              }
            />
          </label>
          {sceneFile && <small>{sceneFile}</small>}
          <label>
            服装参考图
            <input
              type="file"
              accept="image/*"
              onChange={(event) =>
                setOutfitFile(event.target.files?.[0]?.name ?? "")
              }
            />
          </label>
          {outfitFile && <small>{outfitFile}</small>}
          <label>
            场景描述
            <textarea
              rows={5}
              value={scene}
              onChange={(event) => setScene(event.target.value)}
              placeholder="没有参考图时，在这里描述场景。"
            />
          </label>
          <button
            className="secondary-command"
            onClick={() =>
              setScene(
                "专业、明亮的法律咨询室，人物坐在桌后面对镜头，背景有书架、绿植与柔和侧光。",
              )
            }
          >
            <Sparkles size={15} />
            AI 生成场景描述
          </button>
        </aside>
      </div>
      {episode && (
        <div className="episode-shot-list">
          <div className="section-heading">
            <div>
              <span>已生成</span>
              <h2>{episode.shots.length} 个分镜</h2>
            </div>
            <span className="digital-shot-mode"><Layers3 size={14} />逐镜头独立制作</span>
          </div>
          {episode.shots.map((shot) => (
            <DigitalShotEditor
              key={shot.id}
              projectId={projectId}
              presenterId={presenterId}
              episodeId={episode.id}
              shot={shot}
            />
          ))}
        </div>
      )}
      <div className="episode-next-bar">
        <span>下一步：批量或单独生产首帧、视频提示词和视频。</span>
        {episode && (
          <button
            className="secondary-command"
            onClick={() =>
              navigate(
                `/projects/${projectId}/digital-presenters/${presenterId}/episodes/${episode.id}/preview`,
              )
            }
          >
            <Play size={15} />
            预览当前剧集
          </button>
        )}
      </div>
    </Frame>
  );
}

export function DigitalPresenterPreviewPage() {
  const navigate = useNavigate();
  const { presenterId = "", episodeId = "" } = useParams();
  const presenter = usePresenter(presenterId);
  const episode: DigitalPresenterEpisode | undefined = presenter?.episodes.find(
    (item) => item.id === episodeId,
  );
  return (
    <Frame
      eyebrow="STEP 05 / 当前剧集"
      title={episode?.title ?? "剧集预览"}
      description="查看当前剧集的镜头顺序、生成状态和最终拼接结果。"
    >
      <div className="preview-page">
        <div className="preview-toolbar">
          <div>
            <b>{episode?.shots.length ?? 0} 个分镜</b>
            <span>按顺序拼接预览</span>
          </div>
          <div>
            <button className="secondary-command" disabled>
              <Download size={15} />
              下载剧集
            </button>
            <button className="primary-command" disabled>
              <Play size={15} />
              播放预览
            </button>
          </div>
        </div>
        <div className="preview-filmstrip">
          {episode?.shots.map((shot) => (
            <article key={shot.id}>
              <div className="preview-thumb">
                <Video size={22} />
              </div>
              <b>S{String(shot.sortOrder).padStart(2, "0")}</b>
              <span>
                {shot.durationSeconds}s · {shot.dialogue}
              </span>
            </article>
          ))}
        </div>
        {!episode?.shots.length && (
          <div className="digital-empty-state">
            <Clapperboard size={34} />
            <h2>还没有分镜</h2>
            <p>回到剧集页面保存对白，即可生成分镜列表。</p>
            <button
              className="secondary-command"
              onClick={() =>
                navigate(
                  `/projects/${projectId}/digital-presenters/${presenterId}/episodes/${episodeId}`,
                )
              }
            >
              返回剧集
            </button>
          </div>
        )}
      </div>
    </Frame>
  );
}
