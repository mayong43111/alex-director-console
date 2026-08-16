export interface SourceChapter {
  id: string
  number: number
  title: string
  content: string
  characterCount: number
}

export interface ProjectSource {
  id: string
  assetId: string
  version: number
  number: number
  title: string
  description: string | null
  fileName: string | null
  characterCount: number
  chapterCount: number
  chapters: SourceChapter[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface StoryCharacterMaterial {
  name: string
  role: string
  goal: string
  traits: string[]
  chapterNumbers: number[]
}

export interface StoryLocationMaterial {
  name: string
  function: string
  atmosphere: string
  chapterNumbers: number[]
}

export interface StoryPlotBeatMaterial {
  order: number
  title: string
  summary: string
  chapterNumbers: number[]
  characterNames: string[]
  locationName: string | null
}

export interface StoryRelationMaterial {
  source: string
  target: string
  type: string
  evidence: string
}

export interface StoryMaterialAnalysis {
  assetId: string
  resourceId: string
  version: number
  sourceResourceId: string
  sourceAssetId: string
  sourceVersion: number
  isStale: boolean
  staleReason: string | null
  summary: string
  characters: StoryCharacterMaterial[]
  locations: StoryLocationMaterial[]
  plotBeats: StoryPlotBeatMaterial[]
  relations: StoryRelationMaterial[]
  model: string
  runtime: string
  updatedAtUtc: string
}

export interface AdaptationSceneDraft {
  sceneNumber: number
  heading: string
  summary: string
  characters: string[]
  props: string[]
  storyFunction: string
  dialogueNotes: string
}

export interface AdaptationEpisodeDraft {
  proposalNumber: number
  title: string
  logline: string
  targetSeconds: number
  sourceChapterNumbers: number[]
  scenes: AdaptationSceneDraft[]
  smallHooks: string[]
  bigHooks: string[]
}

export interface AdaptationScript {
  assetId: string
  resourceId: string
  version: number
  sourceResourceId: string
  sourceAssetId: string
  sourceVersion: number
  analysisAssetId: string
  status: 'draft' | 'confirmed'
  hasNewerSourceVersion: boolean
  title: string
  approach: string
  overallSmallHooks: string[]
  overallBigHooks: string[]
  episodes: AdaptationEpisodeDraft[]
  productionEpisodeIds: string[]
  model: string
  runtime: string
  updatedAtUtc: string
}

export interface ProductionScriptPackage {
  assetId: string
  resourceId: string
  version: number
  productionEpisodeId: string
  episodeNumber: number
  title: string
  targetSeconds: number | null
  status: string
  adaptationScriptAssetId: string
  episode: AdaptationEpisodeDraft
  updatedAtUtc: string
}

export interface CreateProjectSourceInput {
  title: string
  description?: string
  content: string
  fileName?: string
}

interface ValidationProblem {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const problem = await response.json().catch(() => null) as ValidationProblem | null
  const validationMessage = problem?.errors
    ? Object.values(problem.errors).flat().join(' ')
    : null
  return new Error(validationMessage || problem?.detail || problem?.title || fallback)
}

export async function listProjectSources(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectSource[]> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources`, { signal })
  if (!response.ok) throw await readError(response, '原文资料加载失败。')
  return response.json() as Promise<ProjectSource[]>
}

export async function createProjectSource(
  projectId: string,
  input: CreateProjectSourceInput,
): Promise<ProjectSource> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, '原文资料导入失败。')
  return response.json() as Promise<ProjectSource>
}

export async function appendProjectSourceChapters(
  projectId: string,
  sourceId: string,
  input: Pick<CreateProjectSourceInput, 'content' | 'fileName'>,
): Promise<ProjectSource> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/chapters`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, '追加章节失败。')
  return response.json() as Promise<ProjectSource>
}

export async function getStoryMaterialAnalysis(
  projectId: string,
  sourceId: string,
  signal?: AbortSignal,
): Promise<StoryMaterialAnalysis | null> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/analysis`, { signal })
  if (response.status === 404) return null
  if (!response.ok) throw await readError(response, '素材分析加载失败。')
  return response.json() as Promise<StoryMaterialAnalysis>
}

export async function analyzeStoryMaterial(
  projectId: string,
  sourceId: string,
): Promise<StoryMaterialAnalysis> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/analysis`, {
    method: 'POST',
  })
  if (!response.ok) throw await readError(response, '素材分析失败。')
  return response.json() as Promise<StoryMaterialAnalysis>
}

export async function getAdaptationScript(
  projectId: string,
  sourceId: string,
  signal?: AbortSignal,
): Promise<AdaptationScript | null> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/script-draft`, { signal })
  if (response.status === 404) return null
  if (!response.ok) throw await readError(response, '剧本草案加载失败。')
  return response.json() as Promise<AdaptationScript>
}

export async function generateAdaptationScript(
  projectId: string,
  sourceId: string,
  input: { desiredEpisodeCount?: number; instruction?: string },
): Promise<AdaptationScript> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/script-draft`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, '剧本草案生成失败。')
  return response.json() as Promise<AdaptationScript>
}

export async function appendAdaptationEpisode(
  projectId: string,
  sourceId: string,
  input: { instruction?: string } = {},
): Promise<AdaptationScript> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/script-draft/episodes`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, '添加剧集失败。')
  return response.json() as Promise<AdaptationScript>
}

export async function confirmAdaptationScript(
  projectId: string,
  sourceId: string,
): Promise<AdaptationScript> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/script-draft/confirm`, {
    method: 'POST',
  })
  if (!response.ok) throw await readError(response, '剧本确认失败。')
  return response.json() as Promise<AdaptationScript>
}

export async function getProductionScriptPackage(
  projectId: string,
  productionEpisodeId: string,
  signal?: AbortSignal,
): Promise<ProductionScriptPackage> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/script-package`,
    { signal },
  )
  if (!response.ok) throw await readError(response, '正式剧本加载失败。')
  return response.json() as Promise<ProductionScriptPackage>
}
