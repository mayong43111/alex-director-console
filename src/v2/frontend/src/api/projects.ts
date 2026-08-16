export interface ProjectRecord {
  id: string
  name: string
  description: string | null
  currentCreativeSettingsId: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

interface ValidationProblem {
  title?: string
  detail?: string
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