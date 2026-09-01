import { waitForGenerationResult, type GenerationTask } from './generationTasks'

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
  narration: string
  dialogueCharacter: string
  dialogue: string
  sound: string
  characters: string[]
  props: string[]
  hooks: StoryboardHook[]
  productionMode: 'direct-first-frame' | 'first-last-continuous'
  frameStrategyReason: string
  firstFrameDescription: string
  lastFrameDescription: string
  cutDescription: string
  linkedAssets: StoryboardLinkedAsset[]
  imagePrompt: StoryboardMediaPrompt | null
  videoPrompt: StoryboardMediaPrompt | null
  dialogueAudio: StoryboardDialogueAudio | null
  production: ShotProduction | null
  videoProduction: ShotVideoProduction | null
  status: string
  updatedAtUtc: string
}

export interface StoryboardDialogueAudio {
  assetId: string
  shotResourceId: string
  version: number
  contentUrl: string
  text: string
  voiceName: string
  durationSeconds: number
  createdAtUtc: string
}

export type StoryboardShotTextField =
  | 'visualDescription'
  | 'firstFrameDescription'
  | 'lastFrameDescription'
  | 'cutDescription'
  | 'narration'
  | 'dialogueCharacter'
  | 'dialogue'
  | 'sound'

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
): Promise<GenerationTask> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/generate`,
    { method: 'POST' },
  )
  if (!response.ok) throw await readError(response, '分镜生成失败。')
  return response.json() as Promise<GenerationTask>
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
  outputPrompt?: string | null
  lastFrameAssetId?: string | null
  lastFrameUrl?: string | null
  lastFramePrompt?: string | null
}

export interface ShotVideoPreview {
  prompt: string
  previewHash: string
  width: number
  height: number
  frameCount: number
  fps: number
  durationSeconds: number
  firstFrameAssetId: string
  lastFrameAssetId?: string | null
  workflowProfile: string
}

export interface ShotVideoProduction {
  runId: string
  status: string
  currentStage: string
  assetId?: string | null
  url?: string | null
  version?: number | null
  prompt: string
  createdAtUtc: string
  error?: string | null
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

  export async function updateStoryboardShotMode(
    projectId: string,
    productionEpisodeId: string,
    shotResourceId: string,
    requiresLastFrame: boolean,
  ): Promise<Storyboard> {
    const response = await fetch(
      `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/mode`,
      {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ requiresLastFrame }),
      },
    )
    if (!response.ok) throw await readError(response, '镜头帧策略保存失败。')
    return response.json() as Promise<Storyboard>
  }

  export async function updateStoryboardShotText(
    projectId: string,
    productionEpisodeId: string,
    shotResourceId: string,
    field: StoryboardShotTextField,
    value: string,
  ): Promise<Storyboard> {
    const response = await fetch(
      `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/text/${field}`,
      {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ value }),
      },
    )
    if (!response.ok) throw await readError(response, '镜头文本保存失败。')
    return response.json() as Promise<Storyboard>
  }

  export async function rewriteStoryboardShotText(
    projectId: string,
    productionEpisodeId: string,
    shotResourceId: string,
    instruction: string,
  ): Promise<Storyboard> {
    const response = await fetch(
      `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/rewrite`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ instruction }),
      },
    )
    if (!response.ok) throw await readError(response, '镜头文本重新生成失败。')
    return response.json() as Promise<Storyboard>
  }

export async function startShotProduction(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  confirmedPrompt: string,
  instruction?: string,
  firstFrameOnly = false,
  reconfirmPromptInputs = false,
): Promise<ShotProduction> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/production/start`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmedPrompt, instruction, firstFrameOnly, reconfirmPromptInputs }),
    },
  )
  if (!response.ok) throw await readError(response, '镜头开始制作失败。')
  return response.json() as Promise<ShotProduction>
}

export async function previewShotProduction(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  instruction?: string,
): Promise<ImageGenerationPreview> {
  const query = instruction?.trim() ? `?instruction=${encodeURIComponent(instruction.trim())}` : ''
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/production/preview${query}`,
    { method: 'POST' },
  )
  if (!response.ok) throw await readError(response, '首帧生成规格加载失败。')
  return waitForGenerationResult<ImageGenerationPreview>(await response.json() as GenerationTask)
}

function shotVideoRoute(projectId: string, productionEpisodeId: string, shotResourceId: string) {
  return `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/video`
}

export async function getShotVideo(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  signal?: AbortSignal,
): Promise<ShotVideoProduction | null> {
  const response = await fetch(shotVideoRoute(projectId, productionEpisodeId, shotResourceId), { signal })
  if (response.status === 404) return null
  if (!response.ok) throw await readError(response, '镜头视频状态加载失败。')
  return response.json() as Promise<ShotVideoProduction>
}

export async function previewShotVideo(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  instruction?: string,
): Promise<ShotVideoPreview> {
  const query = instruction?.trim() ? `?instruction=${encodeURIComponent(instruction.trim())}` : ''
  const response = await fetch(`${shotVideoRoute(projectId, productionEpisodeId, shotResourceId)}/preview${query}`, {
    method: 'POST',
  })
  if (!response.ok) throw await readError(response, '视频生成规格加载失败。')
  return waitForGenerationResult<ShotVideoPreview>(await response.json() as GenerationTask)
}

export async function startShotVideo(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  confirmedPrompt: string,
  previewHash: string,
  instruction?: string,
): Promise<ShotVideoProduction> {
  const response = await fetch(`${shotVideoRoute(projectId, productionEpisodeId, shotResourceId)}/start`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ confirmedPrompt, previewHash, instruction }),
  })
  if (!response.ok) throw await readError(response, '镜头视频任务创建失败。')
  return waitForGenerationResult<ShotVideoProduction>(await response.json() as GenerationTask)
}

export interface StoryboardHook {
  type: 'small' | 'big'
  description: string
}

export interface StoryboardMediaPrompt {
  assetId: string
  shotResourceId: string
  kind: 'image' | 'video'
  version: number
  prompt: string
  instruction?: string | null
  previewHash?: string | null
  createdAtUtc: string
}

export interface BatchStoryboardMediaResult {
  generated: number
  skipped: number
  failed: number
  errors: string[]
}

function shotImageRoute(projectId: string, productionEpisodeId: string, shotResourceId: string) {
  return `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/shots/${shotResourceId}/image`
}

export async function generateStoryboardImagePrompt(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  instruction?: string,
): Promise<GenerationTask> {
  const response = await fetch(`${shotImageRoute(projectId, productionEpisodeId, shotResourceId)}/prompt`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ instruction }),
  })
  if (!response.ok) throw await readError(response, '图片提示词生成失败。')
  return response.json() as Promise<GenerationTask>
}

export async function generateStoryboardImage(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
): Promise<GenerationTask> {
  const response = await fetch(`${shotImageRoute(projectId, productionEpisodeId, shotResourceId)}/generate`, {
    method: 'POST',
  })
  if (!response.ok) throw await readError(response, '镜头图片生成失败。')
  return response.json() as Promise<GenerationTask>
}

export async function generateStoryboardVideoPrompt(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
  instruction?: string,
): Promise<GenerationTask> {
  const response = await fetch(`${shotVideoRoute(projectId, productionEpisodeId, shotResourceId)}/prompt`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ instruction }),
  })
  if (!response.ok) throw await readError(response, '视频提示词生成失败。')
  return response.json() as Promise<GenerationTask>
}

export async function generateStoryboardVideo(
  projectId: string,
  productionEpisodeId: string,
  shotResourceId: string,
): Promise<GenerationTask> {
  const response = await fetch(`${shotVideoRoute(projectId, productionEpisodeId, shotResourceId)}/generate`, {
    method: 'POST',
  })
  if (!response.ok) throw await readError(response, '镜头视频任务创建失败。')
  return response.json() as Promise<GenerationTask>
}

async function runStoryboardBatch(
  projectId: string,
  productionEpisodeId: string,
  operation: 'image-prompts' | 'images' | 'video-prompts' | 'videos',
  shotResourceIds?: string[],
): Promise<GenerationTask> {
  const response = await fetch(
    `/api/v2/projects/${projectId}/production-episodes/${productionEpisodeId}/storyboard/batch/${operation}`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ shotResourceIds: shotResourceIds?.length ? shotResourceIds : null }),
    },
  )
  if (!response.ok) throw await readError(response, '分镜批量操作失败。')
  return response.json() as Promise<GenerationTask>
}

export const generateMissingStoryboardImagePrompts = (projectId: string, productionEpisodeId: string, shotResourceIds?: string[]) =>
  runStoryboardBatch(projectId, productionEpisodeId, 'image-prompts', shotResourceIds)

export const generateMissingStoryboardImages = (projectId: string, productionEpisodeId: string, shotResourceIds?: string[]) =>
  runStoryboardBatch(projectId, productionEpisodeId, 'images', shotResourceIds)

export const generateMissingStoryboardVideoPrompts = (projectId: string, productionEpisodeId: string, shotResourceIds?: string[]) =>
  runStoryboardBatch(projectId, productionEpisodeId, 'video-prompts', shotResourceIds)

export const generateMissingStoryboardVideos = (projectId: string, productionEpisodeId: string, shotResourceIds?: string[]) =>
  runStoryboardBatch(projectId, productionEpisodeId, 'videos', shotResourceIds)