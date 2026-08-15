import { useEffect, useLayoutEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import {
  Box,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Clapperboard,
  Download,
  FileSearch,
  FileText,
  Film,
  Images,
  LoaderCircle,
  MapPinned,
  Maximize2,
  Minus,
  Music,
  Paperclip,
  Plus,
  RotateCcw,
  Send,
  Settings2,
  Square,
  Users,
  Video,
  WandSparkles,
  X,
} from 'lucide-react'
import { Navigate, matchPath, useLocation, useNavigate } from 'react-router-dom'
import {
  deleteProjectAsset,
  listAssetVersions,
  listProjectAssets,
  listShotAssetLinks,
  uploadProjectAsset,
} from './api/assets'
import { listProjects, upsertProject } from './api/projects'
import { listSkills, setSkillEnabled } from './api/skills'
import {
  getFoundryConfiguration,
  getRuntimeConfiguration,
  updateFoundryConfiguration,
  updateRuntimeConfiguration,
} from './api/system'
import { AssetPanel, type AssetSection } from './features/assets/AssetPanel'
import { SystemConfigurationPanel } from './features/configuration/SystemConfigurationPanel'
import { useDirectorConversation } from './features/conversations/useDirectorConversation'
import { languageStorageKey, loadLanguage, localize, type Language } from './i18n'
import type {
  AgentStatus,
  AssetRecord,
  GlobalFoundryConfiguration,
  MessageStreamEvent,
  MobilePanel,
  Project,
  ProjectRuntimeConfiguration,
  ScriptAnalysisRecord,
  ServiceState,
  ShotAssetLinkRecord,
  SkillDefinitionRecord,
  SkillRunRecord,
} from './models'
import './App.css'

const projectStorageKey = 'alex-director-console.projects'
const defaultProjectSettings = {
  description: '',
  formatPreset: '16:9',
  outputWidth: 1920,
  outputHeight: 1080,
  previewResolution: '960x540',
  languageModel: 'gpt-5.4',
  imageModel: 'gpt-image-2',
  videoModel: '',
}
const defaultRuntimeConfiguration: ProjectRuntimeConfiguration = {
  projectId: '',
  vmHost: '',
  vmPort: 22,
  vmUsername: 'azureuser',
  sshPrivateKeyPath: '%USERPROFILE%\\.ssh\\id_rsa',
  comfyUiPath: '/home/azureuser/ComfyUI',
  comfyUiPythonPath: '/home/azureuser/envs/comfy311/bin/python',
  comfyUiPort: 8188,
  localProxyPort: 8188,
  workflowDirectory: '/home/azureuser/ComfyUI/user/default/workflows',
  outputDirectory: '/home/azureuser/ComfyUI/output',
  updatedAtUtc: '',
}

const settingAssetLabels: Record<string, { text: string; image: string }> = {
  character: { text: '人物设定稿', image: '人物设定图' },
  scene: { text: '场景设定稿', image: '场景设定图' },
  prop: { text: '道具设定稿', image: '道具设定图' },
}

function getSettingSubjectName(asset: AssetRecord) {
  const label = settingAssetLabels[asset.type]?.text
  if (!label) return null
  const suffix = ` · ${label}`
  return asset.name.endsWith(suffix) ? asset.name.slice(0, -suffix.length).trim() : null
}
const defaultFoundryConfiguration: GlobalFoundryConfiguration = {
  openAiEndpoint: '',
  openAiDeployment: 'gpt-5.4',
  openAiApiKeyConfigured: false,
  imageEndpoint: '',
  imageDeployment: 'gpt-image-2',
  imageApiVersion: '2025-04-01-preview',
  imageQuality: 'medium',
  imageApiKeyConfigured: false,
  speechEndpoint: '',
  speechDeployment: 'tts',
  speechApiVersion: '2025-03-01-preview',
  speechApiKeyConfigured: false,
  updatedAtUtc: '',
}
const projectFormatPresets = [
  { id: '16:9', label: '16:9 · 1920×1080', width: 1920, height: 1080, imageSize: '1536x1024' },
  { id: '9:16', label: '9:16 · 1080×1920', width: 1080, height: 1920, imageSize: '1024x1536' },
  { id: '2.39:1', label: '2.39:1 · 2048×858', width: 2048, height: 858, imageSize: '1536x1024' },
  { id: '1:1', label: '1:1 · 1080×1080', width: 1080, height: 1080, imageSize: '1024x1024' },
  { id: '4:3', label: '4:3 · 1440×1080', width: 1440, height: 1080, imageSize: '1536x1024' },
  { id: 'custom', label: '自定义', width: 1920, height: 1080, imageSize: '1536x1024' },
] as const
const mobilePanels: Array<{ id: MobilePanel; label: string; labelEn: string }> = [
  { id: 'assets', label: '资产', labelEn: 'Assets' },
  { id: 'director', label: '导演台', labelEn: 'Director' },
  { id: 'review', label: '审阅', labelEn: 'Review' },
]

function isFinalVideo(asset: AssetRecord) {
  if (!asset.generationMetadataJson) return false
  try {
    const metadata = JSON.parse(asset.generationMetadataJson) as { operation?: string }
    return metadata.operation === 'assemble-project-video'
  } catch {
    return false
  }
}

const assetSections: AssetSection[] = [
  { id: 'scripts', type: 'script', label: '剧本', labelEn: 'Scripts', accept: '.md,.txt,.pdf,.doc,.docx', icon: FileText },
  { id: 'analyses', type: 'analysis', label: '分析', labelEn: 'Analysis', accept: '.md,.json', icon: FileSearch },
  { id: 'images', type: 'media', label: '图片素材', labelEn: 'Images', accept: 'image/*', icon: Images, contentTypePrefix: 'image/' },
  { id: 'final-videos', type: 'media', label: '成片', labelEn: 'Final Films', accept: 'video/*', icon: Film, contentTypePrefix: 'video/', matches: isFinalVideo },
  { id: 'videos', type: 'media', label: '视频素材', labelEn: 'Video Clips', accept: 'video/*', icon: Video, contentTypePrefix: 'video/', matches: (asset) => !isFinalVideo(asset) },
  { id: 'audio', type: 'media', label: '音频素材', labelEn: 'Audio', accept: 'audio/*', icon: Music, contentTypePrefix: 'audio/' },
  { id: 'characters', type: 'character', label: '人物', labelEn: 'Characters', accept: '.md,image/*,.pdf', icon: Users },
  { id: 'scenes', type: 'scene', label: '场景', labelEn: 'Scenes', accept: '.md,image/*,video/*,.pdf', icon: MapPinned },
  { id: 'props', type: 'prop', label: '道具', labelEn: 'Props', accept: '.md,image/*,.pdf', icon: Box },
  { id: 'shots', type: 'shot', label: '镜头', labelEn: 'Shots', accept: '.md,.txt,image/*,video/*,.pdf', icon: Clapperboard },
]

const groupedAssetSectionIds = new Set(['analyses', 'characters', 'scenes', 'props'])

function formatFileSize(sizeBytes: number) {
  if (sizeBytes < 1024) return `${sizeBytes} B`
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`
}

interface ImageGenerationMetadata {
  schemaVersion: number
  operation: 'generate' | 'generate-from-references' | 'edit' | 'merge-references'
  provider: string
  model: string
  prompt: string | null
  revisedPrompt: string | null
  parameters: {
    size: string
    quality: string
    count: number
    outputFormat: string
    apiVersion: string | null
  }
  sources: Array<{
    assetId: string
    name: string
    version: number
    description: string | null
  }>
}

interface SpeechGenerationMetadata {
  schemaVersion: number
  operation: 'text-to-speech'
  provider: string
  model: string
  prompt: string
  parameters: {
    voice: string
    instructions: string
    instructionsApplied: boolean
    speed: number
    responseFormat: string
    apiVersion: string | null
  }
}

interface VideoGenerationMetadata {
  schemaVersion: number
  operation: 'video-generation'
  provider: string
  model: string
  prompt: string
  parameters: {
    workflow: string
    width: number
    height: number
    frameCount: number
    fps: number
    frameFitMode: string
  }
  sources: Array<{
    assetId: string
    role: string
  }>
}

function parseImageGenerationMetadata(value: string | null): ImageGenerationMetadata | null {
  if (!value) return null
  try {
    return JSON.parse(value) as ImageGenerationMetadata
  } catch {
    return null
  }
}

function parseVideoGenerationMetadata(value: string | null): VideoGenerationMetadata | null {
  if (!value) return null
  try {
    const metadata = JSON.parse(value) as VideoGenerationMetadata
    return metadata.operation === 'video-generation' ? metadata : null
  } catch {
    return null
  }
}

function parseSpeechGenerationMetadata(value: string | null): SpeechGenerationMetadata | null {
  if (!value) return null
  try {
    const metadata = JSON.parse(value) as SpeechGenerationMetadata
    return metadata.operation === 'text-to-speech' ? metadata : null
  } catch {
    return null
  }
}

function formatImageOperation(operation: ImageGenerationMetadata['operation'], language: Language) {
  const labels: Record<ImageGenerationMetadata['operation'], [string, string]> = {
    generate: ['模型生成', 'Model generation'],
    'generate-from-references': ['参考图生成', 'Reference image generation'],
    edit: ['图片编辑', 'Image edit'],
    'merge-references': ['本地合成', 'Local composition'],
  }
  return localize(language, ...labels[operation])
}

function formatDate(value: string, language: Language) {
  return new Intl.DateTimeFormat(language, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}

function LanguageSwitch({ language, onChange }: { language: Language; onChange: (language: Language) => void }) {
  return (
    <div className="language-switch" role="group" aria-label={localize(language, '界面语言', 'Interface language')}>
      <button type="button" className={language === 'zh-CN' ? 'active' : undefined} aria-pressed={language === 'zh-CN'} onClick={() => onChange('zh-CN')}>中文</button>
      <button type="button" className={language === 'en-US' ? 'active' : undefined} aria-pressed={language === 'en-US'} onClick={() => onChange('en-US')}>EN</button>
    </div>
  )
}

function loadProjects(): Project[] {
  try {
    const storedProjects = localStorage.getItem(projectStorageKey)
    const parsedProjects = storedProjects ? (JSON.parse(storedProjects) as Partial<Project>[]) : []
    return parsedProjects
      .filter((project): project is Partial<Project> & Pick<Project, 'id' | 'name' | 'createdAt'> =>
        Boolean(project.id && project.name && project.createdAt))
      .map((project) => ({ ...defaultProjectSettings, ...project }))
  } catch {
    return []
  }
}

function greatestCommonDivisor(left: number, right: number): number {
  return right === 0 ? left : greatestCommonDivisor(right, left % right)
}

function getProjectFormat(project: Project) {
  const preset = projectFormatPresets.find((item) => item.id === project.formatPreset)
    ?? projectFormatPresets[0]
  const divisor = greatestCommonDivisor(project.outputWidth, project.outputHeight)
  return {
    aspectRatio: project.formatPreset === 'custom'
      ? `${project.outputWidth / divisor}:${project.outputHeight / divisor}`
      : preset.id,
    resolution: `${project.outputWidth}x${project.outputHeight}`,
    imageSize: project.outputWidth === project.outputHeight
      ? '1024x1024'
      : project.outputWidth > project.outputHeight ? '1536x1024' : '1024x1536',
  }
}

function getPreviewResolutionOptions(formatPreset: string, outputWidth: number, outputHeight: number) {
  if (outputWidth === outputHeight) return ['720x720', '1080x1080']
  if (outputWidth < outputHeight) return ['540x960', '720x1280', '1080x1920']
  if (formatPreset === '2.39:1') return ['1024x428', '2048x858']
  if (formatPreset === '4:3') return ['960x720', '1440x1080']
  return ['960x540', '1280x720', '1920x1080']
}

function App() {
  const location = useLocation()
  const navigate = useNavigate()
  const conversationRef = useRef<HTMLElement>(null)
  const scrollToLatestAfterLoadRef = useRef(false)
  const stickToLatestRef = useRef(true)
  const initializedAssetSectionProjectRef = useRef<string | null>(null)
  const [language, setLanguage] = useState<Language>(loadLanguage)
  const text = (chinese: string, english: string) => localize(language, chinese, english)
  const [apiState, setApiState] = useState<ServiceState>('checking')
  const [agentStatus, setAgentStatus] = useState<AgentStatus | null>(null)
  const [projects, setProjects] = useState<Project[]>([])
  const [projectsLoading, setProjectsLoading] = useState(true)
  const [newProjectName, setNewProjectName] = useState('')
  const [activeAssetSection, setActiveAssetSection] = useState('images')
  const [expandedAssetSections, setExpandedAssetSections] = useState<Set<string>>(
    () => new Set(['scripts', 'analyses', 'images', 'videos']),
  )
  const [directorOrder, setDirectorOrder] = useState('')
  const [selectedModel, setSelectedModel] = useState('gpt-5.4')
  const [selectingCustomImageModel, setSelectingCustomImageModel] = useState(false)
  const [selectingCustomVideoModel, setSelectingCustomVideoModel] = useState(false)
  const [attachments, setAttachments] = useState<File[]>([])
  const [assets, setAssets] = useState<AssetRecord[]>([])
  const [assetsLoading, setAssetsLoading] = useState(false)
  const [refreshingAssets, setRefreshingAssets] = useState(false)
  const [uploadingAssets, setUploadingAssets] = useState(false)
  const [deletingAssetId, setDeletingAssetId] = useState<string | null>(null)
  const [assetError, setAssetError] = useState<string | null>(null)
  const [selectedAsset, setSelectedAsset] = useState<AssetRecord | null>(null)
  const [assetVersions, setAssetVersions] = useState<AssetRecord[]>([])
  const [assetPreviewText, setAssetPreviewText] = useState<string | null>(null)
  const [assetPreviewLoading, setAssetPreviewLoading] = useState(false)
  const [assetPreviewError, setAssetPreviewError] = useState<string | null>(null)
  const [shotAssetLinks, setShotAssetLinks] = useState<ShotAssetLinkRecord[]>([])
  const [previewImage, setPreviewImage] = useState<AssetRecord | null>(null)
  const [previewZoom, setPreviewZoom] = useState(1)
  const [previewOffset, setPreviewOffset] = useState({ x: 0, y: 0 })
  const previewDragRef = useRef<{ pointerId: number; x: number; y: number } | null>(null)
  const [assetDetailExpanded, setAssetDetailExpanded] = useState(false)
  const [serviceStatusExpanded, setServiceStatusExpanded] = useState(false)
  const [skills, setSkills] = useState<SkillDefinitionRecord[]>([])
  const [selectedSkill, setSelectedSkill] = useState<SkillDefinitionRecord | null>(null)
  const [selectedSkillRun, setSelectedSkillRun] = useState<SkillRunRecord | null>(null)
  const [skillsLoading, setSkillsLoading] = useState(false)
  const [skillError, setSkillError] = useState<string | null>(null)
  const [runtimeConfiguration, setRuntimeConfiguration] = useState(defaultRuntimeConfiguration)
  const [runtimeConfigurationState, setRuntimeConfigurationState] = useState<'idle' | 'loading' | 'saving' | 'saved' | 'error'>('idle')
  const [foundryConfiguration, setFoundryConfiguration] = useState(defaultFoundryConfiguration)
  const [openAiApiKey, setOpenAiApiKey] = useState('')
  const [imageApiKey, setImageApiKey] = useState('')
  const [speechApiKey, setSpeechApiKey] = useState('')
  const [foundryConfigurationState, setFoundryConfigurationState] = useState<'idle' | 'loading' | 'saving' | 'saved' | 'error'>('idle')
  const [mobilePanel, setMobilePanel] = useState<MobilePanel>('director')
  useEffect(() => {
    localStorage.setItem(languageStorageKey, language)
    document.documentElement.lang = language
    document.title = localize(language, 'alex 导演台', 'alex Director Console')
  }, [language])
  const projectRoute = matchPath('/projects/:projectId/*', location.pathname)
    ?? matchPath('/projects/:projectId', location.pathname)
  const selectedProject = projects.find(
    (project) => project.id === projectRoute?.params.projectId,
  )
  const sidebarMode = location.pathname === `/projects/${projectRoute?.params.projectId}/skills`
    || location.pathname === `/projects/${projectRoute?.params.projectId}/configuration`
    ? 'configuration'
    : location.pathname === `/projects/${projectRoute?.params.projectId}/settings`
      ? 'settings'
      : 'assets'
  const selectedAssetSection =
    assetSections.find((section) => section.id === activeAssetSection) ?? assetSections[0]
  const groupedAssetSections = assetSections.filter((section) => groupedAssetSectionIds.has(section.id))
  const isGroupedAssetSection = groupedAssetSectionIds.has(selectedAssetSection.id)
  const visibleAssets = assets
    .filter((asset) => asset.type === selectedAssetSection.type
      && (!selectedAssetSection.contentTypePrefix
        || asset.contentType.startsWith(selectedAssetSection.contentTypePrefix))
      && (!selectedAssetSection.matches || selectedAssetSection.matches(asset)))
    .sort((left, right) => selectedAssetSection.type === 'shot'
      ? left.name.localeCompare(right.name, 'zh-CN')
      : new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime())
  const groupedAssetCount = assets.filter((asset) =>
    groupedAssetSections.some((section) => section.type === asset.type)).length
  const selectedSettingSubject = selectedAsset ? getSettingSubjectName(selectedAsset) : null
  const selectedSettingImageLabel = selectedAsset
    ? settingAssetLabels[selectedAsset.type]?.image
    : undefined
  const associatedSettingImages = selectedSettingSubject && selectedSettingImageLabel
    ? assets.filter((asset) =>
        asset.contentType.startsWith('image/')
        && asset.name.startsWith(`${selectedSettingSubject} · ${selectedSettingImageLabel}`))
    : []
  const mobilePanelIndex = mobilePanels.findIndex((panel) => panel.id === mobilePanel)
  const projectFormat = selectedProject ? getProjectFormat(selectedProject) : null
  const {
    messages,
    messagesLoading,
    sendingMessage,
    conversationError,
    sendDirectorMessage,
    stopDirectorMessage,
    retryDirectorMessage,
  } = useDirectorConversation({
    language,
    project: selectedProject,
    agentConfigured: agentStatus?.configured ?? false,
    selectedAsset,
    aspectRatio: projectFormat?.aspectRatio ?? '16:9',
    resolution: projectFormat?.resolution ?? '1920x1080',
    imageSize: projectFormat?.imageSize ?? '1536x1024',
    onMessageStart: prepareDirectorMessageScroll,
    onAssetGenerated: handleDirectorAssetGenerated,
    onCompleted: handleDirectorMessageCompleted,
  })
  const previewResolutionOptions = selectedProject
    ? getPreviewResolutionOptions(
        selectedProject.formatPreset,
        selectedProject.outputWidth,
        selectedProject.outputHeight,
      )
    : []

  const resetImagePreview = () => {
    setPreviewZoom(1)
    setPreviewOffset({ x: 0, y: 0 })
  }

  const openImagePreview = (asset: AssetRecord) => {
    resetImagePreview()
    setPreviewImage(asset)
  }

  const openGeneratedAsset = (asset: AssetRecord) => {
    if (asset.contentType.startsWith('image/')) {
      openImagePreview(asset)
      return
    }
    void reviewAsset(asset)
  }

  const closeImagePreview = () => {
    setPreviewImage(null)
    previewDragRef.current = null
  }

  const changePreviewZoom = (nextZoom: number) => {
    const zoom = Math.min(5, Math.max(0.5, Number(nextZoom.toFixed(2))))
    setPreviewZoom(zoom)
    if (zoom <= 1) setPreviewOffset({ x: 0, y: 0 })
  }

  useEffect(() => {
    if (!previewImage) return
    const isImagePreview = previewImage.contentType.startsWith('image/')
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeImagePreview()
      if (!isImagePreview) return
      if (event.key === '+' || event.key === '=') changePreviewZoom(previewZoom + 0.25)
      if (event.key === '-') changePreviewZoom(previewZoom - 0.25)
      if (event.key === '0') resetImagePreview()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [previewImage, previewZoom])

  useEffect(() => {
    const controller = new AbortController()

    Promise.all([
      fetch('/api/health', { signal: controller.signal }).then((response) => {
        if (!response.ok) throw new Error('API health check failed')
        return response.json()
      }),
      fetch('/api/agent/status', { signal: controller.signal }).then(
        (response) => {
          if (!response.ok) throw new Error('Agent status check failed')
          return response.json() as Promise<AgentStatus>
        },
      ),
    ])
      .then(([, status]) => {
        setApiState('online')
        setAgentStatus(status)
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setApiState('offline')
      })

    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const localProjects = loadProjects()

    const loadPersistedProjects = async () => {
      try {
        const persistedProjects = await listProjects(controller.signal)

        if (localProjects.length > 0) {
          await Promise.all(localProjects.map((project) => upsertProject(project, controller.signal)))
          localStorage.removeItem(projectStorageKey)
        }

        setProjects(localProjects.length > 0
          ? await listProjects(controller.signal)
          : persistedProjects)
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setProjects(localProjects)
        console.warn('后端项目加载失败，暂时使用浏览器本地项目', error)
      } finally {
        if (!controller.signal.aborted) setProjectsLoading(false)
      }
    }

    void loadPersistedProjects()
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (projectsLoading || !selectedProject) return
    const timeoutId = window.setTimeout(() => {
      void upsertProject(selectedProject).catch((error) => {
        console.warn('项目设置保存失败', error)
      })
    }, 400)
    return () => window.clearTimeout(timeoutId)
  }, [projectsLoading, selectedProject])

  useEffect(() => {
    if (!selectedProject) return

    setSelectingCustomImageModel(false)
    setSelectingCustomVideoModel(false)
    setRuntimeConfigurationState('loading')
    setFoundryConfigurationState('loading')

    const controller = new AbortController()
    setAssets([])
    setAssetsLoading(true)
    setAssetError(null)

    listProjectAssets(selectedProject.id, controller.signal)
      .then((loadedAssets) => {
        setAssets(loadedAssets)
        if (initializedAssetSectionProjectRef.current !== selectedProject.id) {
          setActiveAssetSection(loadedAssets.some((asset) => asset.type === 'shot')
            ? 'shots'
            : 'scripts')
          initializedAssetSectionProjectRef.current = selectedProject.id
        }
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setAssetError(error instanceof Error ? error.message : localize(language, '资产列表加载失败', 'Failed to load assets'))
      })
      .finally(() => {
        if (!controller.signal.aborted) setAssetsLoading(false)
      })

    getRuntimeConfiguration(selectedProject.id, controller.signal)
      .then((configuration) => {
        setRuntimeConfiguration(configuration)
        setRuntimeConfigurationState('idle')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setRuntimeConfigurationState('error')
      })

    getFoundryConfiguration(controller.signal)
      .then((configuration) => {
        setFoundryConfiguration(configuration)
        setOpenAiApiKey('')
        setImageApiKey('')
        setSpeechApiKey('')
        setFoundryConfigurationState('idle')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFoundryConfigurationState('error')
      })

    return () => controller.abort()
  }, [selectedProject, language])

  useEffect(() => {
    if (selectedProject) setSelectedModel(selectedProject.languageModel)
  }, [selectedProject])

  useEffect(() => {
    if (!selectedProject) return

    const controller = new AbortController()
    let refreshing = false
    const refreshAssets = async () => {
      if (refreshing || document.visibilityState !== 'visible') return
      refreshing = true
      try {
        setAssets(await listProjectAssets(selectedProject.id, controller.signal))
      } catch (error) {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          console.warn('资产列表自动同步失败', error)
        }
      } finally {
        refreshing = false
      }
    }
    const handleVisibilityChange = () => void refreshAssets()
    const intervalId = window.setInterval(() => void refreshAssets(), 10_000)
    window.addEventListener('focus', handleVisibilityChange)
    document.addEventListener('visibilitychange', handleVisibilityChange)

    return () => {
      controller.abort()
      window.clearInterval(intervalId)
      window.removeEventListener('focus', handleVisibilityChange)
      document.removeEventListener('visibilitychange', handleVisibilityChange)
    }
  }, [selectedProject])

  useEffect(() => {
    if (!selectedProject || !selectedAsset || assetsLoading) return

    const latestAsset = assets.find((asset) => asset.resourceId === selectedAsset.resourceId)
    if (!latestAsset) {
      setSelectedAsset(null)
      setAssetVersions([])
      setAssetPreviewText(null)
      setAssetPreviewError(null)
      setShotAssetLinks([])
      return
    }
    if (latestAsset.id === selectedAsset.id) return

    setSelectedAsset(latestAsset)
    setAssetVersions([latestAsset])
    void listAssetVersions(selectedProject.id, latestAsset.id)
      .then(setAssetVersions)
      .catch((error: unknown) => {
        setAssetPreviewError(error instanceof Error ? error.message : localize(language, '资源版本加载失败', 'Failed to load asset versions'))
      })
  }, [assets, assetsLoading, language, selectedAsset, selectedProject])

  useEffect(() => {
    const resourceIds = new Set(assets.map((asset) => asset.resourceId))
    setShotAssetLinks((current) => current.filter((link) =>
      resourceIds.has(link.asset.resourceId)))
    if (previewImage && !resourceIds.has(previewImage.resourceId)) setPreviewImage(null)
  }, [assets, previewImage])

  useLayoutEffect(() => {
    if (messagesLoading || messages.length === 0) return
    if (!scrollToLatestAfterLoadRef.current && !stickToLatestRef.current) return

    const conversation = conversationRef.current
    if (!conversation) return

    conversation.scrollTop = conversation.scrollHeight
    scrollToLatestAfterLoadRef.current = false
    stickToLatestRef.current = true
  }, [messages, messagesLoading])

  useEffect(() => {
    if (!selectedProject) return

    const controller = new AbortController()
    setSkillsLoading(true)
    setSkillError(null)
    listSkills(controller.signal)
      .then((loadedSkills) => {
        setSkills(loadedSkills)
        setSelectedSkill((current) => current
          ? loadedSkills.find((skill) => skill.id === current.id) ?? null
          : null)
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setSkillError(error instanceof Error ? error.message : localize(language, '技能加载失败', 'Failed to load skills'))
      })
      .finally(() => {
        if (!controller.signal.aborted) setSkillsLoading(false)
      })

    return () => controller.abort()
  }, [selectedProject, language])

  async function createProject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const name = newProjectName.trim()
    if (!name) return

    const project: Project = {
      id: crypto.randomUUID(),
      name,
      createdAt: new Date().toISOString(),
      ...defaultProjectSettings,
    }
    try {
      const savedProject = await upsertProject(project)
      setProjects((current) => [savedProject, ...current])
      setNewProjectName('')
      navigate(`/projects/${savedProject.id}`)
    } catch (error) {
      console.warn('项目创建失败', error)
    }
  }

  function updateProjectFormat(formatPreset: string, outputWidth: number, outputHeight: number) {
    if (!selectedProject) return
    const compatiblePreviewResolutions = getPreviewResolutionOptions(
      formatPreset,
      outputWidth,
      outputHeight,
    )
    updateProject({
      formatPreset,
      outputWidth,
      outputHeight,
      previewResolution: compatiblePreviewResolutions.includes(selectedProject.previewResolution)
        ? selectedProject.previewResolution
        : compatiblePreviewResolutions[0],
    })
  }

  function updateProject(changes: Partial<Project>) {
    if (!selectedProject) return
    setProjects((current) => current.map((project) => project.id === selectedProject.id
      ? { ...project, ...changes }
      : project))
  }

  async function saveRuntimeConfiguration() {
    if (!selectedProject) return
    setRuntimeConfigurationState('saving')
    try {
      setRuntimeConfiguration(await updateRuntimeConfiguration(
        selectedProject.id,
        runtimeConfiguration,
      ))
      setRuntimeConfigurationState('saved')
    } catch {
      setRuntimeConfigurationState('error')
    }
  }

  async function saveFoundryConfiguration() {
    setFoundryConfigurationState('saving')
    try {
      setFoundryConfiguration(await updateFoundryConfiguration({
        ...foundryConfiguration,
        openAiApiKey: openAiApiKey || null,
        imageApiKey: imageApiKey || null,
        speechApiKey: speechApiKey || null,
        clearOpenAiApiKey: false,
        clearImageApiKey: false,
        clearSpeechApiKey: false,
      }))
      setOpenAiApiKey('')
      setImageApiKey('')
      setSpeechApiKey('')
      setFoundryConfigurationState('saved')
      const statusResponse = await fetch('/api/agent/status')
      if (statusResponse.ok) setAgentStatus(await statusResponse.json() as AgentStatus)
    } catch {
      setFoundryConfigurationState('error')
    }
  }

  async function submitDirectorOrder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const messageText = directorOrder.trim()
    if (!messageText && attachments.length === 0) return
    if (!selectedProject || !agentStatus?.configured) return

    const attachmentContext = attachments.length > 0
      ? text(`附件：${attachments.map((attachment) => attachment.name).join('、')}`, `Attachments: ${attachments.map((attachment) => attachment.name).join(', ')}`)
      : ''
    const message = [messageText, attachmentContext].filter(Boolean).join('\n\n')

    setDirectorOrder('')
    setAttachments([])
    await sendDirectorMessage(message, selectedModel, selectedAsset?.id)
  }

  function prepareDirectorMessageScroll() {
    scrollToLatestAfterLoadRef.current = true
    stickToLatestRef.current = true
  }

  function handleDirectorAssetGenerated(asset: AssetRecord) {
    setAssets((current) => [
      asset,
      ...current.filter((item) => item.resourceId !== asset.resourceId),
    ])
  }

  function handleDirectorMessageCompleted(
    streamEvent: MessageStreamEvent,
    sourceShot: AssetRecord | null,
  ) {
    if (!selectedProject) return

    if (streamEvent.skillRun) {
      setSelectedSkillRun(streamEvent.skillRun)
      setMobilePanel('review')
    }
    if (streamEvent.generatedAssets && streamEvent.generatedAssets.length > 0) {
      const firstAsset = streamEvent.updatedAsset ?? streamEvent.generatedAssets[0]
      const targetSection = assetSections.find((section) => section.type === firstAsset.type
        && (!section.contentTypePrefix
          || firstAsset.contentType.startsWith(section.contentTypePrefix))
        && (!section.matches || section.matches(firstAsset)))
      setAssets((current) => [
        ...streamEvent.generatedAssets!,
        ...current.filter((asset) =>
          !streamEvent.generatedAssets!.some((generated) => generated.resourceId === asset.resourceId)),
      ])
      if (sourceShot) {
        void reviewAsset(sourceShot)
      } else {
        navigate(`/projects/${selectedProject.id}`)
        if (targetSection) {
          setActiveAssetSection(targetSection.id)
          setExpandedAssetSections((current) => new Set(current).add(targetSection.id))
        }
        void reviewAsset(firstAsset)
      }
    } else if (streamEvent.updatedAsset) {
      setAssets((current) => [
        streamEvent.updatedAsset!,
        ...current.filter((asset) =>
          asset.resourceId !== streamEvent.updatedAsset!.resourceId),
      ])
      setSelectedAsset(streamEvent.updatedAsset)
      void reviewAsset(streamEvent.updatedAsset)
    } else if (streamEvent.outputAsset) {
      setAssets((current) => [
        streamEvent.outputAsset!,
        ...current.filter((asset) => asset.resourceId !== streamEvent.outputAsset!.resourceId),
      ])
      navigate(`/projects/${selectedProject.id}`)
      setActiveAssetSection('analyses')
      setExpandedAssetSections((current) => new Set(current).add('analyses'))
    } else if (sourceShot) {
      void reviewAsset(sourceShot)
    }
  }

  async function uploadAssets(
    event: ChangeEvent<HTMLInputElement>,
    section: (typeof assetSections)[number],
  ) {
    const files = Array.from(event.target.files ?? [])
    event.target.value = ''
    if (!selectedProject || files.length === 0) return

    setUploadingAssets(true)
    setAssetError(null)

    try {
      const uploadedAssets = await Promise.all(
        files.map((file) => uploadProjectAsset(selectedProject.id, section.type, file)),
      )

      setAssets((current) => [...uploadedAssets.reverse(), ...current])
    } catch (error) {
      setAssetError(error instanceof Error ? error.message : text('资产上传失败', 'Asset upload failed'))
    } finally {
      setUploadingAssets(false)
    }
  }

  async function deleteAsset(asset: AssetRecord) {
    if (!selectedProject || deletingAssetId) return
    const confirmation = asset.versionCount > 1
      ? text(`永久删除“${asset.name}”及其全部 ${asset.versionCount} 个版本？此操作不可撤销。`, `Permanently delete “${asset.name}” and all ${asset.versionCount} versions? This cannot be undone.`)
      : text(`永久删除“${asset.name}”？此操作不可撤销。`, `Permanently delete “${asset.name}”? This cannot be undone.`)
    if (!window.confirm(confirmation)) return

    setDeletingAssetId(asset.id)
    setAssetError(null)
    try {
      await deleteProjectAsset(selectedProject.id, asset.id)
      setAssets((current) => current.filter((item) => item.resourceId !== asset.resourceId))
      setShotAssetLinks((current) => current.filter((link) =>
        link.asset.resourceId !== asset.resourceId))
      if (selectedAsset?.resourceId === asset.resourceId) {
        setSelectedAsset(null)
        setAssetVersions([])
        setAssetPreviewText(null)
        setAssetPreviewError(null)
        setShotAssetLinks([])
      }
      if (previewImage?.resourceId === asset.resourceId) setPreviewImage(null)
    } catch (error) {
      setAssetError(error instanceof Error ? error.message : text('资产删除失败', 'Failed to delete asset'))
    } finally {
      setDeletingAssetId(null)
    }
  }

  async function refreshProjectAssets() {
    if (!selectedProject || refreshingAssets) return

    setRefreshingAssets(true)
    setAssetError(null)
    try {
      const refreshedAssets = await listProjectAssets(selectedProject.id)
      setAssets(refreshedAssets)
      setSelectedAsset((current) => current
        ? refreshedAssets.find((asset) => asset.id === current.id) ?? current
        : null)
    } catch (error) {
      setAssetError(error instanceof Error ? error.message : text('资产列表刷新失败', 'Failed to refresh assets'))
    } finally {
      setRefreshingAssets(false)
    }
  }

  async function reviewAsset(asset: AssetRecord) {
    if (!selectedProject) return
    const section = assetSections.find((item) => item.type === asset.type
      && (!item.contentTypePrefix || asset.contentType.startsWith(item.contentTypePrefix))
      && (!item.matches || item.matches(asset)))
    if (section) {
      setActiveAssetSection(section.id)
      setExpandedAssetSections((current) => new Set(current).add(section.id))
    }
    setSelectedAsset(asset)
    setAssetVersions([asset])
    setSelectedSkill(null)
    setSelectedSkillRun(null)
    setAssetPreviewText(null)
    setAssetPreviewError(null)
    setShotAssetLinks([])
    setMobilePanel('review')

    void listAssetVersions(selectedProject.id, asset.id)
      .then(setAssetVersions)
      .catch((error: unknown) => {
        setAssetPreviewError(error instanceof Error ? error.message : text('资源版本加载失败', 'Failed to load asset versions'))
      })

    if (asset.type === 'shot') {
      void listShotAssetLinks(selectedProject.id, asset.id)
        .then(setShotAssetLinks)
        .catch((error: unknown) => {
          setAssetPreviewError(error instanceof Error ? error.message : text('镜头素材加载失败', 'Failed to load shot assets'))
        })
    }

    const isTextAsset = asset.type === 'script'
      || asset.type === 'analysis'
      || asset.contentType.startsWith('text/')
    if (!isTextAsset) return

    setAssetPreviewLoading(true)
    try {
      const response = await fetch(asset.contentUrl)
      if (!response.ok) throw new Error(text('资产内容加载失败', 'Failed to load asset content'))
      const contentText = await response.text()
      if (asset.contentType === 'application/json') {
        try {
          setAssetPreviewText(JSON.stringify(JSON.parse(contentText), null, 2))
        } catch {
          setAssetPreviewText(contentText)
        }
      } else {
        setAssetPreviewText(contentText)
      }
    } catch (error) {
      setAssetPreviewError(error instanceof Error ? error.message : text('资产内容加载失败', 'Failed to load asset content'))
    } finally {
      setAssetPreviewLoading(false)
    }
  }

  async function updateSkill(skill: SkillDefinitionRecord, isEnabled: boolean) {
    setSkillError(null)
    try {
      const updated = await setSkillEnabled(skill.id, isEnabled)
      setSkills((current) => current.map((item) => item.id === updated.id ? updated : item))
      setSelectedSkill((current) => current?.id === updated.id ? updated : current)
    } catch (error) {
      setSkillError(error instanceof Error ? error.message : text('技能状态更新失败', 'Failed to update skill status'))
    }
  }

  function parseSkillAnalysis(run: SkillRunRecord | null) {
    if (!run?.resultJson) return null
    try {
      const value = JSON.parse(run.resultJson) as Partial<ScriptAnalysisRecord>
      if (!Array.isArray(value.characters)
        || !Array.isArray(value.locations)
        || !Array.isArray(value.props)
        || !Array.isArray(value.scenes)
        || !Array.isArray(value.ambiguities)) return null
      return value as ScriptAnalysisRecord
    } catch {
      return null
    }
  }

  const selectedAnalysis = parseSkillAnalysis(selectedSkillRun)
  const projectSettingsPanel = selectedProject ? (
    <section className="assets-panel project-settings-panel" aria-labelledby="project-settings-title">
      <header className="assets-header">
        <div>
          <p className="section-label">PROJECT</p>
          <h2 id="project-settings-title">{text('项目信息', 'Project')}</h2>
        </div>
        <button className="switch-project" type="button" onClick={() => navigate('/')}>
          {text('切换', 'Switch')}
        </button>
      </header>
      <div className="project-settings-form">
        <section className="project-settings-group">
          <h3>{text('基本信息', 'Basics')}</h3>
          <label htmlFor="project-settings-name">{text('项目名称', 'Project name')}</label>
          <input
            id="project-settings-name"
            value={selectedProject.name}
            maxLength={100}
            onChange={(event) => updateProject({ name: event.target.value })}
          />
          <label htmlFor="project-settings-description">{text('项目描述', 'Description')}</label>
          <textarea
            id="project-settings-description"
            value={selectedProject.description}
            maxLength={1000}
            rows={4}
            placeholder={text('故事类型、制作目标或项目范围', 'Story type, production goal, or project scope')}
            onChange={(event) => updateProject({ description: event.target.value })}
          />
        </section>

        <section className="project-settings-group">
          <h3>{text('画面与交付', 'Picture & delivery')}</h3>
          <label htmlFor="project-settings-format">{text('成片画面比例（分辨率）', 'Final aspect ratio (resolution)')}</label>
          <select
            id="project-settings-format"
            value={selectedProject.formatPreset}
            onChange={(event) => {
              const preset = projectFormatPresets.find((item) => item.id === event.target.value)
                ?? projectFormatPresets[0]
              updateProjectFormat(
                preset.id,
                preset.id === 'custom' ? selectedProject.outputWidth : preset.width,
                preset.id === 'custom' ? selectedProject.outputHeight : preset.height,
              )
            }}
          >
            {projectFormatPresets.map((preset) => (
              <option value={preset.id} key={preset.id}>{preset.id === 'custom' ? text('自定义', 'Custom') : preset.label}</option>
            ))}
          </select>
          {selectedProject.formatPreset === 'custom' && (
            <div className="project-resolution-inputs">
              <input
                type="number"
                min="64"
                max="8192"
                step="2"
                value={selectedProject.outputWidth}
                aria-label={text('画面宽度', 'Frame width')}
                onChange={(event) => updateProjectFormat(
                  'custom',
                  Math.min(8192, Math.max(64, Number(event.target.value))),
                  selectedProject.outputHeight,
                )}
              />
              <span aria-hidden="true">×</span>
              <input
                type="number"
                min="64"
                max="8192"
                step="2"
                value={selectedProject.outputHeight}
                aria-label={text('画面高度', 'Frame height')}
                onChange={(event) => updateProjectFormat(
                  'custom',
                  selectedProject.outputWidth,
                  Math.min(8192, Math.max(64, Number(event.target.value))),
                )}
              />
            </div>
          )}
          <label htmlFor="project-settings-preview-resolution">{text('快速拉片分辨率', 'Preview resolution')}</label>
          <select
            id="project-settings-preview-resolution"
            value={previewResolutionOptions.includes(selectedProject.previewResolution)
              ? selectedProject.previewResolution
              : previewResolutionOptions[0]}
            onChange={(event) => updateProject({ previewResolution: event.target.value })}
          >
            {previewResolutionOptions.map((resolution) => (
              <option value={resolution} key={resolution}>{resolution.replace('x', ' × ')}</option>
            ))}
          </select>
        </section>

        <section className="project-settings-group">
          <h3>{text('生成模型', 'Generation models')}</h3>
          <label htmlFor="project-settings-language-model">{text('语言模型', 'Language model')}</label>
          <select
            id="project-settings-language-model"
            value={selectedProject.languageModel}
            onChange={(event) => {
              updateProject({ languageModel: event.target.value })
              setSelectedModel(event.target.value)
            }}
          >
            <option value="gpt-5.4">GPT-5.4</option>
            <option value="gpt-5.4-mini">GPT-5.4 mini</option>
            <option value="gpt-4.1">GPT-4.1</option>
          </select>
          <label htmlFor="project-settings-image-model">{text('Image 模型', 'Image model')}</label>
          <select
            id="project-settings-image-model"
            value={!selectingCustomImageModel
              && selectedProject.imageModel === (agentStatus?.imageDeployment ?? 'gpt-image-2')
              ? 'configured'
              : 'custom'}
            onChange={(event) => {
              const isCustom = event.target.value === 'custom'
              setSelectingCustomImageModel(isCustom)
              if (!isCustom) {
                updateProject({ imageModel: agentStatus?.imageDeployment ?? 'gpt-image-2' })
              }
            }}
          >
            <option value="configured">
              {text('系统配置', 'System configuration')} · {agentStatus?.imageDeployment ?? 'gpt-image-2'}
            </option>
            <option value="custom">{text('自定义 Azure 部署', 'Custom Azure deployment')}</option>
          </select>
          {(selectingCustomImageModel
            || selectedProject.imageModel !== (agentStatus?.imageDeployment ?? 'gpt-image-2')) && (
            <>
              <label htmlFor="project-settings-custom-image-model">{text('图片部署名称', 'Image deployment name')}</label>
              <input
                id="project-settings-custom-image-model"
                value={selectedProject.imageModel}
                maxLength={100}
                placeholder={text('Azure 部署名称', 'Azure deployment name')}
                onChange={(event) => updateProject({ imageModel: event.target.value })}
              />
            </>
          )}
          <div className="project-setting-readout">
            <span>{text('模型生成尺寸', 'Generation size')}</span>
            <strong>{projectFormat?.imageSize.replace('x', ' × ')}</strong>
          </div>

          <label htmlFor="project-settings-video-model">{text('视频模型', 'Video model')}</label>
          <select
            id="project-settings-video-model"
            value={selectingCustomVideoModel || selectedProject.videoModel ? 'custom' : 'none'}
            onChange={(event) => {
              const isCustom = event.target.value === 'custom'
              setSelectingCustomVideoModel(isCustom)
              if (!isCustom) updateProject({ videoModel: '' })
            }}
          >
            <option value="none">{text('未配置', 'Not configured')}</option>
            <option value="custom">{text('自定义 Azure 部署', 'Custom Azure deployment')}</option>
          </select>
          {(selectingCustomVideoModel || selectedProject.videoModel) && (
            <>
              <label htmlFor="project-settings-custom-video-model">{text('视频部署名称', 'Video deployment name')}</label>
              <input
                id="project-settings-custom-video-model"
                value={selectedProject.videoModel}
                maxLength={100}
                placeholder={text('Azure 部署名称', 'Azure deployment name')}
                onChange={(event) => updateProject({ videoModel: event.target.value })}
              />
            </>
          )}
          <div className="project-setting-readout">
            <span>{text('视频生成', 'Video generation')}</span>
            <strong>{selectedProject.videoModel.trim() ? text('使用项目部署', 'Using project deployment') : text('未配置', 'Not configured')}</strong>
          </div>
        </section>

      </div>
    </section>
  ) : null

  if (projectsLoading) {
    return (
      <main className="project-gate">
        <div className="no-projects">
          <p>{text('正在加载项目...', 'Loading projects...')}</p>
        </div>
      </main>
    )
  }

  if (!selectedProject) {
    if (location.pathname !== '/') {
      return <Navigate to="/" replace />
    }

    return (
      <main className="project-gate">
        <header className="gate-brand">
          <span className="brand-mark" aria-hidden="true">A</span>
          <div>
            <strong>{text('alex 导演台', 'alex Director Console')}</strong>
            <span>PROJECT ENTRY</span>
          </div>
          <LanguageSwitch language={language} onChange={setLanguage} />
        </header>

        <div className="gate-layout">
          <section className="project-picker" aria-labelledby="project-picker-title">
            <p className="section-label">DIRECTOR'S PROJECTS</p>
            <h1 id="project-picker-title">{text('先选择一个项目', 'Choose a project')}</h1>
            <p className="gate-intro">{text('导演台中的指令、素材与审阅都归属于具体项目。', 'Commands, assets, and reviews belong to a specific project.')}</p>

            {projects.length > 0 ? (
              <div className="project-list">
                {projects.map((project) => (
                  <button
                    className="project-row"
                    type="button"
                    key={project.id}
                    onClick={() => navigate(`/projects/${project.id}`)}
                  >
                    <span className="project-initial" aria-hidden="true">
                      {project.name.slice(0, 1).toUpperCase()}
                    </span>
                    <span className="project-copy">
                      <strong>{project.name}</strong>
                      <small>{text('创建于', 'Created')} {new Date(project.createdAt).toLocaleDateString(language)}</small>
                    </span>
                    <span className="enter-project" aria-hidden="true">{text('进入 →', 'Open →')}</span>
                  </button>
                ))}
              </div>
            ) : (
              <div className="no-projects">
                <span>0</span>
                <p>{text('还没有项目，从右侧创建第一个。', 'No projects yet. Create the first one on the right.')}</p>
              </div>
            )}
          </section>

          <section className="create-project" aria-labelledby="create-project-title">
            <p className="section-label">NEW PROJECT</p>
            <h2 id="create-project-title">{text('创建项目', 'Create project')}</h2>
            <form onSubmit={createProject}>
              <label htmlFor="project-name">{text('项目名称', 'Project name')}</label>
              <input
                id="project-name"
                value={newProjectName}
                onChange={(event) => setNewProjectName(event.target.value)}
                placeholder={text('例如：天桥食堂', 'For example: Product Film')}
                autoComplete="off"
                autoFocus={projects.length === 0}
              />
              <button type="submit" disabled={!newProjectName.trim()}>
                {text('创建并进入', 'Create and open')}
              </button>
            </form>
          </section>
        </div>
      </main>
    )
  }

  return (
    <div className={`app-shell mobile-panel-${mobilePanel}`}>
      <aside className="monitor">
        <header className="brand">
          <span className="brand-mark" aria-hidden="true">A</span>
          <div>
            <strong>{text('alex 导演台', 'alex Director')}</strong>
          </div>
          <LanguageSwitch language={language} onChange={setLanguage} />
        </header>
        <div className="sidebar-middle">
          <nav className="icon-menu" aria-label={text('制作资源与技能', 'Production assets and skills')}>
            <button
              type="button"
              className={sidebarMode === 'settings' ? 'active' : undefined}
              aria-label={text('项目信息', 'Project information')}
              aria-pressed={sidebarMode === 'settings'}
              title={text('项目信息', 'Project information')}
              onClick={() => navigate(`/projects/${selectedProject.id}/settings`)}
            >
              <Settings2 size={19} strokeWidth={1.7} aria-hidden="true" />
            </button>
            {assetSections
              .filter((section) => section.id === 'scripts')
              .map((section) => {
                const Icon = section.icon
                const isActive = sidebarMode === 'assets' && selectedAssetSection.id === section.id
                return (
                  <button
                    type="button"
                    className={isActive ? 'active' : undefined}
                    key={section.id}
                    aria-label={localize(language, section.label, section.labelEn)}
                    aria-pressed={isActive}
                    title={localize(language, section.label, section.labelEn)}
                    onClick={() => {
                      setActiveAssetSection(section.id)
                      navigate(`/projects/${selectedProject.id}`)
                    }}
                  >
                    <Icon size={19} strokeWidth={1.7} aria-hidden="true" />
                  </button>
                )
              })}
            <button
              type="button"
              className={sidebarMode === 'assets' && isGroupedAssetSection ? 'active' : undefined}
              aria-label={text('资产', 'Assets')}
              aria-pressed={sidebarMode === 'assets' && isGroupedAssetSection}
              title={text('资产', 'Assets')}
              onClick={() => {
                if (!isGroupedAssetSection) setActiveAssetSection('analyses')
                setExpandedAssetSections((current) => new Set(current).add('analyses'))
                navigate(`/projects/${selectedProject.id}`)
              }}
            >
              <Box size={19} strokeWidth={1.7} aria-hidden="true" />
            </button>
            {assetSections
              .filter((section) => ['images', 'videos', 'audio'].includes(section.id))
              .map((section) => {
                const Icon = section.icon
                const isActive = sidebarMode === 'assets' && selectedAssetSection.id === section.id
                return (
                  <button
                    type="button"
                    className={isActive ? 'active' : undefined}
                    key={section.id}
                    aria-label={localize(language, section.label, section.labelEn)}
                    aria-pressed={isActive}
                    title={localize(language, section.label, section.labelEn)}
                    onClick={() => {
                      setActiveAssetSection(section.id)
                      navigate(`/projects/${selectedProject.id}`)
                    }}
                  >
                    <Icon size={19} strokeWidth={1.7} aria-hidden="true" />
                  </button>
                )
              })}
            {assetSections
              .filter((section) => section.id === 'shots')
              .map((section) => {
                const Icon = section.icon
                const isActive = sidebarMode === 'assets' && selectedAssetSection.id === section.id
                return (
                  <button
                    type="button"
                    className={isActive ? 'active' : undefined}
                    key={section.id}
                    aria-label={localize(language, section.label, section.labelEn)}
                    aria-pressed={isActive}
                    title={localize(language, section.label, section.labelEn)}
                    onClick={() => {
                      setActiveAssetSection(section.id)
                      navigate(`/projects/${selectedProject.id}`)
                    }}
                  >
                    <Icon size={19} strokeWidth={1.7} aria-hidden="true" />
                  </button>
                )
              })}
            {assetSections
              .filter((section) => section.id === 'final-videos')
              .map((section) => {
                const Icon = section.icon
                const isActive = sidebarMode === 'assets' && selectedAssetSection.id === section.id
                return (
                  <button
                    type="button"
                    className={isActive ? 'active' : undefined}
                    key={section.id}
                    aria-label={localize(language, section.label, section.labelEn)}
                    aria-pressed={isActive}
                    title={localize(language, section.label, section.labelEn)}
                    onClick={() => {
                      setActiveAssetSection(section.id)
                      navigate(`/projects/${selectedProject.id}`)
                    }}
                  >
                    <Icon size={19} strokeWidth={1.7} aria-hidden="true" />
                  </button>
                )
              })}
            <button
              type="button"
              className={sidebarMode === 'configuration' ? 'active skill-menu-button' : 'skill-menu-button'}
              aria-label={text('系统配置', 'System configuration')}
              aria-pressed={sidebarMode === 'configuration'}
              title={text('系统配置', 'System configuration')}
              onClick={() => navigate(`/projects/${selectedProject.id}/configuration`)}
            >
              <WandSparkles size={19} strokeWidth={1.7} aria-hidden="true" />
            </button>
          </nav>

          {sidebarMode === 'settings' ? projectSettingsPanel : sidebarMode === 'assets' ? (
          <AssetPanel
            language={language}
            projectName={selectedProject.name}
            selectedSection={selectedAssetSection}
            groupedSections={groupedAssetSections}
            isGroupedSection={isGroupedAssetSection}
            groupedAssetCount={groupedAssetCount}
            visibleAssetCount={visibleAssets.length}
            assets={assets}
            selectedAsset={selectedAsset}
            activeSectionId={activeAssetSection}
            expandedSectionIds={expandedAssetSections}
            assetsLoading={assetsLoading}
            refreshingAssets={refreshingAssets}
            uploadingAssets={uploadingAssets}
            deletingAssetId={deletingAssetId}
            assetError={assetError}
            onSwitchProject={() => navigate('/')}
            onRefresh={() => void refreshProjectAssets()}
            onUpload={uploadAssets}
            onReviewAsset={reviewAsset}
            onDeleteAsset={deleteAsset}
            onToggleSection={(sectionId) => {
              setActiveAssetSection(sectionId)
              setExpandedAssetSections((current) => {
                const next = new Set(current)
                if (next.has(sectionId)) next.delete(sectionId)
                else next.add(sectionId)
                return next
              })
            }}
          />
          ) : (
          <SystemConfigurationPanel
            language={language}
            agentStatus={agentStatus}
            foundryConfiguration={foundryConfiguration}
            setFoundryConfiguration={setFoundryConfiguration}
            foundryConfigurationState={foundryConfigurationState}
            openAiApiKey={openAiApiKey}
            setOpenAiApiKey={setOpenAiApiKey}
            imageApiKey={imageApiKey}
            setImageApiKey={setImageApiKey}
            speechApiKey={speechApiKey}
            setSpeechApiKey={setSpeechApiKey}
            saveFoundryConfiguration={saveFoundryConfiguration}
            runtimeConfiguration={runtimeConfiguration}
            setRuntimeConfiguration={setRuntimeConfiguration}
            runtimeConfigurationState={runtimeConfigurationState}
            saveRuntimeConfiguration={saveRuntimeConfiguration}
            skills={skills}
            skillsLoading={skillsLoading}
            skillError={skillError}
            selectedSkill={selectedSkill}
            onSelectSkill={(skill) => {
              setSelectedSkill(skill)
              setSelectedSkillRun(null)
              setMobilePanel('review')
            }}
            updateSkill={updateSkill}
          />
          )}
        </div>
        <footer className={`service-status ${serviceStatusExpanded ? 'expanded' : ''}`}>
          <button
            type="button"
            className="service-status-toggle"
            aria-expanded={serviceStatusExpanded}
            onClick={() => setServiceStatusExpanded((expanded) => !expanded)}
          >
            <span>
              <span className={`status-dot ${apiState}`} />
              {text('服务状态', 'Services')}
            </span>
            {serviceStatusExpanded
              ? <ChevronDown size={15} aria-hidden="true" />
              : <ChevronUp size={15} aria-hidden="true" />}
          </button>
          {serviceStatusExpanded && (
            <div className="service-status-details">
              <div>
                <span className={`status-dot ${apiState}`} />
                API {apiState === 'online' ? text('在线', 'Online') : apiState === 'offline' ? text('离线', 'Offline') : text('检查中', 'Checking')}
              </div>
              <div>
                <span className={`status-dot ${agentStatus?.configured ? 'online' : 'idle'}`} />
                MAF {agentStatus?.configured ? text('已配置', 'Configured') : text('待配置', 'Pending')}
              </div>
              <div>
                <span className={`status-dot ${agentStatus?.imageConfigured ? 'online' : 'idle'}`} />
                Image {agentStatus?.imageConfigured
                  ? `${agentStatus.imageDeployment} · ${agentStatus.imageQuality}`
                  : text('待配置', 'Pending')}
              </div>
            </div>
          )}
        </footer>
      </aside>

      <main className="director-desk">
        <header className="desk-header">
          <div>
            <h1>{text('导演台', 'Director Console')}</h1>
          </div>
          <LanguageSwitch language={language} onChange={setLanguage} />
          <span className="environment-badge">{text('本地开发', 'Local')}</span>
        </header>
        <section
          ref={conversationRef}
          className={`conversation ${messages.length > 0 ? 'has-messages' : ''}`}
          aria-label={text('导演工作区', 'Director workspace')}
          onScroll={(event) => {
            const conversation = event.currentTarget
            stickToLatestRef.current = conversation.scrollHeight
              - conversation.scrollTop
              - conversation.clientHeight < 80
          }}
        >
          {messagesLoading ? (
            <div className="empty-conversation">
              <LoaderCircle className="spin" size={24} aria-hidden="true" />
              <p>{text('正在加载对话…', 'Loading conversation…')}</p>
            </div>
          ) : messages.length > 0 ? (
            <div className="conversation-thread">
              {messages.map((message) => (
                <article className={`chat-message ${message.role}`} key={message.id}>
                  <header>
                    <strong>{message.role === 'user' ? text('导演', 'Director') : text('执行副导演', 'Assistant Director')}</strong>
                    <span className="chat-message-meta">
                      <span>{message.isStreaming ? text('执行中', 'Running') : message.model}</span>
                      {message.role === 'user' && !message.id.startsWith('pending-') && (
                        <button
                          className="chat-retry-button"
                          type="button"
                          disabled={sendingMessage}
                          onClick={() => void retryDirectorMessage(message)}
                          title={text('从这里重试', 'Retry from here')}
                          aria-label={text('删除此条及后续对话并重试', 'Delete this and later messages, then retry')}
                        >
                          <RotateCcw size={13} aria-hidden="true" />
                        </button>
                      )}
                    </span>
                  </header>
                  {message.processEvents && message.processEvents.length > 0 && (
                    <ol className="process-trace" aria-label={text('执行过程', 'Execution trace')}>
                      {message.processEvents.map((processEvent, index) => (
                        <li className={processEvent.stage === 'error' ? 'error' : undefined} key={`${processEvent.stage}-${index}`}>
                          <span className="process-trace-dot" aria-hidden="true" />
                          <span>
                            {processEvent.message}
                            {processEvent.data?.imagePrompt && (
                              <span className="process-image-prompt">
                                <strong>{text('实际提示词', 'Actual prompt')}</strong>
                                <pre>{processEvent.data.imagePrompt}</pre>
                              </span>
                            )}
                            {processEvent.data?.videoPrompt && (
                              <span className="process-image-prompt">
                                <strong>{text('实际视频提示词', 'Actual video prompt')}</strong>
                                {processEvent.data.workflowFileName && (
                                  <small>
                                    {processEvent.data.workflowFileName}
                                    {processEvent.data.width && processEvent.data.height
                                      ? ` · ${processEvent.data.width}×${processEvent.data.height}`
                                      : ''}
                                    {processEvent.data.frameCount
                                      ? text(` · ${processEvent.data.frameCount} 帧`, ` · ${processEvent.data.frameCount} frames`)
                                      : ''}
                                    {processEvent.data.fps ? ` · ${processEvent.data.fps} FPS` : ''}
                                  </small>
                                )}
                                <pre>{processEvent.data.videoPrompt}</pre>
                              </span>
                            )}
                          </span>
                        </li>
                      ))}
                      {message.isStreaming && (
                        <li className="active">
                          <LoaderCircle className="spin" size={12} aria-hidden="true" />
                          <span>{text('等待下一步回执', 'Waiting for the next update')}</span>
                        </li>
                      )}
                    </ol>
                  )}
                  {message.content ? <p>{message.content}</p> : message.isStreaming ? (
                    <p className="stream-placeholder">{text('执行副导演正在处理…', 'The assistant director is working…')}</p>
                  ) : null}
                  {message.generatedAssets?.map((asset) => (
                      <button
                        className={`chat-generated-asset ${asset.contentType.startsWith('image/') ? 'image' : 'document'}`}
                        type="button"
                        key={asset.id}
                        onClick={() => openGeneratedAsset(asset)}
                        aria-label={`${asset.contentType.startsWith('image/') ? text('预览图片', 'Preview image') : text('审阅资源', 'Review asset')}: ${asset.name}`}
                      >
                        {asset.contentType.startsWith('image/')
                          ? <img src={asset.contentUrl} alt={asset.name} loading="lazy" />
                          : <FileText size={18} aria-hidden="true" />}
                        <span>
                          <strong>{asset.name}</strong>
                          {!asset.contentType.startsWith('image/') && <small>{asset.fileName} · v{asset.version}</small>}
                        </span>
                      </button>
                    ))}
                </article>
              ))}
            </div>
          ) : (
            <div className="empty-conversation">
              <span className="prompt-mark">A</span>
              <h2>{agentStatus?.configured ? text('等待导演指令', 'Waiting for direction') : text('Azure AI Foundry 未配置', 'Azure AI Foundry is not configured')}</h2>
              <p>
                {agentStatus?.configured
                  ? text('执行副导演已就绪。', 'The assistant director is ready.')
                  : text('请在系统配置中填写 Azure AI Foundry 设置。', 'Configure Azure AI Foundry in System Configuration.')}
              </p>
            </div>
          )}
          {conversationError && <p className="conversation-error">{conversationError}</p>}
        </section>
        <form className="composer" onSubmit={submitDirectorOrder}>
          <div className="composer-box">
            <textarea
              id="director-order"
              aria-label={text('导演令', 'Director instruction')}
              placeholder={text('告诉执行副导演现在要做什么…', 'Tell the assistant director what to do…')}
              rows={4}
              value={directorOrder}
              onChange={(event) => setDirectorOrder(event.target.value)}
            />
            {attachments.length > 0 && (
              <div className="attachment-list" aria-label={text('已添加附件', 'Attached files')}>
                {attachments.map((attachment, index) => (
                  <span className="attachment-chip" key={`${attachment.name}-${attachment.lastModified}`}>
                    <Paperclip size={13} aria-hidden="true" />
                    <span>{attachment.name}</span>
                    <button
                      type="button"
                      aria-label={text(`移除 ${attachment.name}`, `Remove ${attachment.name}`)}
                      title={text('移除附件', 'Remove attachment')}
                      onClick={() => setAttachments((current) => current.filter((_, itemIndex) => itemIndex !== index))}
                    >
                      <X size={13} aria-hidden="true" />
                    </button>
                  </span>
                ))}
              </div>
            )}
            <div className="composer-toolbar">
              <div className="composer-tools">
                <label className="attachment-button" title={text('添加附件', 'Add attachment')}>
                  <Paperclip size={18} aria-hidden="true" />
                  <span className="sr-only">{text('添加附件', 'Add attachment')}</span>
                  <input
                    type="file"
                    multiple
                    aria-label={text('添加附件', 'Add attachment')}
                    onChange={(event) => setAttachments(Array.from(event.target.files ?? []))}
                  />
                </label>
                <select
                  aria-label={text('选择模型', 'Select model')}
                  value={selectedModel}
                  onChange={(event) => setSelectedModel(event.target.value)}
                >
                  <option value="gpt-5.4">GPT-5.4</option>
                  <option value="gpt-5.4-mini">GPT-5.4 mini</option>
                  <option value="gpt-4.1">GPT-4.1</option>
                </select>
              </div>
              <button
                className="send-button"
                type={sendingMessage ? 'button' : 'submit'}
                aria-label={sendingMessage ? text('停止执行', 'Stop run') : text('发送导演令', 'Send instruction')}
                title={sendingMessage ? text('停止执行', 'Stop run') : text('发送导演令', 'Send instruction')}
                onClick={sendingMessage ? stopDirectorMessage : undefined}
                disabled={!sendingMessage && (
                  !agentStatus?.configured
                  || (!directorOrder.trim() && attachments.length === 0)
                )}
              >
                {sendingMessage
                  ? <Square size={16} fill="currentColor" aria-hidden="true" />
                  : <Send size={18} aria-hidden="true" />}
              </button>
            </div>
          </div>
        </form>
      </main>

      <aside className="context-panel">
        <header>
          <h2>{selectedSkillRun
            ? text('剧本拆解结果', 'Script analysis')
            : selectedSkill?.title ?? selectedAsset?.name ?? text('当前审阅', 'Current review')}</h2>
        </header>
        {selectedSkillRun && selectedAnalysis ? (
          <div className="skill-result">
            <div className="analysis-counts">
              <span><strong>{selectedAnalysis.characters.length}</strong>{text('人物', 'Characters')}</span>
              <span><strong>{selectedAnalysis.locations.length}</strong>{text('场景', 'Scenes')}</span>
              <span><strong>{selectedAnalysis.props.length}</strong>{text('道具', 'Props')}</span>
              <span><strong>{selectedAnalysis.scenes.length}</strong>{text('场次', 'Scenes')}</span>
            </div>
            <section>
              <h3>{text('人物', 'Characters')}</h3>
              <p>{selectedAnalysis.characters.map((item) => item.name).join(language === 'zh-CN' ? '、' : ', ') || text('未识别', 'None identified')}</p>
            </section>
            <section>
              <h3>{text('场景', 'Locations')}</h3>
              <p>{selectedAnalysis.locations.map((item) => item.name).join(language === 'zh-CN' ? '、' : ', ') || text('未识别', 'None identified')}</p>
            </section>
            <section>
              <h3>{text('道具', 'Props')}</h3>
              <p>{selectedAnalysis.props.map((item) => item.name).join(language === 'zh-CN' ? '、' : ', ') || text('未识别', 'None identified')}</p>
            </section>
            <section>
              <h3>{text('每场发生的事情', 'Scene summaries')}</h3>
              <div className="analysis-scenes">
                {selectedAnalysis.scenes.map((scene, index) => (
                  <article key={`${scene.heading}-${index}`}>
                    <strong>{scene.heading || text(`场次 ${index + 1}`, `Scene ${index + 1}`)}</strong>
                    <small>{[scene.time, scene.location].filter(Boolean).join(' · ')}</small>
                    <p>{scene.summary}</p>
                  </article>
                ))}
              </div>
            </section>
            <section className={selectedAnalysis.ambiguities.length > 0 ? 'analysis-ambiguities' : ''}>
              <h3>{text('待导演确认', 'Needs director confirmation')}</h3>
              {selectedAnalysis.ambiguities.length > 0
                ? <ul>{selectedAnalysis.ambiguities.map((item) => <li key={item}>{item}</li>)}</ul>
                : <p>{text('没有发现需要确认的歧义。', 'No ambiguities require confirmation.')}</p>}
            </section>
          </div>
        ) : selectedSkillRun ? (
          <div className="empty-review review-error">
            <WandSparkles size={24} aria-hidden="true" />
            <p>{selectedSkillRun.error ?? text('技能结果无法解析', 'The skill result could not be parsed')}</p>
          </div>
        ) : selectedSkill ? (
          <div className="skill-preview">
            <div className="skill-preview-summary">
              <span className={selectedSkill.isEnabled ? 'enabled' : 'disabled'}>
                {selectedSkill.isEnabled ? text('已启用', 'Enabled') : text('已停用', 'Disabled')}
              </span>
              <p>{selectedSkill.description}</p>
            </div>
            <pre>{selectedSkill.content || text('此技能没有可显示的 SKILL.md 内容。', 'This skill has no SKILL.md content to display.')}</pre>
          </div>
        ) : assetPreviewLoading ? (
          <div className="empty-review">
            <LoaderCircle className="spin" size={24} aria-hidden="true" />
            <p>{text('正在加载内容…', 'Loading content…')}</p>
          </div>
        ) : assetPreviewError ? (
          <div className="empty-review review-error">
            <FileText size={24} aria-hidden="true" />
            <p>{assetPreviewError}</p>
          </div>
        ) : selectedAsset ? (
          <div className="asset-preview">
            <div className="asset-preview-meta">
              <span>{selectedAsset.fileName}</span>
              <div className="asset-version-controls">
                {assetVersions.length > 1 && (
                  <select
                    aria-label={text('资源版本', 'Asset version')}
                    value={selectedAsset.id}
                    onChange={(event) => {
                      const version = assetVersions.find((item) => item.id === event.target.value)
                      if (version) void reviewAsset(version)
                    }}
                  >
                    {assetVersions.map((version) => (
                      <option key={version.id} value={version.id}>
                        v{version.version} · {formatDate(version.createdAtUtc, language)}
                      </option>
                    ))}
                  </select>
                )}
                <small>{formatFileSize(selectedAsset.sizeBytes)}</small>
              </div>
            </div>
            {associatedSettingImages.length > 0 && (
              <section className="shot-linked-assets setting-linked-images" aria-labelledby="setting-linked-images-title">
                <header>
                  <h3 id="setting-linked-images-title">{text('关联图片设定稿', 'Linked visual designs')}</h3>
                  <span>{associatedSettingImages.length}</span>
                </header>
                <div className="shot-linked-assets-grid">
                  {associatedSettingImages.map((image) => (
                    <article className="shot-linked-asset" key={image.id}>
                      <button
                        type="button"
                        className="shot-linked-media"
                        onClick={() => openImagePreview(image)}
                        aria-label={text(`全屏预览${image.name}`, `Preview ${image.name} full screen`)}
                      >
                        <img src={image.contentUrl} alt={image.name} />
                      </button>
                      <div>
                        <span>{text(`图片设定稿 v${image.version}`, `Visual design v${image.version}`)}</span>
                        <strong>{image.name}</strong>
                      </div>
                    </article>
                  ))}
                </div>
              </section>
            )}
            {selectedAsset.type === 'shot' && (
              <section className="shot-linked-assets" aria-labelledby="shot-linked-assets-title">
                <header>
                  <h3 id="shot-linked-assets-title">{text('镜头素材', 'Shot assets')}</h3>
                  <span>{shotAssetLinks.length}</span>
                </header>
                {shotAssetLinks.length > 0 ? (
                  <div className="shot-linked-assets-grid">
                    {shotAssetLinks.map((link) => (
                      <article className="shot-linked-asset" key={link.id}>
                        {link.asset.contentType.startsWith('image/') ? (
                          <button
                            type="button"
                            className="shot-linked-media"
                            onClick={() => openImagePreview(link.asset)}
                            aria-label={text(`全屏预览${link.asset.name}`, `Preview ${link.asset.name} full screen`)}
                          >
                            <img src={link.asset.contentUrl} alt={link.asset.name} />
                          </button>
                        ) : link.asset.contentType.startsWith('video/') ? (
                          <button
                            type="button"
                            className="shot-linked-media"
                            onClick={() => openImagePreview(link.asset)}
                            aria-label={text(`播放预览${link.asset.name}`, `Play ${link.asset.name}`)}
                          >
                            <video src={link.asset.contentUrl} preload="metadata" muted />
                          </button>
                        ) : link.asset.contentType.startsWith('audio/') ? (
                          <audio src={link.asset.contentUrl} controls preload="metadata" />
                        ) : null}
                        <div>
                          <span>{({
                            'first-frame': text('首帧', 'First frame'),
                            'last-frame': text('尾帧', 'Last frame'),
                            reference: text('关键帧 / 参考', 'Keyframe / Reference'),
                            video: text('镜头视频', 'Shot video'),
                            other: text('其他', 'Other'),
                          } as const)[link.role]}</span>
                          <strong>{link.asset.name}</strong>
                        </div>
                      </article>
                    ))}
                  </div>
                ) : (
                  <p>{text('暂无关联素材。生成首帧、尾帧或视频后，Agent 会通过技能绑定到这里。', 'No linked assets yet. The Agent will link generated first frames, last frames, and videos here.')}</p>
                )}
              </section>
            )}
            {assetPreviewText !== null ? (
              <pre>{assetPreviewText}</pre>
            ) : selectedAsset.contentType.startsWith('image/') ? (
              <button
                className="asset-image-preview-button"
                type="button"
                onClick={() => openImagePreview(selectedAsset)}
                aria-label={text(`全屏预览图片：${selectedAsset.name}`, `Preview image full screen: ${selectedAsset.name}`)}
                title={text('全屏预览', 'Full-screen preview')}
              >
                <img src={selectedAsset.contentUrl} alt={selectedAsset.name} />
                <span><Maximize2 size={16} aria-hidden="true" />{text('全屏预览', 'Full-screen preview')}</span>
              </button>
            ) : selectedAsset.contentType.startsWith('video/') ? (
              <video src={selectedAsset.contentUrl} controls />
            ) : selectedAsset.contentType.startsWith('audio/') ? (
              <audio src={selectedAsset.contentUrl} controls />
            ) : selectedAsset.contentType === 'application/pdf' ? (
              <object data={selectedAsset.contentUrl} type="application/pdf">
                <p>{text('浏览器无法预览此 PDF。', 'The browser cannot preview this PDF.')}</p>
              </object>
            ) : (
              <div className="unsupported-preview">
                <FileText size={28} aria-hidden="true" />
                <p>{text('暂不支持预览此文件格式', 'This file format cannot be previewed')}</p>
              </div>
            )}
          </div>
        ) : (
          <div className="empty-review">
            <span>00:00:00</span>
            <p>{text('暂无待审内容', 'Nothing to review')}</p>
          </div>
        )}
        <div className={`asset-detail ${assetDetailExpanded ? '' : 'collapsed'}`}>
          <button
            className="asset-detail-toggle"
            type="button"
            aria-expanded={assetDetailExpanded}
            aria-controls="asset-detail-content"
            title={assetDetailExpanded ? text('收起资源信息', 'Collapse asset information') : text('展开资源信息', 'Expand asset information')}
            onClick={() => setAssetDetailExpanded((expanded) => !expanded)}
          >
            <span>{selectedSkillRun ? text('技能运行信息', 'Skill run') : selectedSkill ? text('技能信息', 'Skill information') : text('当前资源信息', 'Asset information')}</span>
            {assetDetailExpanded
              ? <ChevronDown size={16} aria-hidden="true" />
              : <ChevronUp size={16} aria-hidden="true" />}
          </button>
          <div className="asset-detail-body" id="asset-detail-content">
            {selectedSkillRun ? (
              <dl>
                <div><dt>{text('技能', 'Skill')}</dt><dd>{skills.find((skill) => skill.id === selectedSkillRun.skillId)?.name ?? selectedSkillRun.skillId}</dd></div>
                <div><dt>{text('状态', 'Status')}</dt><dd>{selectedSkillRun.status}</dd></div>
                <div><dt>{text('模型', 'Model')}</dt><dd>{selectedSkillRun.model}</dd></div>
                <div><dt>{text('输入资源', 'Input asset')}</dt><dd>{selectedAsset?.fileName ?? selectedSkillRun.inputAssetId}</dd></div>
                <div><dt>{text('开始时间', 'Started')}</dt><dd>{formatDate(selectedSkillRun.startedAtUtc, language)}</dd></div>
                <div><dt>{text('运行 ID', 'Run ID')}</dt><dd>{selectedSkillRun.id}</dd></div>
              </dl>
            ) : selectedSkill ? (
              <dl>
                <div><dt>{text('名称', 'Name')}</dt><dd>{selectedSkill.name}</dd></div>
                <div><dt>{text('版本', 'Version')}</dt><dd>v{selectedSkill.version}</dd></div>
                <div><dt>{text('类型', 'Type')}</dt><dd>{selectedSkill.isSystem ? text('系统技能', 'System skill') : text('项目技能', 'Project skill')}</dd></div>
                <div><dt>{text('状态', 'Status')}</dt><dd>{selectedSkill.isEnabled ? text('已启用', 'Enabled') : text('已停用', 'Disabled')}</dd></div>
                <div><dt>{text('允许工具', 'Allowed tools')}</dt><dd>{selectedSkill.allowedTools.join(language === 'zh-CN' ? '、' : ', ') || text('无', 'None')}</dd></div>
              </dl>
            ) : selectedAsset ? (
              <dl>
                <div><dt>{text('名称', 'Name')}</dt><dd>{selectedAsset.name}</dd></div>
                <div><dt>{text('编号', 'Number')}</dt><dd>{selectedAsset.number.toString().padStart(3, '0')}</dd></div>
                <div><dt>{text('版本', 'Version')}</dt><dd>v{selectedAsset.version} / {selectedAsset.versionCount}</dd></div>
                <div><dt>{text('分类', 'Category')}</dt><dd>{localize(language, selectedAssetSection.label, selectedAssetSection.labelEn)}</dd></div>
                <div><dt>{text('文件名', 'File name')}</dt><dd>{selectedAsset.fileName}</dd></div>
                <div><dt>{text('格式', 'Format')}</dt><dd>{selectedAsset.contentType}</dd></div>
                <div><dt>{text('大小', 'Size')}</dt><dd>{formatFileSize(selectedAsset.sizeBytes)}</dd></div>
                {selectedAsset.sourceScript && (
                  <div>
                    <dt>{text('关联剧本', 'Source script')}</dt>
                    <dd>
                      <button
                        className="source-script-button"
                        type="button"
                        onClick={() => {
                          const script = assets.find((asset) =>
                            asset.type === 'script'
                            && asset.resourceId === selectedAsset.sourceScript?.resourceId)
                          if (script) void reviewAsset(script)
                        }}
                      >
                        {selectedAsset.sourceScript.name} v{selectedAsset.sourceScript.version}
                      </button>
                    </dd>
                  </div>
                )}
                <div><dt>{text('添加时间', 'Added')}</dt><dd>{formatDate(selectedAsset.createdAtUtc, language)}</dd></div>
                <div><dt>{text('资源 ID', 'Asset ID')}</dt><dd>{selectedAsset.id}</dd></div>
                {(() => {
                  const metadata = parseImageGenerationMetadata(selectedAsset.generationMetadataJson)
                  if (!selectedAsset.contentType.startsWith('image/') || !metadata) return null
                  return (
                    <>
                      <div><dt>{text('生成方式', 'Generation method')}</dt><dd>{formatImageOperation(metadata.operation, language)}</dd></div>
                      <div><dt>{text('模型', 'Model')}</dt><dd>{metadata.model}</dd></div>
                      <div>
                        <dt>{text('调用参数', 'Parameters')}</dt>
                        <dd>{metadata.parameters.size} · {metadata.parameters.quality} · {metadata.parameters.outputFormat} · {metadata.parameters.count} {text('张', 'images')}{metadata.parameters.apiVersion ? ` · ${metadata.parameters.apiVersion}` : ''}</dd>
                      </div>
                      {metadata.prompt && (
                        <div className="generation-detail-row">
                          <dt>{text('提示词', 'Prompt')}</dt><dd><pre>{metadata.prompt}</pre></dd>
                        </div>
                      )}
                      {metadata.revisedPrompt && metadata.revisedPrompt !== metadata.prompt && (
                        <div className="generation-detail-row">
                          <dt>{text('修订提示词', 'Revised prompt')}</dt><dd><pre>{metadata.revisedPrompt}</pre></dd>
                        </div>
                      )}
                      {metadata.sources.length > 0 && (
                        <div className="generation-detail-row">
                          <dt>{text('来源图片', 'Source images')}</dt>
                          <dd className="generation-sources">
                            {metadata.sources.map((source) => (
                              <span key={source.assetId}>{source.name} v{source.version}{source.description ? ` · ${source.description}` : ''}</span>
                            ))}
                          </dd>
                        </div>
                      )}
                    </>
                  )
                })()}
                {(() => {
                  const metadata = parseVideoGenerationMetadata(selectedAsset.generationMetadataJson)
                  if (!selectedAsset.contentType.startsWith('video/') || !metadata) return null
                  return (
                    <>
                      <div><dt>{text('生成方式', 'Generation method')}</dt><dd>{text('ComfyUI 视频生成', 'ComfyUI video generation')}</dd></div>
                      <div><dt>{text('模型', 'Model')}</dt><dd>{metadata.model}</dd></div>
                      <div><dt>Workflow</dt><dd>{metadata.parameters.workflow}</dd></div>
                      <div>
                        <dt>{text('调用参数', 'Parameters')}</dt>
                        <dd>{metadata.parameters.width}×{metadata.parameters.height} · {metadata.parameters.frameCount} {text('帧', 'frames')} · {metadata.parameters.fps} FPS · {metadata.parameters.frameFitMode}</dd>
                      </div>
                      <div className="generation-detail-row">
                        <dt>{text('提示词', 'Prompt')}</dt><dd><pre>{metadata.prompt}</pre></dd>
                      </div>
                      {metadata.sources.length > 0 && (
                        <div className="generation-detail-row">
                          <dt>{text('关键帧来源', 'Keyframe sources')}</dt>
                          <dd className="generation-sources">
                            {metadata.sources.map((source) => (
                              <span key={`${source.role}-${source.assetId}`}>{source.role} · {source.assetId}</span>
                            ))}
                          </dd>
                        </div>
                      )}
                    </>
                  )
                })()}
                {(() => {
                  const metadata = parseSpeechGenerationMetadata(selectedAsset.generationMetadataJson)
                  if (!selectedAsset.contentType.startsWith('audio/') || !metadata) return null
                  return (
                    <>
                      <div><dt>{text('生成方式', 'Generation method')}</dt><dd>{text('文本转语音', 'Text to speech')}</dd></div>
                      <div><dt>{text('模型', 'Model')}</dt><dd>{metadata.model}</dd></div>
                      <div><dt>Voice</dt><dd>{metadata.parameters.voice}</dd></div>
                      <div>
                        <dt>{text('调用参数', 'Parameters')}</dt>
                        <dd>{metadata.parameters.responseFormat} · {metadata.parameters.speed}×{metadata.parameters.apiVersion ? ` · ${metadata.parameters.apiVersion}` : ''}</dd>
                      </div>
                      <div className="generation-detail-row">
                        <dt>{text('朗读原文', 'Source text')}</dt><dd><pre>{metadata.prompt}</pre></dd>
                      </div>
                      {metadata.parameters.instructions && (
                        <div className="generation-detail-row">
                          <dt>{text('表演指令', 'Performance direction')}</dt><dd><pre>{metadata.parameters.instructions}{metadata.parameters.instructionsApplied ? '' : text('\n（当前部署不支持，未提交给模型）', '\n(Not supported by the current deployment; not sent to the model.)')}</pre></dd>
                        </div>
                      )}
                    </>
                  )
                })()}
              </dl>
            ) : (
              <p className="asset-detail-empty">{text('选择一个资源后查看详细信息', 'Select an asset to view details')}</p>
            )}
          </div>
        </div>
      </aside>

      {previewImage && (
        <div
          className="image-lightbox"
          role="dialog"
          aria-modal="true"
          aria-label={text(`媒体预览：${previewImage.name}`, `Media preview: ${previewImage.name}`)}
        >
          <header className="image-lightbox-header">
            <div>
              <strong>{previewImage.name}</strong>
              {previewImage.contentType.startsWith('image/') && <span>{Math.round(previewZoom * 100)}%</span>}
            </div>
            <div className="image-lightbox-tools">
              {previewImage.contentType.startsWith('image/') && (
                <>
                  <button type="button" title={text('缩小', 'Zoom out')} aria-label={text('缩小', 'Zoom out')} onClick={() => changePreviewZoom(previewZoom - 0.25)}>
                    <Minus size={18} aria-hidden="true" />
                  </button>
                  <button type="button" title={text('适应窗口', 'Fit to window')} aria-label={text('适应窗口', 'Fit to window')} onClick={resetImagePreview}>
                    <Maximize2 size={18} aria-hidden="true" />
                  </button>
                  <button type="button" title={text('放大', 'Zoom in')} aria-label={text('放大', 'Zoom in')} onClick={() => changePreviewZoom(previewZoom + 0.25)}>
                    <Plus size={18} aria-hidden="true" />
                  </button>
                </>
              )}
              <a href={previewImage.contentUrl} download={previewImage.fileName} title={text('下载文件', 'Download file')} aria-label={text('下载文件', 'Download file')}>
                <Download size={18} aria-hidden="true" />
              </a>
              <button type="button" title={text('关闭', 'Close')} aria-label={text('关闭媒体预览', 'Close media preview')} onClick={closeImagePreview}>
                <X size={19} aria-hidden="true" />
              </button>
            </div>
          </header>
          {previewImage.contentType.startsWith('video/') ? (
            <div className="image-lightbox-stage video-lightbox-stage">
              <video src={previewImage.contentUrl} controls autoPlay preload="metadata" />
            </div>
          ) : (
            <div
              className={`image-lightbox-stage ${previewZoom > 1 ? 'zoomed' : ''}`}
              onDoubleClick={() => changePreviewZoom(previewZoom === 1 ? 2 : 1)}
              onWheel={(event) => {
                event.preventDefault()
                changePreviewZoom(previewZoom + (event.deltaY < 0 ? 0.25 : -0.25))
              }}
              onPointerDown={(event) => {
                if (previewZoom <= 1) return
                previewDragRef.current = { pointerId: event.pointerId, x: event.clientX, y: event.clientY }
                event.currentTarget.setPointerCapture(event.pointerId)
              }}
              onPointerMove={(event) => {
                const drag = previewDragRef.current
                if (!drag || drag.pointerId !== event.pointerId) return
                setPreviewOffset((offset) => ({
                  x: offset.x + event.clientX - drag.x,
                  y: offset.y + event.clientY - drag.y,
                }))
                previewDragRef.current = { pointerId: event.pointerId, x: event.clientX, y: event.clientY }
              }}
              onPointerUp={() => { previewDragRef.current = null }}
              onPointerCancel={() => { previewDragRef.current = null }}
            >
              <img
                src={previewImage.contentUrl}
                alt={previewImage.name}
                draggable={false}
                style={{ transform: `translate(${previewOffset.x}px, ${previewOffset.y}px) scale(${previewZoom})` }}
              />
            </div>
          )}
        </div>
      )}

      <nav className="mobile-panel-switcher" aria-label={text('手机屏幕切换', 'Mobile panel navigation')}>
        <button
          type="button"
          aria-label={text(`切换到${mobilePanels[Math.max(0, mobilePanelIndex - 1)].label}`, `Switch to ${mobilePanels[Math.max(0, mobilePanelIndex - 1)].labelEn}`)}
          title={text('上一个屏幕', 'Previous panel')}
          disabled={mobilePanelIndex === 0}
          onClick={() => setMobilePanel(mobilePanels[mobilePanelIndex - 1].id)}
        >
          <ChevronLeft size={21} aria-hidden="true" />
        </button>
        <span className="sr-only" aria-live="polite">{text(`当前屏幕：${mobilePanels[mobilePanelIndex].label}`, `Current panel: ${mobilePanels[mobilePanelIndex].labelEn}`)}</span>
        <button
          type="button"
          aria-label={text(`切换到${mobilePanels[Math.min(mobilePanels.length - 1, mobilePanelIndex + 1)].label}`, `Switch to ${mobilePanels[Math.min(mobilePanels.length - 1, mobilePanelIndex + 1)].labelEn}`)}
          title={text('下一个屏幕', 'Next panel')}
          disabled={mobilePanelIndex === mobilePanels.length - 1}
          onClick={() => setMobilePanel(mobilePanels[mobilePanelIndex + 1].id)}
        >
          <ChevronRight size={21} aria-hidden="true" />
        </button>
      </nav>
    </div>
  )
}

export default App
