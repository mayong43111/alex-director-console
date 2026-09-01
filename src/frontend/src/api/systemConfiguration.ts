export interface FoundryConfiguration {
  provider: string
  llmProvider: 'azure-foundry' | 'vllm'
  endpoint: string
  deployment: string
  apiKeyConfigured: boolean
  vllmBaseUrl: string
  vllmModel: string
  vllmApiKeyConfigured: boolean
  imageProvider: 'azure-foundry' | 'comfyui'
  imageEndpoint: string
  imageDeployment: string
  imageQuality: 'low' | 'medium' | 'high'
  imageApiKeyConfigured: boolean
  imageConfigured: boolean
  updatedAtUtc: string | null
}

export interface FoundryConnectionResult {
  isSuccess: boolean
  message: string
  deployment: string
  isConfigured: boolean
}

export interface ComfyUiConfiguration {
  provider: string
  connectionMode: 'local-http'
  baseUrl: string
  workflowProfile: string
  textToImageWorkflow: string
  imageEditWorkflow: string
  maxConcurrentJobs: number
  isEnabled: boolean
  isConfigured: boolean
  updatedAtUtc: string | null
}

export type ComfyUiImageEditWorkflow = 'qwen-image-edit-2511' | 'flux2-dev-image-edit-kv-cache'
export type ComfyUiVideoWorkflow = 'minimax-h3-fl2va-turbo-4step' | 'ltx-2.3-av-i2v'

export interface ComfyUiCapabilities {
  isSuccess: boolean
  message: string
  workflowProfile: string
  requiredNodes: string[]
  missingNodes: string[]
  requiredModels: string[]
  missingModels: string[]
}

interface ValidationProblem {
  title?: string
  detail?: string
  error?: string
  errors?: Record<string, string[]>
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const problem = await response.json().catch(() => null) as ValidationProblem | null
  const validationMessage = problem?.errors
    ? Object.values(problem.errors).flat().join(' ')
    : null
  return new Error(validationMessage || problem?.error || problem?.detail || problem?.title || fallback)
}

export async function getFoundryConfiguration(signal?: AbortSignal): Promise<FoundryConfiguration> {
  const response = await fetch('/api/v2/system/foundry-configuration', { signal })
  if (!response.ok) throw await readError(response, 'Foundry 配置加载失败。')
  return response.json() as Promise<FoundryConfiguration>
}

export async function updateFoundryConfiguration(input: {
  llmProvider: 'azure-foundry' | 'vllm'
  endpoint: string
  apiKey?: string
  clearApiKey: boolean
  vllmBaseUrl: string
  vllmModel: string
  vllmApiKey?: string
  clearVllmApiKey: boolean
  imageProvider: 'azure-foundry' | 'comfyui'
  imageEndpoint: string
  imageQuality: 'low' | 'medium' | 'high'
  imageApiKey?: string
  clearImageApiKey: boolean
}): Promise<FoundryConfiguration> {
  const response = await fetch('/api/v2/system/foundry-configuration', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, 'Foundry 配置保存失败。')
  return response.json() as Promise<FoundryConfiguration>
}

export async function testFoundryConnection(): Promise<FoundryConnectionResult> {
  const response = await fetch('/api/v2/system/foundry-configuration/test', { method: 'POST' })
  const result = await response.json().catch(() => null) as FoundryConnectionResult | null
  if (!response.ok) throw new Error(result?.message || 'Foundry 连接测试失败。')
  if (!result) throw new Error('Foundry 连接测试未返回结果。')
  return result
}

export async function getComfyUiConfiguration(signal?: AbortSignal): Promise<ComfyUiConfiguration> {
  const response = await fetch('/api/v2/system/comfyui-configuration', { signal })
  if (!response.ok) throw await readError(response, 'ComfyUI 配置加载失败。')
  return response.json() as Promise<ComfyUiConfiguration>
}

export async function updateComfyUiConfiguration(input: {
  baseUrl: string
  imageEditWorkflow: ComfyUiImageEditWorkflow
  workflowProfile: ComfyUiVideoWorkflow
  isEnabled: boolean
}): Promise<ComfyUiConfiguration> {
  const response = await fetch('/api/v2/system/comfyui-configuration', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, 'ComfyUI 配置保存失败。')
  return response.json() as Promise<ComfyUiConfiguration>
}

export async function testComfyUiConnection(): Promise<ComfyUiCapabilities> {
  const response = await fetch('/api/v2/system/comfyui-configuration/test', { method: 'POST' })
  const result = await response.json().catch(() => null) as ComfyUiCapabilities | null
  if (!response.ok) throw new Error(result?.message || 'ComfyUI 连接测试失败。')
  if (!result) throw new Error('ComfyUI 连接测试未返回结果。')
  return result
}