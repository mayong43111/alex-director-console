export interface FoundryConfiguration {
  provider: string
  endpoint: string
  deployment: string
  apiKeyConfigured: boolean
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
  endpoint: string
  apiKey?: string
  clearApiKey: boolean
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