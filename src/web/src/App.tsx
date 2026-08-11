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
  Server,
  Settings2,
  Upload,
  Users,
  Video,
  WandSparkles,
  X,
  type LucideIcon,
} from 'lucide-react'
import { Navigate, matchPath, useLocation, useNavigate } from 'react-router-dom'
import './App.css'

type ServiceState = 'checking' | 'online' | 'offline'
type MobilePanel = 'assets' | 'director' | 'review'

interface AgentStatus {
  framework: string
  frameworkVersion: string | null
  deployment: string
  configured: boolean
  imageDeployment: string
  imageQuality: string
  imageConfigured: boolean
}

interface Project {
  id: string
  name: string
  description: string
  createdAt: string
  formatPreset: string
  outputWidth: number
  outputHeight: number
  previewResolution: string
  languageModel: string
  imageModel: string
  videoModel: string
}

interface ProjectRuntimeConfiguration {
  projectId: string
  vmHost: string
  vmPort: number
  vmUsername: string
  sshPrivateKeyPath: string
  comfyUiPath: string
  comfyUiPythonPath: string
  comfyUiPort: number
  localProxyPort: number
  workflowDirectory: string
  outputDirectory: string
  updatedAtUtc: string
}

interface GlobalFoundryConfiguration {
  openAiEndpoint: string
  openAiDeployment: string
  openAiApiKeyConfigured: boolean
  imageEndpoint: string
  imageDeployment: string
  imageApiVersion: string
  imageQuality: 'low' | 'medium' | 'high'
  imageApiKeyConfigured: boolean
  updatedAtUtc: string
}

interface ConversationMessageRecord {
  id: string
  projectId: string
  role: 'user' | 'assistant'
  content: string
  model: string
  createdAtUtc: string
  processEvents?: ProcessEventRecord[]
  generatedAssets?: AssetRecord[]
  isStreaming?: boolean
}

interface ProcessEventRecord {
  stage: string
  message: string
}

interface MessageStreamEvent {
  type: 'message.accepted' | 'process' | 'assistant.delta' | 'completed' | 'error'
  stage?: string
  message?: string
  detail?: string
  delta?: string
  userMessage?: ConversationMessageRecord
  assistantMessage?: ConversationMessageRecord
  skillRun?: SkillRunRecord
  outputAsset?: AssetRecord
  generatedAssets?: AssetRecord[]
  updatedAsset?: AssetRecord
}

interface AssetRecord {
  id: string
  resourceId: string
  version: number
  versionCount: number
  projectId: string
  type: string
  name: string
  fileName: string
  contentType: string
  sizeBytes: number
  createdAtUtc: string
  contentUrl: string
}

interface ShotAssetLinkRecord {
  id: string
  role: 'first-frame' | 'last-frame' | 'reference' | 'video' | 'other'
  createdAtUtc: string
  asset: AssetRecord
}

interface SkillDefinitionRecord {
  id: string
  name: string
  description: string
  version: string
  isEnabled: boolean
  isSystem: boolean
  title: string
  allowedTools: string[]
  content: string
}

interface ScriptEntityRecord {
  name: string
  description: string
  evidence: string[]
}

interface ScriptSceneRecord {
  heading: string
  time: string
  location: string
  summary: string
  characters: string[]
  props: string[]
  evidence: string[]
}

interface ScriptAnalysisRecord {
  characters: ScriptEntityRecord[]
  locations: ScriptEntityRecord[]
  props: ScriptEntityRecord[]
  scenes: ScriptSceneRecord[]
  ambiguities: string[]
}

interface SkillRunRecord {
  id: string
  projectId: string
  skillId: string
  inputAssetId: string
  outputAssetId: string | null
  status: string
  directorInstruction: string
  model: string
  resultJson: string | null
  error: string | null
  startedAtUtc: string
  completedAtUtc: string | null
}

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

const assetSections: Array<{
  id: string
  type: string
  label: string
  accept: string
  icon: LucideIcon
  contentTypePrefix?: string
}> = [
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

async function upsertProject(project: Project, signal?: AbortSignal): Promise<Project> {
  const response = await fetch(`/api/projects/${project.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(project),
    signal,
  })
  if (!response.ok) throw new Error('项目保存失败')
  return response.json() as Promise<Project>
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
  const [messages, setMessages] = useState<ConversationMessageRecord[]>([])
  const [messagesLoading, setMessagesLoading] = useState(false)
  const [sendingMessage, setSendingMessage] = useState(false)
  const [conversationError, setConversationError] = useState<string | null>(null)
  const [assets, setAssets] = useState<AssetRecord[]>([])
  const [assetsLoading, setAssetsLoading] = useState(false)
  const [uploadingAssets, setUploadingAssets] = useState(false)
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
        const response = await fetch('/api/projects', { signal: controller.signal })
        if (!response.ok) throw new Error('项目列表加载失败')

        if (localProjects.length > 0) {
          await Promise.all(localProjects.map((project) => upsertProject(project, controller.signal)))
          localStorage.removeItem(projectStorageKey)
        }

        const refreshedResponse = localProjects.length > 0
          ? await fetch('/api/projects', { signal: controller.signal })
          : response
        if (!refreshedResponse.ok) throw new Error('项目列表加载失败')
        setProjects(await refreshedResponse.json() as Project[])
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

    fetch(
      `/api/projects/${selectedProject.id}/assets`,
      { signal: controller.signal },
    )
      .then((response) => {
        if (!response.ok) throw new Error('资产列表加载失败')
        return response.json() as Promise<AssetRecord[]>
      })
      .then(setAssets)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setAssetError(error instanceof Error ? error.message : '资产列表加载失败')
      })
      .finally(() => {
        if (!controller.signal.aborted) setAssetsLoading(false)
      })

    fetch('/api/system/runtime-configuration', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error('VM 配置加载失败')
        return response.json() as Promise<ProjectRuntimeConfiguration>
      })
      .then((configuration) => {
        setRuntimeConfiguration(configuration.vmHost
          ? configuration
          : defaultRuntimeConfiguration)
        setRuntimeConfigurationState('idle')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setRuntimeConfigurationState('error')
      })

    fetch('/api/system/foundry-configuration', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error('Azure Foundry 配置加载失败')
        return response.json() as Promise<GlobalFoundryConfiguration>
      })
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
  }, [selectedProject?.id])

  useEffect(() => {
    if (!selectedProject) return

    const controller = new AbortController()
    let refreshing = false
    const refreshAssets = async () => {
      if (refreshing || document.visibilityState !== 'visible') return
      refreshing = true
      try {
        const response = await fetch(`/api/projects/${selectedProject.id}/assets`, {
          signal: controller.signal,
        })
        if (!response.ok) return
        setAssets(await response.json() as AssetRecord[])
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
    if (!selectedAsset) return

    const latestAsset = assets.find((asset) => asset.resourceId === selectedAsset.resourceId)
    if (!latestAsset || latestAsset.id === selectedAsset.id) return

    setSelectedAsset(latestAsset)
    setAssetVersions([latestAsset])
    void fetch(`/api/assets/${latestAsset.id}/versions`)
      .then((response) => {
        if (!response.ok) throw new Error('资源版本加载失败')
        return response.json() as Promise<AssetRecord[]>
      })
      .then(setAssetVersions)
      .catch((error: unknown) => {
        setAssetPreviewError(error instanceof Error ? error.message : '资源版本加载失败')
      })
  }, [assets, selectedAsset])

  useEffect(() => {
    if (!selectedProject) return

    const controller = new AbortController()
    scrollToLatestAfterLoadRef.current = true
    setMessages([])
    setMessagesLoading(true)
    setConversationError(null)

    fetch(`/api/projects/${selectedProject.id}/messages`, { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error('对话历史加载失败')
        return response.json() as Promise<ConversationMessageRecord[]>
      })
      .then(setMessages)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setConversationError(error instanceof Error ? error.message : '对话历史加载失败')
      })
      .finally(() => {
        if (!controller.signal.aborted) setMessagesLoading(false)
      })

    return () => controller.abort()
  }, [selectedProject])

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
    fetch('/api/skills', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error('技能列表加载失败')
        return response.json() as Promise<SkillDefinitionRecord[]>
      })
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
      const response = await fetch('/api/system/runtime-configuration', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(runtimeConfiguration),
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(problem?.error ?? 'VM 配置保存失败')
      }
      setRuntimeConfiguration(await response.json() as ProjectRuntimeConfiguration)
      setRuntimeConfigurationState('saved')
    } catch {
      setRuntimeConfigurationState('error')
    }
  }

  async function saveFoundryConfiguration() {
    setFoundryConfigurationState('saving')
    try {
      const response = await fetch('/api/system/foundry-configuration', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...foundryConfiguration,
          openAiApiKey: openAiApiKey || null,
          imageApiKey: imageApiKey || null,
          clearOpenAiApiKey: false,
          clearImageApiKey: false,
        }),
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(problem?.error ?? 'Azure Foundry 配置保存失败')
      }
      setFoundryConfiguration(await response.json() as GlobalFoundryConfiguration)
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

  async function retryDirectorMessage(message: ConversationMessageRecord) {
    if (!selectedProject || sendingMessage || message.role !== 'user') return

    setConversationError(null)
    try {
      const response = await fetch(
        `/api/projects/${selectedProject.id}/messages/${message.id}/following`,
        { method: 'DELETE' },
      )
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(problem?.error ?? '无法清理后续对话')
      }

      setMessages((current) => current.slice(0, current.findIndex((item) => item.id === message.id)))
      await sendDirectorMessage(message.content, message.model)
    } catch (error) {
      setConversationError(error instanceof Error ? error.message : '重试失败')
    }
  }

  async function sendDirectorMessage(message: string, model: string, assetId?: string) {
    if (!selectedProject || !agentStatus?.configured) return

    const sourceShot = selectedAsset?.type === 'shot' ? selectedAsset : null
    stickToLatestRef.current = true
    setSendingMessage(true)
    setConversationError(null)
    const now = new Date().toISOString()
    const temporaryUserId = `pending-user-${crypto.randomUUID()}`
    const temporaryAssistantId = `pending-assistant-${crypto.randomUUID()}`
    const temporaryUser: ConversationMessageRecord = {
      id: temporaryUserId,
      projectId: selectedProject.id,
      role: 'user',
      content: message,
      model,
      createdAtUtc: now,
    }
    const temporaryAssistant: ConversationMessageRecord = {
      id: temporaryAssistantId,
      projectId: selectedProject.id,
      role: 'assistant',
      content: '',
      model,
      createdAtUtc: now,
      processEvents: [],
      isStreaming: true,
    }
    setMessages((current) => [...current, temporaryUser, temporaryAssistant])

    try {
      const projectFormat = getProjectFormat(selectedProject)
      const response = await fetch(`/api/projects/${selectedProject.id}/messages/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message,
          model,
          assetId,
          projectAspectRatio: projectFormat.aspectRatio,
          projectResolution: projectFormat.resolution,
          imageSize: projectFormat.imageSize,
          projectName: selectedProject.name,
          projectDescription: selectedProject.description,
          previewResolution: selectedProject.previewResolution,
          imageModel: selectedProject.imageModel,
          videoModel: selectedProject.videoModel,
        }),
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { detail?: string } | null
        throw new Error(problem?.detail ?? '执行副导演响应失败')
      }
      if (!response.body) throw new Error('浏览器未提供流式响应')

      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      let completed = false

      const consumeEvent = (streamEvent: MessageStreamEvent) => {
        if (streamEvent.type === 'process' || streamEvent.type === 'message.accepted') {
          setMessages((current) => current.map((item) => item.id === temporaryAssistantId
            ? {
                ...item,
                processEvents: [
                  ...(item.processEvents ?? []),
                  {
                    stage: streamEvent.stage ?? streamEvent.type,
                    message: streamEvent.message ?? '处理中',
                  },
                ],
              }
            : item))
        } else if (streamEvent.type === 'assistant.delta' && streamEvent.delta) {
          setMessages((current) => current.map((item) => item.id === temporaryAssistantId
            ? { ...item, content: item.content + streamEvent.delta }
            : item))
        } else if (
          streamEvent.type === 'completed'
          && streamEvent.userMessage
          && streamEvent.assistantMessage
        ) {
          completed = true
          setMessages((current) => current.map((item) => {
            if (item.id === temporaryUserId) return streamEvent.userMessage!
            if (item.id === temporaryAssistantId) {
              return {
                ...streamEvent.assistantMessage!,
                processEvents: item.processEvents,
                generatedAssets: streamEvent.generatedAssets,
                isStreaming: false,
              }
            }
            return item
          }))
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
        } else if (streamEvent.type === 'error') {
          throw new Error(streamEvent.detail ?? streamEvent.message ?? '执行副导演响应失败')
        }
      }

      while (true) {
        const { value, done } = await reader.read()
        buffer += decoder.decode(value, { stream: !done })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        for (const line of lines) {
          if (line.trim()) consumeEvent(JSON.parse(line) as MessageStreamEvent)
        }
        if (done) break
      }
      if (buffer.trim()) consumeEvent(JSON.parse(buffer) as MessageStreamEvent)
      if (!completed) throw new Error('流式响应在完成前中断')
    } catch (error) {
      const message = error instanceof Error ? error.message : '执行副导演响应失败'
      setConversationError(message)
      setMessages((current) => current.map((item) => item.id === temporaryAssistantId
        ? {
            ...item,
            content: item.content || '本次执行未完成。',
            isStreaming: false,
            processEvents: [
              ...(item.processEvents ?? []),
              { stage: 'error', message },
            ],
          }
        : item))
    } finally {
      setSendingMessage(false)
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
        files.map(async (file) => {
          const form = new FormData()
          form.append('file', file)
          form.append('type', section.type)

          const response = await fetch(`/api/projects/${selectedProject.id}/assets`, {
            method: 'POST',
            body: form,
          })
          if (!response.ok) throw new Error(`上传 ${file.name} 失败`)
          return response.json() as Promise<AssetRecord>
        }),
      )

      setAssets((current) => [...uploadedAssets.reverse(), ...current])
    } catch (error) {
      setAssetError(error instanceof Error ? error.message : '资产上传失败')
    } finally {
      setUploadingAssets(false)
    }
  }

  async function reviewAsset(asset: AssetRecord) {
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

    void fetch(`/api/assets/${asset.id}/versions`)
      .then((response) => {
        if (!response.ok) throw new Error('资源版本加载失败')
        return response.json() as Promise<AssetRecord[]>
      })
      .then(setAssetVersions)
      .catch((error: unknown) => {
        setAssetPreviewError(error instanceof Error ? error.message : '资源版本加载失败')
      })

    if (asset.type === 'shot') {
      void fetch(`/api/assets/${asset.id}/linked-assets`)
        .then((response) => {
          if (!response.ok) throw new Error('镜头素材加载失败')
          return response.json() as Promise<ShotAssetLinkRecord[]>
        })
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
      const response = await fetch(`/api/skills/${skill.id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isEnabled }),
      })
      if (!response.ok) throw new Error('技能状态更新失败')
      const updated = await response.json() as SkillDefinitionRecord
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
          <section className="assets-panel" aria-labelledby="assets-title">
            <header className="assets-header">
              <div>
                <p className="section-label">当前项目</p>
                <p className="current-project-name">{selectedProject.name}</p>
              </div>
              <button className="switch-project" type="button" onClick={() => navigate('/')}>
                切换
              </button>
            </header>
            <div className="assets-title-row">
              <div>
                <p className="section-label">ASSETS</p>
                <h2 id="assets-title">{isGroupedAssetSection ? '资产' : selectedAssetSection.label}</h2>
              </div>
              <div className="assets-actions">
                <span className="asset-count">
                  {isGroupedAssetSection ? groupedAssetCount : visibleAssets.length}
                </span>
                {!isGroupedAssetSection && (
                  <label className="asset-upload-button" title={`上传${selectedAssetSection.label}`}>
                    <Upload size={14} aria-hidden="true" />
                    <span className="sr-only">上传{selectedAssetSection.label}</span>
                    <input
                      type="file"
                      multiple
                      accept={selectedAssetSection.accept}
                      aria-label={`上传${selectedAssetSection.label}`}
                      disabled={uploadingAssets}
                      onChange={(event) => uploadAssets(event, selectedAssetSection)}
                    />
                  </label>
                )}
              </div>
            </div>
            {assetError && <p className="asset-error">{assetError}</p>}
            {assetsLoading || uploadingAssets ? (
              <div className="asset-empty">
                <Upload size={22} strokeWidth={1.4} aria-hidden="true" />
                <p>{uploadingAssets ? '正在上传…' : '正在加载…'}</p>
              </div>
            ) : (
              <div className="asset-groups">
                {(isGroupedAssetSection ? groupedAssetSections : [selectedAssetSection]).map((section) => {
                  const Icon = section.icon
                  const sectionAssets = assets
                    .filter((asset) => asset.type === section.type
                      && (!section.contentTypePrefix || asset.contentType.startsWith(section.contentTypePrefix)))
                    .sort((left, right) => section.type === 'shot'
                      ? left.name.localeCompare(right.name, 'zh-CN')
                      : new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime())
                  const isExpanded = expandedAssetSections.has(section.id)

                  if (!isGroupedAssetSection) {
                    return sectionAssets.length > 0 ? (
                      <div className="asset-list standalone-asset-list" key={section.id}>
                        {sectionAssets.map((asset) => (
                          <button
                            type="button"
                            className={`asset-row ${selectedAsset?.id === asset.id ? 'active' : ''}`}
                            key={asset.id}
                            title={`审阅 ${asset.fileName}`}
                            aria-pressed={selectedAsset?.id === asset.id}
                            onClick={() => reviewAsset(asset)}
                          >
                            {asset.contentType.startsWith('image/') ? (
                              <img
                                className="asset-row-thumbnail"
                                src={asset.contentUrl}
                                alt=""
                                loading="lazy"
                              />
                            ) : (
                              <Icon size={17} strokeWidth={1.5} aria-hidden="true" />
                            )}
                            <span>
                              <strong>{asset.name}</strong>
                              <small>
                                {asset.versionCount > 1
                                  ? `当前 v${asset.version} · 共 ${asset.versionCount} 版 · `
                                  : ''}
                                {formatFileSize(asset.sizeBytes)}
                              </small>
                            </span>
                          </button>
                        ))}
                      </div>
                    ) : (
                      <div className="asset-empty" key={section.id}>
                        <Icon size={22} strokeWidth={1.4} aria-hidden="true" />
                        <p>暂无{section.label}</p>
                      </div>
                    )
                  }

                  return (
                    <section className="asset-group" key={section.id}>
                      <div className="asset-group-header">
                        <button
                          type="button"
                          className={activeAssetSection === section.id ? 'active' : undefined}
                          aria-expanded={isExpanded}
                          onClick={() => {
                            setActiveAssetSection(section.id)
                            setExpandedAssetSections((current) => {
                              const next = new Set(current)
                              if (next.has(section.id)) next.delete(section.id)
                              else next.add(section.id)
                              return next
                            })
                          }}
                        >
                          <Icon size={16} strokeWidth={1.6} aria-hidden="true" />
                          <strong>{section.label}</strong>
                          <span>{sectionAssets.length}</span>
                          {isExpanded
                            ? <ChevronUp size={14} aria-hidden="true" />
                            : <ChevronDown size={14} aria-hidden="true" />}
                        </button>
                        <label className="asset-upload-button" title={`上传${section.label}`}>
                          <Upload size={14} aria-hidden="true" />
                          <span className="sr-only">上传{section.label}</span>
                          <input
                            type="file"
                            multiple
                            accept={section.accept}
                            aria-label={`上传${section.label}`}
                            disabled={uploadingAssets}
                            onChange={(event) => uploadAssets(event, section)}
                          />
                        </label>
                      </div>
                      {isExpanded && (sectionAssets.length > 0 ? (
                        <div className="asset-list">
                          {sectionAssets.map((asset) => (
                            <button
                              type="button"
                              className={`asset-row ${selectedAsset?.id === asset.id ? 'active' : ''}`}
                              key={asset.id}
                              title={`审阅 ${asset.fileName}`}
                              aria-pressed={selectedAsset?.id === asset.id}
                              onClick={() => reviewAsset(asset)}
                            >
                              {asset.contentType.startsWith('image/') ? (
                                <img
                                  className="asset-row-thumbnail"
                                  src={asset.contentUrl}
                                  alt=""
                                  loading="lazy"
                                />
                              ) : (
                                <Icon size={17} strokeWidth={1.5} aria-hidden="true" />
                              )}
                              <span>
                                <strong>{asset.name}</strong>
                                <small>
                                  {asset.versionCount > 1
                                    ? `当前 v${asset.version} · 共 ${asset.versionCount} 版 · `
                                    : ''}
                                  {formatFileSize(asset.sizeBytes)}
                                </small>
                              </span>
                            </button>
                          ))}
                        </div>
                      ) : (
                        <p className="asset-group-empty">暂无{section.label}</p>
                      ))}
                    </section>
                  )
                })}
              </div>
            )}
          </section>
          ) : (
          <section className="assets-panel project-settings-panel system-configuration-panel" aria-labelledby="system-configuration-title">
            <header className="assets-header">
              <div>
                <p className="section-label">GLOBAL</p>
                <h2 id="system-configuration-title">系统配置</h2>
              </div>
            </header>
            <div className="project-settings-form">
              <section className="project-settings-group">
                <h3>Azure AI Foundry</h3>
                <label htmlFor="system-openai-endpoint">语言模型 Endpoint</label>
                <input id="system-openai-endpoint" value={foundryConfiguration.openAiEndpoint} placeholder="https://资源名.openai.azure.com" onChange={(event) => setFoundryConfiguration((current) => ({ ...current, openAiEndpoint: event.target.value }))} />
                <label htmlFor="system-openai-deployment">语言模型部署</label>
                <input id="system-openai-deployment" value={foundryConfiguration.openAiDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, openAiDeployment: event.target.value }))} />
                <label htmlFor="system-openai-key">语言模型 API Key</label>
                <input id="system-openai-key" type="password" value={openAiApiKey} autoComplete="new-password" placeholder={foundryConfiguration.openAiApiKeyConfigured ? '已配置，留空则保持不变' : '输入 API Key'} onChange={(event) => setOpenAiApiKey(event.target.value)} />

                <label htmlFor="system-image-endpoint">图片模型 Endpoint</label>
                <input id="system-image-endpoint" value={foundryConfiguration.imageEndpoint} placeholder="留空则复用语言模型 Endpoint" onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageEndpoint: event.target.value }))} />
                <label htmlFor="system-image-deployment">图片模型部署</label>
                <input id="system-image-deployment" value={foundryConfiguration.imageDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageDeployment: event.target.value }))} />
                <label htmlFor="system-image-key">图片模型 API Key</label>
                <input id="system-image-key" type="password" value={imageApiKey} autoComplete="new-password" placeholder={foundryConfiguration.imageApiKeyConfigured ? '已配置，留空则保持不变' : '留空则复用语言模型 API Key'} onChange={(event) => setImageApiKey(event.target.value)} />
                <label htmlFor="system-image-api-version">图片 API 版本</label>
                <input id="system-image-api-version" value={foundryConfiguration.imageApiVersion} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageApiVersion: event.target.value }))} />
                <label htmlFor="system-image-quality">默认图片质量</label>
                <select id="system-image-quality" value={foundryConfiguration.imageQuality} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageQuality: event.target.value as GlobalFoundryConfiguration['imageQuality'] }))}>
                  <option value="low">低</option>
                  <option value="medium">中</option>
                  <option value="high">高</option>
                </select>
                <button className="project-settings-save" type="button" disabled={foundryConfigurationState === 'loading' || foundryConfigurationState === 'saving'} onClick={() => void saveFoundryConfiguration()}>
                  {foundryConfigurationState === 'saving' ? '保存中…' : '保存 Foundry 配置'}
                </button>
                <div className="project-setting-readout">
                  <span>连接状态</span>
                  <strong>{foundryConfigurationState === 'error' ? '保存失败' : agentStatus?.configured ? '语言模型已配置' : '语言模型待配置'}</strong>
                </div>
              </section>

              <section className="project-settings-group">
                <h3><Server size={14} aria-hidden="true" /> VM 与 ComfyUI</h3>
                <label htmlFor="system-vm-host">VM 主机或 IP</label>
                <input id="system-vm-host" value={runtimeConfiguration.vmHost} placeholder="20.0.0.1" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmHost: event.target.value }))} />
                <div className="project-resolution-inputs">
                  <input type="number" min="1" max="65535" value={runtimeConfiguration.vmPort} aria-label="SSH 端口" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmPort: Number(event.target.value) }))} />
                  <span>用户</span>
                  <input value={runtimeConfiguration.vmUsername} aria-label="SSH 用户" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmUsername: event.target.value }))} />
                </div>
                <label htmlFor="system-ssh-key">本机 SSH 私钥路径</label>
                <input id="system-ssh-key" value={runtimeConfiguration.sshPrivateKeyPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, sshPrivateKeyPath: event.target.value }))} />
                <label htmlFor="system-comfy-path">远端 ComfyUI 目录</label>
                <input id="system-comfy-path" value={runtimeConfiguration.comfyUiPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPath: event.target.value }))} />
                <label htmlFor="system-python-path">远端 Python</label>
                <input id="system-python-path" value={runtimeConfiguration.comfyUiPythonPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPythonPath: event.target.value }))} />
                <div className="project-resolution-inputs">
                  <input type="number" min="1" max="65535" value={runtimeConfiguration.comfyUiPort} aria-label="远端 ComfyUI 端口" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPort: Number(event.target.value) }))} />
                  <span>代理到</span>
                  <input type="number" min="1" max="65535" value={runtimeConfiguration.localProxyPort} aria-label="本地代理端口" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, localProxyPort: Number(event.target.value) }))} />
                </div>
                <label htmlFor="system-workflow-path">远端 Workflow 目录</label>
                <input id="system-workflow-path" value={runtimeConfiguration.workflowDirectory} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, workflowDirectory: event.target.value }))} />
                <label htmlFor="system-output-path">远端输出目录</label>
                <input id="system-output-path" value={runtimeConfiguration.outputDirectory} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, outputDirectory: event.target.value }))} />
                <button className="project-settings-save" type="button" disabled={runtimeConfigurationState === 'loading' || runtimeConfigurationState === 'saving'} onClick={() => void saveRuntimeConfiguration()}>
                  {runtimeConfigurationState === 'saving' ? '保存中…' : '保存 VM 配置'}
                </button>
                <div className="project-setting-readout">
                  <span>配置状态</span>
                  <strong>{runtimeConfigurationState === 'error' ? '保存失败' : runtimeConfiguration.vmHost ? runtimeConfigurationState === 'saved' ? '已保存' : '已配置' : '未配置'}</strong>
                </div>
              </section>

              <section className="project-settings-group system-skills-group">
                <div className="system-section-heading">
                  <h3>Agent 技能</h3>
                  <span className="asset-count">{skills.filter((skill) => skill.isEnabled).length}</span>
                </div>
                {skillError && <p className="asset-error">{skillError}</p>}
                {skillsLoading ? (
                  <div className="asset-empty">
                    <LoaderCircle className="spin" size={22} aria-hidden="true" />
                    <p>正在加载技能…</p>
                  </div>
                ) : (
                  <div className="skill-list">
                    {skills.map((skill) => (
                      <article className={`skill-card ${selectedSkill?.id === skill.id ? 'active' : ''}`} key={skill.id}>
                        <header>
                          <button className="skill-card-select" type="button" aria-pressed={selectedSkill?.id === skill.id} onClick={() => {
                            setSelectedSkill(skill)
                            setSelectedSkillRun(null)
                            setMobilePanel('review')
                          }}>
                            <strong>{skill.title}</strong>
                            <small>{skill.name} · v{skill.version}</small>
                            <p>{skill.description}</p>
                          </button>
                          <label className="skill-switch">
                            <input type="checkbox" checked={skill.isEnabled} aria-label={`${skill.isEnabled ? '停用' : '启用'}${skill.name}`} onChange={(event) => updateSkill(skill, event.target.checked)} />
                            <span aria-hidden="true" />
                          </label>
                        </header>
                      </article>
                    ))}
                  </div>
                )}
              </section>
            </div>
          </section>
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
                          <span>{processEvent.message}</span>
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
