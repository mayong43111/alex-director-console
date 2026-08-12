export type ServiceState = 'checking' | 'online' | 'offline'
export type MobilePanel = 'assets' | 'director' | 'review'

export interface AgentStatus {
  framework: string
  frameworkVersion: string | null
  deployment: string
  configured: boolean
  imageDeployment: string
  imageQuality: string
  imageConfigured: boolean
}

export interface Project {
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

export interface ProjectRuntimeConfiguration {
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

export interface GlobalFoundryConfiguration {
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

export interface ConversationMessageRecord {
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

export interface ProcessEventRecord {
  stage: string
  message: string
  data?: {
    imagePrompt?: string
  }
}

export interface MessageStreamEvent {
  type: 'message.accepted' | 'process' | 'assistant.delta' | 'completed' | 'error'
  stage?: string
  message?: string
  data?: {
    imagePrompt?: string
  }
  detail?: string
  delta?: string
  userMessage?: ConversationMessageRecord
  assistantMessage?: ConversationMessageRecord
  skillRun?: SkillRunRecord
  outputAsset?: AssetRecord
  generatedAssets?: AssetRecord[]
  updatedAsset?: AssetRecord
}

export interface AssetRecord {
  id: string
  resourceId: string
  version: number
  versionCount: number
  projectId: string
  type: string
  name: string
  fileName: string
  contentType: string
  generationMetadataJson: string | null
  sizeBytes: number
  createdAtUtc: string
  contentUrl: string
}

export interface ShotAssetLinkRecord {
  id: string
  role: 'first-frame' | 'last-frame' | 'reference' | 'video' | 'other'
  createdAtUtc: string
  asset: AssetRecord
}

export interface SkillDefinitionRecord {
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

export interface ScriptEntityRecord {
  name: string
  description: string
  evidence: string[]
}

export interface ScriptSceneRecord {
  heading: string
  time: string
  location: string
  summary: string
  characters: string[]
  props: string[]
  evidence: string[]
}

export interface ScriptAnalysisRecord {
  characters: ScriptEntityRecord[]
  locations: ScriptEntityRecord[]
  props: ScriptEntityRecord[]
  scenes: ScriptSceneRecord[]
  ambiguities: string[]
}

export interface SkillRunRecord {
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
