export interface ProductionRunItem {
  id: string
  shotResourceId: string
  shotAssetId: string
  shotName: string
  stage: string
  status: string
  attempt: number
  outputAssetId: string | null
  outputUrl: string | null
  errorCode: string | null
  errorDetail: string | null
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export interface ProductionRun {
  id: string
  productionEpisodeId: string
  episodeNumber: number
  episodeTitle: string
  mode: string
  status: string
  currentStage: string
  originalInstruction: string
  lastError: string | null
  finalAssetId: string | null
  items: ProductionRunItem[]
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  updatedAtUtc: string
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const problem = await response.json().catch(() => null) as { title?: string; detail?: string } | null
  return new Error(problem?.detail || problem?.title || fallback)
}

export async function listProductionRuns(
  projectId: string,
  productionEpisodeId?: string,
  signal?: AbortSignal,
): Promise<ProductionRun[]> {
  const query = productionEpisodeId
    ? `?productionEpisodeId=${encodeURIComponent(productionEpisodeId)}`
    : ''
  const response = await fetch(`/api/v2/projects/${projectId}/production-runs${query}`, { signal })
  if (!response.ok) throw await readError(response, '生产运行加载失败。')
  return response.json() as Promise<ProductionRun[]>
}

export async function getProductionRun(
  projectId: string,
  runId: string,
  signal?: AbortSignal,
): Promise<ProductionRun | null> {
  const response = await fetch(`/api/v2/projects/${projectId}/production-runs/${runId}`, { signal })
  if (response.status === 404) return null
  if (!response.ok) throw await readError(response, '生产运行详情加载失败。')
  return response.json() as Promise<ProductionRun>
}