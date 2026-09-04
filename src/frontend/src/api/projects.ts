import { waitForGenerationResult, type GenerationTask } from './generationTasks'

export interface ProjectRecord {
  id: string
  type: string
  name: string
  description: string | null
  currentCreativeSettingsId: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

interface ValidationProblem {
  title?: string
  detail?: string
  error?: string
  errors?: Record<string, string[]>
}

export async function createProject(
  input: { name: string; description?: string },
  signal?: AbortSignal,
): Promise<ProjectRecord> {
  const response = await fetch('/api/v2/projects', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
    signal,
  })

  if (response.ok) {
    return response.json() as Promise<ProjectRecord>
  }

  const problem = await response.json().catch(() => null) as ValidationProblem | null
  const validationMessage = problem?.errors
    ? Object.values(problem.errors).flat().join(' ')
    : null
  throw new Error(validationMessage || problem?.detail || problem?.title || '项目创建失败，请稍后重试。')
}

export async function listProjects(signal?: AbortSignal): Promise<ProjectRecord[]> {
  const response = await fetch('/api/v2/projects', { signal })
  if (!response.ok) throw new Error('项目列表加载失败，请检查后端服务。')
  return response.json() as Promise<ProjectRecord[]>
}

export async function getProject(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectRecord | null> {
  const response = await fetch(`/api/v2/projects/${projectId}`, { signal })
  if (response.status === 404) return null
  if (!response.ok) throw new Error('项目信息加载失败。')
  return response.json() as Promise<ProjectRecord>
}

export async function updateProject(
  projectId: string,
  input: { name: string; description?: string },
  signal?: AbortSignal,
): Promise<ProjectRecord> {
  const response = await fetch(`/api/v2/projects/${projectId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
    signal,
  })

  if (response.ok) {
    return response.json() as Promise<ProjectRecord>
  }

  throw new Error(await readProjectError(response, '项目更新失败，请稍后重试。'))
}

export async function deleteProject(
  projectId: string,
  force = false,
  signal?: AbortSignal,
): Promise<void> {
  const query = force ? '?force=true' : ''
  const response = await fetch(`/api/v2/projects/${projectId}${query}`, {
    method: 'DELETE',
    signal,
  })

  if (!response.ok) {
    const message = await readProjectError(response, '项目删除失败，请稍后重试。')
    if (response.status === 409) {
      throw new ProjectDeleteConflictError(message)
    }
    throw new Error(message)
  }
}

export class ProjectDeleteConflictError extends Error {
  override name = 'ProjectDeleteConflictError'
}

export interface ProjectDescriptionAssistResult {
  field: string
  value: string
  model: string
  runtime: string
}

export async function assistProjectDescription(
  input: { name: string; description: string },
  signal?: AbortSignal,
): Promise<ProjectDescriptionAssistResult> {
  const response = await fetch('/api/v2/projects/assist-description', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
    signal,
  })

  if (!response.ok) {
    throw new Error(await readProjectError(response, '项目描述优化失败，请稍后重试。'))
  }
  return waitForGenerationResult<ProjectDescriptionAssistResult>(await response.json() as GenerationTask)
}

async function readProjectError(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null) as ValidationProblem | null
  const validationMessage = problem?.errors
    ? Object.values(problem.errors).flat().join(' ')
    : null
  return validationMessage || problem?.detail || problem?.title || problem?.error || fallback
}

export interface ProductionEpisodeRecord {
  id: string
  episodeNumber: number
  title: string
  targetSeconds: number | null
  status: string
}

export async function listProductionEpisodes(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProductionEpisodeRecord[]> {
  const response = await fetch(`/api/v2/projects/${projectId}/production-episodes`, { signal })
  if (!response.ok) throw new Error('生产剧集列表加载失败。')
  return response.json() as Promise<ProductionEpisodeRecord[]>
}