import type { ImageGenerationPreview } from './generation'

export interface StoryboardShot {
  assetId: string
  resourceId: string
  version: number
  sceneNumber: number
  shotNumber: number
  durationSeconds: number
  shotSize: string
  cameraAngle: string
  cameraMovement: string
  composition: string
  visualDescription: string
  action: string
  dialogue: string
  sound: string
  characters: string[]
  props: string[]
  hooks: StoryboardHook[]
  linkedAssets: StoryboardLinkedAsset[]
  production: ShotProduction | null
  status: string
  updatedAtUtc: string
}

export interface Storyboard {
  productionEpisodeId: string
  episodeNumber: number
  title: string
  scriptPackageAssetId: string
  revision: number
  isStale: boolean
  targetSeconds: number
  totalDurationSeconds: number
  shots: StoryboardShot[]
  model: string
  runtime: string
  updatedAtUtc: string
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const problem = await response.json().catch(() => null) as { error?: string; title?: string; detail?: string } | null
  return new Error(problem?.error || problem?.detail || problem?.title || fallback)
}

export async function getStoryboard(
  projectId: string,
  productionEpisodeId: string,
  signal?: AbortSignal,
): Promise<Storyboard | null> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard`,
    { signal },
  )
  if (response.status === 404) return null
  if (!response.ok) throw await readError(response, '分镜加载失败。')
  return response.json() as Promise<Storyboard>
}

export async function generateStoryboard(
  projectId: string,
  productionEpisodeId: string,
): Promise<Storyboard> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/generate`,
    { method: 'POST' },
  )
  if (!response.ok) throw await readError(response, '分镜生成失败。')
  return response.json() as Promise<Storyboard>
}

export interface StoryboardLinkedAsset {
  assetId: string
  resourceId: string
  kind: 'character' | 'scene' | 'prop'
  name: string
}

export interface ShotProduction {
  runId: string
  mode: 'direct-first-frame' | 'first-last-continuous'
  status: string
  currentStage: string
  stages: string[]
  createdAtUtc: string
  outputAssetId?: string | null
  outputUrl?: string | null
}

export async function updateStoryboardShotAssets(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  assetResourceIds: string[],
): Promise<Storyboard> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/assets`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ assetResourceIds }),
    },
  )
  if (!response.ok) throw await readError(response, '镜头资产关联保存失败。')
  return response.json() as Promise<Storyboard>
}

export async function startShotProduction(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  confirmedPrompt: string,
): Promise<ShotProduction> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/production/start`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmedPrompt }),
    },
  )
  if (!response.ok) throw await readError(response, '镜头开始制作失败。')
  return response.json() as Promise<ShotProduction>
}

export async function previewShotProduction(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
): Promise<ImageGenerationPreview> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/production/preview`,
    { method: 'POST' },
  )
  if (!response.ok) throw await readError(response, '首帧生成规格加载失败。')
  return response.json() as Promise<ImageGenerationPreview>
}

export interface StoryboardHook {
  type: 'small' | 'big'
  description: string
}