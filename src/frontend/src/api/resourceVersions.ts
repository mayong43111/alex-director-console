export interface ResourceVersion {
  assetId: string
  resourceId: string
  version: number
  type: string
  name: string
  isCurrent: boolean
  createdAtUtc: string
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const problem = await response.json().catch(() => null) as { error?: string; detail?: string; title?: string } | null
  return new Error(problem?.error || problem?.detail || problem?.title || fallback)
}

export async function listResourceVersions(
  projectId: string,
  assetId: string,
  signal?: AbortSignal,
): Promise<ResourceVersion[]> {
  const response = await fetch(`/api/v2/projects/${projectId}/assets/${assetId}/versions`, { signal })
  if (!response.ok) throw await readError(response, '历史版本加载失败。')
  return response.json() as Promise<ResourceVersion[]>
}

export async function setCurrentResourceVersion(
  projectId: string,
  anchorAssetId: string,
  assetId: string,
): Promise<ResourceVersion> {
  const response = await fetch(`/api/v2/projects/${projectId}/assets/${anchorAssetId}/versions/current`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ assetId }),
  })
  if (!response.ok) throw await readError(response, '当前版本切换失败。')
  return response.json() as Promise<ResourceVersion>
}