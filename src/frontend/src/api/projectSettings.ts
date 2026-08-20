import type { ImageGenerationPreview } from './generation'

export interface ProjectSettings {
  projectId: string
  version: number
  projectName: string
  description: string
  contentType: string
  targetAudience: string
  plannedEpisodeCount: number
  targetEpisodeSeconds: number
  aspectRatio: '16:9' | '9:16' | '2.39:1'
  outputWidth: number
  outputHeight: number
  visualStyle: string
  artDirection: string
  characterDesign: string
  colorPalette: string
  cameraLanguage: string
  soundStrategy: string
  imagePromptPrefix: string
  assetId: string | null
  impactedAssetCount: number
  updatedAtUtc: string | null
  cover: ProjectCover | null
}

export interface ProjectCover {
  assetId: string
  version: number
  contentType: string
  contentUrl: string
  createdAtUtc: string
}

export type ProjectSettingsAssistField =
  | 'visualStyle'
  | 'artDirection'
  | 'characterDesign'
  | 'colorPalette'
  | 'cameraLanguage'
  | 'soundStrategy'
  | 'imagePromptPrefix'

export interface ProjectSettingsAssistResult {
  field: ProjectSettingsAssistField
  value: string
  model: string
  runtime: string
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

export async function getProjectSettings(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectSettings> {
  const response = await fetch(`/api/v2/projects/${projectId}/settings`, { signal })
  if (!response.ok) throw await readError(response, '项目设定加载失败。')
  return response.json() as Promise<ProjectSettings>
}

export async function saveProjectSettings(
  projectId: string,
  settings: ProjectSettings,
): Promise<ProjectSettings> {
  const response = await fetch(`/api/v2/projects/${projectId}/settings`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(settings),
  })
  if (!response.ok) throw await readError(response, '项目设定保存失败。')
  return response.json() as Promise<ProjectSettings>
}

export async function generateProjectCover(
  projectId: string,
  instruction?: string,
  confirmedPrompt?: string,
): Promise<ProjectCover> {
  const response = await fetch(`/api/v2/projects/${projectId}/settings/cover`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ instruction, confirmedPrompt }),
  })
  if (!response.ok) throw await readError(response, '概念封面生成失败。')
  return response.json() as Promise<ProjectCover>
}

export async function assistProjectSettingsField(
  projectId: string,
  field: ProjectSettingsAssistField,
  currentValue: string,
  context: ProjectSettings,
  instruction?: string,
): Promise<ProjectSettingsAssistResult> {
  const response = await fetch(`/api/v2/projects/${projectId}/settings/assist`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ field, currentValue, instruction, context }),
  })
  if (!response.ok) throw await readError(response, 'AI 帮写失败。')
  return response.json() as Promise<ProjectSettingsAssistResult>
}

export async function previewProjectCover(
  projectId: string,
  instruction?: string,
): Promise<ImageGenerationPreview> {
  const response = await fetch(`/api/v2/projects/${projectId}/settings/cover/preview`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ instruction }),
  })
  if (!response.ok) throw await readError(response, '概念封面生成规格加载失败。')
  return response.json() as Promise<ImageGenerationPreview>
}