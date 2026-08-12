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
  Images,
  LoaderCircle,
  MapPinned,
  Maximize2,
  Minus,
  Paperclip,
  Plus,
  RotateCcw,
  Send,
  Settings2,
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
const defaultFoundryConfiguration: GlobalFoundryConfiguration = {
  openAiEndpoint: '',
  openAiDeployment: 'gpt-5.4',
  openAiApiKeyConfigured: false,
  imageEndpoint: '',
  imageDeployment: 'gpt-image-2',
  imageApiVersion: '2025-04-01-preview',
  imageQuality: 'medium',
  imageApiKeyConfigured: false,
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
const mobilePanels: Array<{ id: MobilePanel; label: string }> = [
  { id: 'assets', label: '资产' },
  { id: 'director', label: '导演台' },
  { id: 'review', label: '审阅' },
]

const assetSections: AssetSection[] = [
  { id: 'scripts', type: 'script', label: '剧本', accept: '.md,.txt,.pdf,.doc,.docx', icon: FileText },
  { id: 'analyses', type: 'analysis', label: '分析', accept: '.md,.json', icon: FileSearch },
  { id: 'images', type: 'media', label: '图片素材', accept: 'image/*', icon: Images, contentTypePrefix: 'image/' },
  { id: 'videos', type: 'media', label: '视频素材', accept: 'video/*', icon: Video, contentTypePrefix: 'video/' },
  { id: 'characters', type: 'character', label: '人物', accept: '.md,image/*,.pdf', icon: Users },
  { id: 'scenes', type: 'scene', label: '场景', accept: '.md,image/*,video/*,.pdf', icon: MapPinned },
  { id: 'props', type: 'prop', label: '道具', accept: '.md,image/*,.pdf', icon: Box },
  { id: 'shots', type: 'shot', label: '镜头', accept: '.md,.txt,image/*,video/*,.pdf', icon: Clapperboard },
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

function parseImageGenerationMetadata(value: string | null): ImageGenerationMetadata | null {
  if (!value) return null
  try {
    return JSON.parse(value) as ImageGenerationMetadata
  } catch {
    return null
  }
}

function formatImageOperation(operation: ImageGenerationMetadata['operation']) {
  const labels: Record<ImageGenerationMetadata['operation'], string> = {
    generate: '模型生成',
    'generate-from-references': '参考图生成',
    edit: '图片编辑',
    'merge-references': '本地合成',
  }
  return labels[operation]
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
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
  const [assetDetailExpanded, setAssetDetailExpanded] = useState(true)
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
  const [foundryConfigurationState, setFoundryConfigurationState] = useState<'idle' | 'loading' | 'saving' | 'saved' | 'error'>('idle')
  const [mobilePanel, setMobilePanel] = useState<MobilePanel>('director')
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
        || asset.contentType.startsWith(selectedAssetSection.contentTypePrefix)))
    .sort((left, right) => selectedAssetSection.type === 'shot'
      ? left.name.localeCompare(right.name, 'zh-CN')
      : new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime())
  const groupedAssetCount = assets.filter((asset) =>
    groupedAssetSections.some((section) => section.type === asset.type)).length
  const mobilePanelIndex = mobilePanels.findIndex((panel) => panel.id === mobilePanel)
  const projectFormat = selectedProject ? getProjectFormat(selectedProject) : null
  const {
    messages,
    messagesLoading,
    sendingMessage,
    conversationError,
    sendDirectorMessage,
    retryDirectorMessage,
  } = useDirectorConversation({
    project: selectedProject,
    agentConfigured: agentStatus?.configured ?? false,
    selectedAsset,
    aspectRatio: projectFormat?.aspectRatio ?? '16:9',
    resolution: projectFormat?.resolution ?? '1920x1080',
    imageSize: projectFormat?.imageSize ?? '1536x1024',
    onMessageStart: prepareDirectorMessageScroll,
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
      .then(setAssets)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setAssetError(error instanceof Error ? error.message : '资产列表加载失败')
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
        setFoundryConfigurationState('idle')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFoundryConfigurationState('error')
      })

    return () => controller.abort()
  }, [selectedProject])

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
        setAssetPreviewError(error instanceof Error ? error.message : '资源版本加载失败')
      })
  }, [assets, assetsLoading, selectedAsset, selectedProject])

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
        setSkillError(error instanceof Error ? error.message : '技能加载失败')
      })
      .finally(() => {
        if (!controller.signal.aborted) setSkillsLoading(false)
      })

    return () => controller.abort()
  }, [selectedProject])

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
        clearOpenAiApiKey: false,
        clearImageApiKey: false,
      }))
      setOpenAiApiKey('')
      setImageApiKey('')
      setFoundryConfigurationState('saved')
      const statusResponse = await fetch('/api/agent/status')
      if (statusResponse.ok) setAgentStatus(await statusResponse.json() as AgentStatus)
    } catch {
      setFoundryConfigurationState('error')
    }
  }

  async function submitDirectorOrder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const text = directorOrder.trim()
    if (!text && attachments.length === 0) return
    if (!selectedProject || !agentStatus?.configured) return

    const attachmentContext = attachments.length > 0
      ? `附件：${attachments.map((attachment) => attachment.name).join('、')}`
      : ''
    const message = [text, attachmentContext].filter(Boolean).join('\n\n')

    setDirectorOrder('')
    setAttachments([])
    await sendDirectorMessage(message, selectedModel, selectedAsset?.id)
  }

  function prepareDirectorMessageScroll() {
    scrollToLatestAfterLoadRef.current = true
    stickToLatestRef.current = true
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
          || firstAsset.contentType.startsWith(section.contentTypePrefix)))
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
      setAssetError(error instanceof Error ? error.message : '资产上传失败')
    } finally {
      setUploadingAssets(false)
    }
  }

  async function deleteAsset(asset: AssetRecord) {
    if (!selectedProject || deletingAssetId) return
    const versionLabel = asset.versionCount > 1 ? `及其全部 ${asset.versionCount} 个版本` : ''
    if (!window.confirm(`永久删除“${asset.name}”${versionLabel}？此操作不可撤销。`)) return

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
      setAssetError(error instanceof Error ? error.message : '资产删除失败')
    } finally {
      setDeletingAssetId(null)
    }
  }

  async function refreshProjectAssets() {
    if (!selectedProject || refreshingAssets) return

    setRefreshingAssets(true)
    setAssetError(null)
    try {
      setAssets(await listProjectAssets(selectedProject.id))
    } catch (error) {
      setAssetError(error instanceof Error ? error.message : '资产列表刷新失败')
    } finally {
      setRefreshingAssets(false)
    }
  }

  async function reviewAsset(asset: AssetRecord) {
    if (!selectedProject) return
    const section = assetSections.find((item) => item.type === asset.type
      && (!item.contentTypePrefix || asset.contentType.startsWith(item.contentTypePrefix)))
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
        setAssetPreviewError(error instanceof Error ? error.message : '资源版本加载失败')
      })

    if (asset.type === 'shot') {
      void listShotAssetLinks(selectedProject.id, asset.id)
        .then(setShotAssetLinks)
        .catch((error: unknown) => {
          setAssetPreviewError(error instanceof Error ? error.message : '镜头素材加载失败')
        })
    }

    const isTextAsset = asset.type === 'script'
      || asset.type === 'analysis'
      || asset.contentType.startsWith('text/')
    if (!isTextAsset) return

    setAssetPreviewLoading(true)
    try {
      const response = await fetch(asset.contentUrl)
      if (!response.ok) throw new Error('资产内容加载失败')
      const text = await response.text()
      if (asset.contentType === 'application/json') {
        try {
          setAssetPreviewText(JSON.stringify(JSON.parse(text), null, 2))
        } catch {
          setAssetPreviewText(text)
        }
      } else {
        setAssetPreviewText(text)
      }
    } catch (error) {
      setAssetPreviewError(error instanceof Error ? error.message : '资产内容加载失败')
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
      setSkillError(error instanceof Error ? error.message : '技能状态更新失败')
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
          <h2 id="project-settings-title">项目信息</h2>
        </div>
        <button className="switch-project" type="button" onClick={() => navigate('/')}>
          切换
        </button>
      </header>
      <div className="project-settings-form">
        <section className="project-settings-group">
          <h3>基本信息</h3>
          <label htmlFor="project-settings-name">项目名称</label>
          <input
            id="project-settings-name"
            value={selectedProject.name}
            maxLength={100}
            onChange={(event) => updateProject({ name: event.target.value })}
          />
          <label htmlFor="project-settings-description">项目描述</label>
          <textarea
            id="project-settings-description"
            value={selectedProject.description}
            maxLength={1000}
            rows={4}
            placeholder="故事类型、制作目标或项目范围"
            onChange={(event) => updateProject({ description: event.target.value })}
          />
        </section>

        <section className="project-settings-group">
          <h3>画面与交付</h3>
          <label htmlFor="project-settings-format">成片画面比例（分辨率）</label>
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
              <option value={preset.id} key={preset.id}>{preset.label}</option>
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
                aria-label="画面宽度"
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
                aria-label="画面高度"
                onChange={(event) => updateProjectFormat(
                  'custom',
                  selectedProject.outputWidth,
                  Math.min(8192, Math.max(64, Number(event.target.value))),
                )}
              />
            </div>
          )}
          <label htmlFor="project-settings-preview-resolution">快速拉片分辨率</label>
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
          <h3>生成模型</h3>
          <label htmlFor="project-settings-language-model">语言模型</label>
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
          <label htmlFor="project-settings-image-model">Image 模型</label>
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
              系统配置 · {agentStatus?.imageDeployment ?? 'gpt-image-2'}
            </option>
            <option value="custom">自定义 Azure 部署</option>
          </select>
          {(selectingCustomImageModel
            || selectedProject.imageModel !== (agentStatus?.imageDeployment ?? 'gpt-image-2')) && (
            <>
              <label htmlFor="project-settings-custom-image-model">图片部署名称</label>
              <input
                id="project-settings-custom-image-model"
                value={selectedProject.imageModel}
                maxLength={100}
                placeholder="Azure 部署名称"
                onChange={(event) => updateProject({ imageModel: event.target.value })}
              />
            </>
          )}
          <div className="project-setting-readout">
            <span>模型生成尺寸</span>
            <strong>{projectFormat?.imageSize.replace('x', ' × ')}</strong>
          </div>

          <label htmlFor="project-settings-video-model">视频模型</label>
          <select
            id="project-settings-video-model"
            value={selectingCustomVideoModel || selectedProject.videoModel ? 'custom' : 'none'}
            onChange={(event) => {
              const isCustom = event.target.value === 'custom'
              setSelectingCustomVideoModel(isCustom)
              if (!isCustom) updateProject({ videoModel: '' })
            }}
          >
            <option value="none">未配置</option>
            <option value="custom">自定义 Azure 部署</option>
          </select>
          {(selectingCustomVideoModel || selectedProject.videoModel) && (
            <>
              <label htmlFor="project-settings-custom-video-model">视频部署名称</label>
              <input
                id="project-settings-custom-video-model"
                value={selectedProject.videoModel}
                maxLength={100}
                placeholder="Azure 部署名称"
                onChange={(event) => updateProject({ videoModel: event.target.value })}
              />
            </>
          )}
          <div className="project-setting-readout">
            <span>视频生成</span>
            <strong>{selectedProject.videoModel.trim() ? '使用项目部署' : '未配置'}</strong>
          </div>
        </section>

      </div>
    </section>
  ) : null

  if (projectsLoading) {
    return (
      <main className="project-gate">
        <div className="no-projects">
          <p>正在加载项目...</p>
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
            <strong>alex 导演台</strong>
            <span>PROJECT ENTRY</span>
          </div>
        </header>

        <div className="gate-layout">
          <section className="project-picker" aria-labelledby="project-picker-title">
            <p className="section-label">DIRECTOR'S PROJECTS</p>
            <h1 id="project-picker-title">先选择一个项目</h1>
            <p className="gate-intro">导演台中的指令、素材与审阅都归属于具体项目。</p>

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
                      <small>创建于 {new Date(project.createdAt).toLocaleDateString('zh-CN')}</small>
                    </span>
                    <span className="enter-project" aria-hidden="true">进入 →</span>
                  </button>
                ))}
              </div>
            ) : (
              <div className="no-projects">
                <span>0</span>
                <p>还没有项目，从右侧创建第一个。</p>
              </div>
            )}
          </section>

          <section className="create-project" aria-labelledby="create-project-title">
            <p className="section-label">NEW PROJECT</p>
            <h2 id="create-project-title">创建项目</h2>
            <form onSubmit={createProject}>
              <label htmlFor="project-name">项目名称</label>
              <input
                id="project-name"
                value={newProjectName}
                onChange={(event) => setNewProjectName(event.target.value)}
                placeholder="例如：天桥食堂"
                autoComplete="off"
                autoFocus={projects.length === 0}
              />
              <button type="submit" disabled={!newProjectName.trim()}>
                创建并进入
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
            <strong>alex 导演台</strong>
          </div>
        </header>
        <div className="sidebar-middle">
          <nav className="icon-menu" aria-label="制作资源与技能">
            <button
              type="button"
              className={sidebarMode === 'settings' ? 'active' : undefined}
              aria-label="项目信息"
              aria-pressed={sidebarMode === 'settings'}
              title="项目信息"
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
                    aria-label={section.label}
                    aria-pressed={isActive}
                    title={section.label}
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
              aria-label="资产"
              aria-pressed={sidebarMode === 'assets' && isGroupedAssetSection}
              title="资产"
              onClick={() => {
                if (!isGroupedAssetSection) setActiveAssetSection('analyses')
                setExpandedAssetSections((current) => new Set(current).add('analyses'))
                navigate(`/projects/${selectedProject.id}`)
              }}
            >
              <Box size={19} strokeWidth={1.7} aria-hidden="true" />
            </button>
            {assetSections
              .filter((section) => ['images', 'videos'].includes(section.id))
              .map((section) => {
                const Icon = section.icon
                const isActive = sidebarMode === 'assets' && selectedAssetSection.id === section.id
                return (
                  <button
                    type="button"
                    className={isActive ? 'active' : undefined}
                    key={section.id}
                    aria-label={section.label}
                    aria-pressed={isActive}
                    title={section.label}
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
                    aria-label={section.label}
                    aria-pressed={isActive}
                    title={section.label}
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
              aria-label="系统配置"
              aria-pressed={sidebarMode === 'configuration'}
              title="系统配置"
              onClick={() => navigate(`/projects/${selectedProject.id}/configuration`)}
            >
              <WandSparkles size={19} strokeWidth={1.7} aria-hidden="true" />
            </button>
          </nav>

          {sidebarMode === 'settings' ? projectSettingsPanel : sidebarMode === 'assets' ? (
          <AssetPanel
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
            agentStatus={agentStatus}
            foundryConfiguration={foundryConfiguration}
            setFoundryConfiguration={setFoundryConfiguration}
            foundryConfigurationState={foundryConfigurationState}
            openAiApiKey={openAiApiKey}
            setOpenAiApiKey={setOpenAiApiKey}
            imageApiKey={imageApiKey}
            setImageApiKey={setImageApiKey}
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
              服务状态
            </span>
            {serviceStatusExpanded
              ? <ChevronDown size={15} aria-hidden="true" />
              : <ChevronUp size={15} aria-hidden="true" />}
          </button>
          {serviceStatusExpanded && (
            <div className="service-status-details">
              <div>
                <span className={`status-dot ${apiState}`} />
                API {apiState === 'online' ? '在线' : apiState === 'offline' ? '离线' : '检查中'}
              </div>
              <div>
                <span className={`status-dot ${agentStatus?.configured ? 'online' : 'idle'}`} />
                MAF {agentStatus?.configured ? '已配置' : '待配置'}
              </div>
              <div>
                <span className={`status-dot ${agentStatus?.imageConfigured ? 'online' : 'idle'}`} />
                Image {agentStatus?.imageConfigured
                  ? `${agentStatus.imageDeployment} · ${agentStatus.imageQuality}`
                  : '待配置'}
              </div>
            </div>
          )}
        </footer>
      </aside>

      <main className="director-desk">
        <header className="desk-header">
          <div>
            <h1>导演台</h1>
          </div>
          <span className="environment-badge">本地开发</span>
        </header>
        <section
          ref={conversationRef}
          className={`conversation ${messages.length > 0 ? 'has-messages' : ''}`}
          aria-label="导演工作区"
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
              <p>正在加载对话…</p>
            </div>
          ) : messages.length > 0 ? (
            <div className="conversation-thread">
              {messages.map((message) => (
                <article className={`chat-message ${message.role}`} key={message.id}>
                  <header>
                    <strong>{message.role === 'user' ? '导演' : '执行副导演'}</strong>
                    <span className="chat-message-meta">
                      <span>{message.isStreaming ? '执行中' : message.model}</span>
                      {message.role === 'user' && !message.id.startsWith('pending-') && (
                        <button
                          className="chat-retry-button"
                          type="button"
                          disabled={sendingMessage}
                          onClick={() => void retryDirectorMessage(message)}
                          title="从这里重试"
                          aria-label="删除此条及后续对话并重试"
                        >
                          <RotateCcw size={13} aria-hidden="true" />
                        </button>
                      )}
                    </span>
                  </header>
                  {message.processEvents && message.processEvents.length > 0 && (
                    <ol className="process-trace" aria-label="执行过程">
                      {message.processEvents.map((processEvent, index) => (
                        <li className={processEvent.stage === 'error' ? 'error' : undefined} key={`${processEvent.stage}-${index}`}>
                          <span className="process-trace-dot" aria-hidden="true" />
                          <span>
                            {processEvent.message}
                            {processEvent.data?.imagePrompt && (
                              <span className="process-image-prompt">
                                <strong>实际提示词</strong>
                                <pre>{processEvent.data.imagePrompt}</pre>
                              </span>
                            )}
                          </span>
                        </li>
                      ))}
                      {message.isStreaming && (
                        <li className="active">
                          <LoaderCircle className="spin" size={12} aria-hidden="true" />
                          <span>等待下一步回执</span>
                        </li>
                      )}
                    </ol>
                  )}
                  {message.content ? <p>{message.content}</p> : message.isStreaming ? (
                    <p className="stream-placeholder">执行副导演正在处理…</p>
                  ) : null}
                  {message.generatedAssets?.map((asset) => (
                      <button
                        className={`chat-generated-asset ${asset.contentType.startsWith('image/') ? 'image' : 'document'}`}
                        type="button"
                        key={asset.id}
                        onClick={() => openGeneratedAsset(asset)}
                        aria-label={`${asset.contentType.startsWith('image/') ? '预览图片' : '审阅资源'}：${asset.name}`}
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
              <h2>{agentStatus?.configured ? '等待导演指令' : 'Azure AI Foundry 未配置'}</h2>
              <p>
                {agentStatus?.configured
                  ? '执行副导演已就绪。'
                  : '请填写项目根目录 .env 后重启 API。'}
              </p>
            </div>
          )}
          {conversationError && <p className="conversation-error">{conversationError}</p>}
        </section>
        <form className="composer" onSubmit={submitDirectorOrder}>
          <div className="composer-box">
            <textarea
              id="director-order"
              aria-label="导演令"
              placeholder="告诉执行副导演现在要做什么…"
              rows={4}
              value={directorOrder}
              onChange={(event) => setDirectorOrder(event.target.value)}
            />
            {attachments.length > 0 && (
              <div className="attachment-list" aria-label="已添加附件">
                {attachments.map((attachment, index) => (
                  <span className="attachment-chip" key={`${attachment.name}-${attachment.lastModified}`}>
                    <Paperclip size={13} aria-hidden="true" />
                    <span>{attachment.name}</span>
                    <button
                      type="button"
                      aria-label={`移除 ${attachment.name}`}
                      title="移除附件"
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
                <label className="attachment-button" title="添加附件">
                  <Paperclip size={18} aria-hidden="true" />
                  <span className="sr-only">添加附件</span>
                  <input
                    type="file"
                    multiple
                    aria-label="添加附件"
                    onChange={(event) => setAttachments(Array.from(event.target.files ?? []))}
                  />
                </label>
                <select
                  aria-label="选择模型"
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
                type="submit"
                aria-label="发送导演令"
                title="发送导演令"
                disabled={
                  sendingMessage
                  || !agentStatus?.configured
                  || (!directorOrder.trim() && attachments.length === 0)
                }
              >
                {sendingMessage
                  ? <LoaderCircle className="spin" size={18} aria-hidden="true" />
                  : <Send size={18} aria-hidden="true" />}
              </button>
            </div>
          </div>
        </form>
      </main>

      <aside className="context-panel">
        <header>
          <h2>{selectedSkillRun
            ? '剧本拆解结果'
            : selectedSkill?.title ?? selectedAsset?.name ?? '当前审阅'}</h2>
        </header>
        {selectedSkillRun && selectedAnalysis ? (
          <div className="skill-result">
            <div className="analysis-counts">
              <span><strong>{selectedAnalysis.characters.length}</strong>人物</span>
              <span><strong>{selectedAnalysis.locations.length}</strong>场景</span>
              <span><strong>{selectedAnalysis.props.length}</strong>道具</span>
              <span><strong>{selectedAnalysis.scenes.length}</strong>场次</span>
            </div>
            <section>
              <h3>人物</h3>
              <p>{selectedAnalysis.characters.map((item) => item.name).join('、') || '未识别'}</p>
            </section>
            <section>
              <h3>场景</h3>
              <p>{selectedAnalysis.locations.map((item) => item.name).join('、') || '未识别'}</p>
            </section>
            <section>
              <h3>道具</h3>
              <p>{selectedAnalysis.props.map((item) => item.name).join('、') || '未识别'}</p>
            </section>
            <section>
              <h3>每场发生的事情</h3>
              <div className="analysis-scenes">
                {selectedAnalysis.scenes.map((scene, index) => (
                  <article key={`${scene.heading}-${index}`}>
                    <strong>{scene.heading || `场次 ${index + 1}`}</strong>
                    <small>{[scene.time, scene.location].filter(Boolean).join(' · ')}</small>
                    <p>{scene.summary}</p>
                  </article>
                ))}
              </div>
            </section>
            <section className={selectedAnalysis.ambiguities.length > 0 ? 'analysis-ambiguities' : ''}>
              <h3>待导演确认</h3>
              {selectedAnalysis.ambiguities.length > 0
                ? <ul>{selectedAnalysis.ambiguities.map((item) => <li key={item}>{item}</li>)}</ul>
                : <p>没有发现需要确认的歧义。</p>}
            </section>
          </div>
        ) : selectedSkillRun ? (
          <div className="empty-review review-error">
            <WandSparkles size={24} aria-hidden="true" />
            <p>{selectedSkillRun.error ?? '技能结果无法解析'}</p>
          </div>
        ) : selectedSkill ? (
          <div className="skill-preview">
            <div className="skill-preview-summary">
              <span className={selectedSkill.isEnabled ? 'enabled' : 'disabled'}>
                {selectedSkill.isEnabled ? '已启用' : '已停用'}
              </span>
              <p>{selectedSkill.description}</p>
            </div>
            <pre>{selectedSkill.content || '此技能没有可显示的 SKILL.md 内容。'}</pre>
          </div>
        ) : assetPreviewLoading ? (
          <div className="empty-review">
            <LoaderCircle className="spin" size={24} aria-hidden="true" />
            <p>正在加载内容…</p>
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
                    aria-label="资源版本"
                    value={selectedAsset.id}
                    onChange={(event) => {
                      const version = assetVersions.find((item) => item.id === event.target.value)
                      if (version) void reviewAsset(version)
                    }}
                  >
                    {assetVersions.map((version) => (
                      <option key={version.id} value={version.id}>
                        v{version.version} · {formatDate(version.createdAtUtc)}
                      </option>
                    ))}
                  </select>
                )}
                <small>{formatFileSize(selectedAsset.sizeBytes)}</small>
              </div>
            </div>
            {selectedAsset.type === 'shot' && (
              <section className="shot-linked-assets" aria-labelledby="shot-linked-assets-title">
                <header>
                  <h3 id="shot-linked-assets-title">镜头素材</h3>
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
                            aria-label={`全屏预览${link.asset.name}`}
                          >
                            <img src={link.asset.contentUrl} alt={link.asset.name} />
                          </button>
                        ) : link.asset.contentType.startsWith('video/') ? (
                          <button
                            type="button"
                            className="shot-linked-media"
                            onClick={() => openImagePreview(link.asset)}
                            aria-label={`播放预览${link.asset.name}`}
                          >
                            <video src={link.asset.contentUrl} preload="metadata" muted />
                          </button>
                        ) : link.asset.contentType.startsWith('audio/') ? (
                          <audio src={link.asset.contentUrl} controls preload="metadata" />
                        ) : null}
                        <div>
                          <span>{({
                            'first-frame': '首帧',
                            'last-frame': '尾帧',
                            reference: '关键帧 / 参考',
                            video: '镜头视频',
                            other: '其他',
                          } as const)[link.role]}</span>
                          <strong>{link.asset.name}</strong>
                        </div>
                      </article>
                    ))}
                  </div>
                ) : (
                  <p>暂无关联素材。生成首帧、尾帧或视频后，Agent 会通过技能绑定到这里。</p>
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
                aria-label={`全屏预览图片：${selectedAsset.name}`}
                title="全屏预览"
              >
                <img src={selectedAsset.contentUrl} alt={selectedAsset.name} />
                <span><Maximize2 size={16} aria-hidden="true" />全屏预览</span>
              </button>
            ) : selectedAsset.contentType.startsWith('video/') ? (
              <video src={selectedAsset.contentUrl} controls />
            ) : selectedAsset.contentType.startsWith('audio/') ? (
              <audio src={selectedAsset.contentUrl} controls />
            ) : selectedAsset.contentType === 'application/pdf' ? (
              <object data={selectedAsset.contentUrl} type="application/pdf">
                <p>浏览器无法预览此 PDF。</p>
              </object>
            ) : (
              <div className="unsupported-preview">
                <FileText size={28} aria-hidden="true" />
                <p>暂不支持预览此文件格式</p>
              </div>
            )}
          </div>
        ) : (
          <div className="empty-review">
            <span>00:00:00</span>
            <p>暂无待审内容</p>
          </div>
        )}
        <div className={`asset-detail ${assetDetailExpanded ? '' : 'collapsed'}`}>
          <button
            className="asset-detail-toggle"
            type="button"
            aria-expanded={assetDetailExpanded}
            aria-controls="asset-detail-content"
            title={assetDetailExpanded ? '收起资源信息' : '展开资源信息'}
            onClick={() => setAssetDetailExpanded((expanded) => !expanded)}
          >
            <span>{selectedSkillRun ? '技能运行信息' : selectedSkill ? '技能信息' : '当前资源信息'}</span>
            {assetDetailExpanded
              ? <ChevronDown size={16} aria-hidden="true" />
              : <ChevronUp size={16} aria-hidden="true" />}
          </button>
          <div className="asset-detail-body" id="asset-detail-content">
            {selectedSkillRun ? (
              <dl>
                <div><dt>技能</dt><dd>{skills.find((skill) => skill.id === selectedSkillRun.skillId)?.name ?? selectedSkillRun.skillId}</dd></div>
                <div><dt>状态</dt><dd>{selectedSkillRun.status}</dd></div>
                <div><dt>模型</dt><dd>{selectedSkillRun.model}</dd></div>
                <div><dt>输入资源</dt><dd>{selectedAsset?.fileName ?? selectedSkillRun.inputAssetId}</dd></div>
                <div><dt>开始时间</dt><dd>{formatDate(selectedSkillRun.startedAtUtc)}</dd></div>
                <div><dt>运行 ID</dt><dd>{selectedSkillRun.id}</dd></div>
              </dl>
            ) : selectedSkill ? (
              <dl>
                <div><dt>名称</dt><dd>{selectedSkill.name}</dd></div>
                <div><dt>版本</dt><dd>v{selectedSkill.version}</dd></div>
                <div><dt>类型</dt><dd>{selectedSkill.isSystem ? '系统技能' : '项目技能'}</dd></div>
                <div><dt>状态</dt><dd>{selectedSkill.isEnabled ? '已启用' : '已停用'}</dd></div>
                <div><dt>允许工具</dt><dd>{selectedSkill.allowedTools.join('、') || '无'}</dd></div>
              </dl>
            ) : selectedAsset ? (
              <dl>
                <div><dt>名称</dt><dd>{selectedAsset.name}</dd></div>
                <div><dt>版本</dt><dd>v{selectedAsset.version} / {selectedAsset.versionCount}</dd></div>
                <div><dt>分类</dt><dd>{selectedAssetSection.label}</dd></div>
                <div><dt>文件名</dt><dd>{selectedAsset.fileName}</dd></div>
                <div><dt>格式</dt><dd>{selectedAsset.contentType}</dd></div>
                <div><dt>大小</dt><dd>{formatFileSize(selectedAsset.sizeBytes)}</dd></div>
                <div><dt>添加时间</dt><dd>{formatDate(selectedAsset.createdAtUtc)}</dd></div>
                <div><dt>资源 ID</dt><dd>{selectedAsset.id}</dd></div>
                {(() => {
                  const metadata = parseImageGenerationMetadata(selectedAsset.generationMetadataJson)
                  if (!selectedAsset.contentType.startsWith('image/') || !metadata) return null
                  return (
                    <>
                      <div><dt>生成方式</dt><dd>{formatImageOperation(metadata.operation)}</dd></div>
                      <div><dt>模型</dt><dd>{metadata.model}</dd></div>
                      <div>
                        <dt>调用参数</dt>
                        <dd>{metadata.parameters.size} · {metadata.parameters.quality} · {metadata.parameters.outputFormat} · {metadata.parameters.count} 张{metadata.parameters.apiVersion ? ` · ${metadata.parameters.apiVersion}` : ''}</dd>
                      </div>
                      {metadata.prompt && (
                        <div className="generation-detail-row">
                          <dt>提示词</dt><dd><pre>{metadata.prompt}</pre></dd>
                        </div>
                      )}
                      {metadata.revisedPrompt && metadata.revisedPrompt !== metadata.prompt && (
                        <div className="generation-detail-row">
                          <dt>修订提示词</dt><dd><pre>{metadata.revisedPrompt}</pre></dd>
                        </div>
                      )}
                      {metadata.sources.length > 0 && (
                        <div className="generation-detail-row">
                          <dt>来源图片</dt>
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
              </dl>
            ) : (
              <p className="asset-detail-empty">选择一个资源后查看详细信息</p>
            )}
          </div>
        </div>
      </aside>

      {previewImage && (
        <div
          className="image-lightbox"
          role="dialog"
          aria-modal="true"
          aria-label={`媒体预览：${previewImage.name}`}
        >
          <header className="image-lightbox-header">
            <div>
              <strong>{previewImage.name}</strong>
              {previewImage.contentType.startsWith('image/') && <span>{Math.round(previewZoom * 100)}%</span>}
            </div>
            <div className="image-lightbox-tools">
              {previewImage.contentType.startsWith('image/') && (
                <>
                  <button type="button" title="缩小" aria-label="缩小" onClick={() => changePreviewZoom(previewZoom - 0.25)}>
                    <Minus size={18} aria-hidden="true" />
                  </button>
                  <button type="button" title="适应窗口" aria-label="适应窗口" onClick={resetImagePreview}>
                    <Maximize2 size={18} aria-hidden="true" />
                  </button>
                  <button type="button" title="放大" aria-label="放大" onClick={() => changePreviewZoom(previewZoom + 0.25)}>
                    <Plus size={18} aria-hidden="true" />
                  </button>
                </>
              )}
              <a href={previewImage.contentUrl} download={previewImage.fileName} title="下载文件" aria-label="下载文件">
                <Download size={18} aria-hidden="true" />
              </a>
              <button type="button" title="关闭" aria-label="关闭媒体预览" onClick={closeImagePreview}>
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

      <nav className="mobile-panel-switcher" aria-label="手机屏幕切换">
        <button
          type="button"
          aria-label={`切换到${mobilePanels[Math.max(0, mobilePanelIndex - 1)].label}`}
          title="上一个屏幕"
          disabled={mobilePanelIndex === 0}
          onClick={() => setMobilePanel(mobilePanels[mobilePanelIndex - 1].id)}
        >
          <ChevronLeft size={21} aria-hidden="true" />
        </button>
        <span className="sr-only" aria-live="polite">当前屏幕：{mobilePanels[mobilePanelIndex].label}</span>
        <button
          type="button"
          aria-label={`切换到${mobilePanels[Math.min(mobilePanels.length - 1, mobilePanelIndex + 1)].label}`}
          title="下一个屏幕"
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
