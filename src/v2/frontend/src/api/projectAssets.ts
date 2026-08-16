export type VisualAssetKind = 'character' | 'scene' | 'prop'

export interface VisualReferenceImage {
  assetId: string
  subjectResourceId: string
  subjectType: 'character' | 'scene'
  subjectName: string
  version: number
  contentType: string
  contentUrl: string
  createdAtUtc: string
}

export interface VisualAsset {
  assetId: string
  resourceId: string
  version: number
  number: number
  kind: VisualAssetKind
  name: string
  summary: string
  visualDescription: string
  mustKeep: string[]
  avoid: string[]
  storyReferences: string[]
  status: string
  sourceAssetId: string | null
  updatedAtUtc: string
  referenceImage: VisualReferenceImage | null
}

export interface SaveVisualAssetInput {
  kind: VisualAssetKind
  name: string
  summary: string
  visualDescription: string
  mustKeep: string[]
  avoid: string[]
  storyReferences: string[]
  sourceAssetId?: string | null
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

export async function listVisualAssets(
  projectId: string,
  signal?: AbortSignal,
): Promise<VisualAsset[]> {
  const response = await fetch(`/api/v2/projects/${projectId}/visual-assets`, { signal })
  if (!response.ok) throw await readError(response, '资产加载失败。')
  return response.json() as Promise<VisualAsset[]>
}

export async function createVisualAsset(
  projectId: string,
  input: SaveVisualAssetInput,
): Promise<VisualAsset> {
  const response = await fetch(`/api/v2/projects/${projectId}/visual-assets`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, '资产创建失败。')
  return response.json() as Promise<VisualAsset>
}

export async function updateVisualAsset(
  projectId: string,
  resourceId: string,
  input: SaveVisualAssetInput,
): Promise<VisualAsset> {
  const response = await fetch(`/api/v2/projects/${projectId}/visual-assets/${resourceId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, '资产保存失败。')
  return response.json() as Promise<VisualAsset>
}

export async function importStoryMaterialAssets(projectId: string): Promise<VisualAsset[]> {
  const response = await fetch(`/api/v2/projects/${projectId}/visual-assets/import-story-materials`, {
    method: 'POST',
  })
  if (!response.ok) throw await readError(response, '从素材图谱建立资产失败。')
  return response.json() as Promise<VisualAsset[]>
}

export async function generateVisualReference(
  projectId: string,
  resourceId: string,
): Promise<VisualReferenceImage> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/visual-assets/${resourceId}/reference/generate`,
    { method: 'POST' },
  )
  if (!response.ok) throw await readError(response, '参考图生成失败。')
  return response.json() as Promise<VisualReferenceImage>
}