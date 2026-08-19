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
  chapterIds: string[]
}

export interface StoryLocationMaterial {
  name: string
  function: string
  atmosphere: string
  chapterNumbers: number[]
  chapterIds: string[]
}

export interface StoryPlotBeatMaterial {
  order: number
  title: string
  summary: string
  chapterNumbers: number[]
  characterNames: string[]
  locationName: string | null
  chapterIds: string[]
}

export interface StoryRelationMaterial {
  source: string
  target: string
  type: string
  evidence: string
  chapterNumbers: number[]
  chapterIds: string[]
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
  analyzedChapterIds?: string[]
  model: string
  runtime: string
  updatedAtUtc: string
}

export interface AdaptationShotPlanDraft {
  shotNumber: number
  durationSeconds: number
  shotSize: string
  cameraAngle: string
  cameraMovement: string
  purpose: string
}

export interface AdaptationSceneDraft {
  sceneNumber: number
  heading: string
  summary: string
  characters: string[]
  props: string[]
  storyFunction: string
  dialogueNotes: string
  targetSeconds?: number | null
  rhythm?: string | null
  visualContrast?: string | null
  shotPlan?: AdaptationShotPlanDraft[] | null
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

export interface ScreenplayDialogueDraft {
  character: string
  parenthetical: string | null
  lines: string[]
}

export interface ProductionScriptSceneDraft {
  sceneNumber: number
  heading: string
  summary: string
  action: string
  dialogues: ScreenplayDialogueDraft[]
  characters: string[]
  props: string[]
  storyFunction: string
  targetSeconds: number
  rhythm: string
  visualContrast: string
  shotPlan: AdaptationShotPlanDraft[]
  dialogueIntent?: string | null
}

export interface ProductionScriptEpisodeDraft {
  title: string
  logline: string
  targetSeconds: number
  scenes: ProductionScriptSceneDraft[]
  smallHooks: string[]
  bigHooks: string[]
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
  isLegacyOutline: boolean
  episode: ProductionScriptEpisodeDraft
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
  chapterId: string,
  signal?: AbortSignal,
): Promise<StoryMaterialAnalysis> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/chapters/${chapterId}/analysis`, {
    method: 'POST',
    signal,
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
  if (!response.ok) throw await readError(response, '改编大纲加载失败。')
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
  if (!response.ok) throw await readError(response, '改编大纲生成失败。')
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

export async function regenerateAdaptationEpisode(
  projectId: string,
  sourceId: string,
  episodeNumber: number,
  input: { instruction: string },
): Promise<AdaptationScript> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/sources/${sourceId}/script-draft/episodes/${episodeNumber}/regenerate`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    },
  )
  if (!response.ok) throw await readError(response, '重新生成剧集失败。')
  return response.json() as Promise<AdaptationScript>
}

export async function confirmAdaptationScript(
  projectId: string,
  sourceId: string,
): Promise<AdaptationScript> {
  const response = await fetch(`/api/v2/projects/${projectId}/sources/${sourceId}/script-draft/confirm`, {
    method: 'POST',
  })
  if (!response.ok) throw await readError(response, '正式剧本生成失败。')
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

export async function regenerateProductionScript(
  projectId: string,
  productionEpisodeId: string,
): Promise<ProductionScriptPackage> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/script-package/regenerate`,
    { method: 'POST' },
  )
  if (!response.ok) throw await readError(response, '正式剧本重新生成失败。')
  return response.json() as Promise<ProductionScriptPackage>
}
