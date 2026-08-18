export type VisualAssetKind = 'character' | 'scene' | 'prop'

export interface VisualReferenceImage {
  assetId: string
  subjectResourceId: string
  subjectType: VisualAssetKind
  subjectName: string
  version: number
  contentType: string
  contentUrl: string
  prompt: string
  revisedPrompt: string | null
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

export interface VoiceReference {
  assetId: string
  version: number
  contentType: string
  contentUrl: string
  model: string
  device: string
  durationSeconds: number
  createdAtUtc: string
}

export interface VoiceProfile {
  assetId: string
  resourceId: string
  characterResourceId: string
  version: number
  name: string
  designPrompt: string
  sampleText: string
  language: string
  seed: number
  status: string
  updatedAtUtc: string
  reference: VoiceReference | null
}

export interface SaveVoiceProfileInput {
  name: string
  designPrompt: string
  sampleText: string
  language: string
  seed: number | null
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

export async function getVoiceProfile(
  projectId: string,
  characterResourceId: string,
  signal?: AbortSignal,
): Promise<VoiceProfile | null> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/visual-assets/${characterResourceId}/voice-profile`,
    { signal },
  )
  if (response.status === 404) return null
  if (!response.ok) throw await readError(response, '音色配置加载失败。')
  return response.json() as Promise<VoiceProfile>
}

export async function saveVoiceProfile(
  projectId: string,
  characterResourceId: string,
  input: SaveVoiceProfileInput,
): Promise<VoiceProfile> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/visual-assets/${characterResourceId}/voice-profile`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    },
  )
  if (!response.ok) throw await readError(response, '音色配置保存失败。')
  return response.json() as Promise<VoiceProfile>
}

export async function generateVoiceReference(
  projectId: string,
  characterResourceId: string,
): Promise<VoiceProfile> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/visual-assets/${characterResourceId}/voice-profile/generate`,
    { method: 'POST' },
  )
  if (!response.ok) throw await readError(response, '参考音生成失败。')
  return response.json() as Promise<VoiceProfile>
}