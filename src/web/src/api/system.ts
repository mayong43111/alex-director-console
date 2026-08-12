import type { GlobalFoundryConfiguration, ProjectRuntimeConfiguration } from '../models'

interface FoundryConfigurationUpdate extends GlobalFoundryConfiguration {
  openAiApiKey: string | null
  imageApiKey: string | null
  speechApiKey: string | null
  clearOpenAiApiKey: boolean
  clearImageApiKey: boolean
  clearSpeechApiKey: boolean
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const problem = await response.json().catch(() => null) as { error?: string } | null
  return new Error(problem?.error ?? fallback)
}

export async function getRuntimeConfiguration(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectRuntimeConfiguration> {
  const response = await fetch(`/api/projects/${projectId}/runtime-configuration`, { signal })
  if (!response.ok) throw new Error('VM 配置加载失败')
  return response.json() as Promise<ProjectRuntimeConfiguration>
}

export async function updateRuntimeConfiguration(
  projectId: string,
  configuration: ProjectRuntimeConfiguration,
): Promise<ProjectRuntimeConfiguration> {
  const response = await fetch(`/api/projects/${projectId}/runtime-configuration`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(configuration),
  })
  if (!response.ok) throw await readError(response, 'VM 配置保存失败')
  return response.json() as Promise<ProjectRuntimeConfiguration>
}

export async function getFoundryConfiguration(signal?: AbortSignal): Promise<GlobalFoundryConfiguration> {
  const response = await fetch('/api/system/foundry-configuration', { signal })
  if (!response.ok) throw new Error('Azure Foundry 配置加载失败')
  return response.json() as Promise<GlobalFoundryConfiguration>
}

export async function updateFoundryConfiguration(
  configuration: FoundryConfigurationUpdate,
): Promise<GlobalFoundryConfiguration> {
  const response = await fetch('/api/system/foundry-configuration', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(configuration),
  })
  if (!response.ok) throw await readError(response, 'Azure Foundry 配置保存失败')
  return response.json() as Promise<GlobalFoundryConfiguration>
}
