import type { Project } from '../models'

export async function listProjects(signal?: AbortSignal): Promise<Project[]> {
  const response = await fetch('/api/projects', { signal })
  if (!response.ok) throw new Error('项目列表加载失败')
  return response.json() as Promise<Project[]>
}

export async function upsertProject(project: Project, signal?: AbortSignal): Promise<Project> {
  const response = await fetch(`/api/projects/${project.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(project),
    signal,
  })
  if (!response.ok) throw new Error('项目保存失败')
  return response.json() as Promise<Project>
}
