export interface DigitalPresenterShot {
  id: string
  sortOrder: number
  dialogue: string
  imagePrompt: string
  videoPrompt: string
  effectiveCharacterCount: number
  durationSeconds: number
  firstFrameAssetId: string | null
  videoAssetId: string | null
  status: string
  error: string | null
}

export interface DigitalPresenterEpisode {
  id: string
  episodeNumber: number
  title: string
  dialogue: string
  backgroundImageAssetId: string | null
  outfitImageAssetId: string | null
  status: string
  shots: DigitalPresenterShot[]
  updatedAtUtc: string
}

export interface DigitalPresenter {
  id: string
  name: string
  identityImageAssetId: string
  backgroundImageAssetId: string | null
  outfitImageAssetId: string | null
  voiceAssetId: string
  episodes: DigitalPresenterEpisode[]
  updatedAtUtc: string
}

export interface FoundryImageConfiguration {
  imageProvider: string
  imageDeployment: string
  imageQuality: string
  imageConfigured: boolean
}

export async function listDigitalPresenters(projectId: string, signal?: AbortSignal) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters`, { signal })
  if (!response.ok) throw new Error('数字人资产加载失败。')
  return response.json() as Promise<DigitalPresenter[]>
}

export async function createDigitalPresenter(projectId: string, form: FormData) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters`, { method: 'POST', body: form })
  if (!response.ok) throw new Error(await readError(response, '数字人创建失败。'))
  return response.json() as Promise<DigitalPresenter>
}

export async function updateDigitalPresenter(projectId: string, presenterId: string, form: FormData) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters/${presenterId}`, { method: 'PUT', body: form })
  if (!response.ok) throw new Error(await readError(response, '数字人素材更新失败。'))
  return response.json() as Promise<DigitalPresenter>
}

export async function saveDigitalPresenterEpisode(
  projectId: string,
  presenterId: string,
  episodeId: string | null,
  input: { title: string; dialogue: string; backgroundImageAssetId: string | null; outfitImageAssetId: string | null },
) {
  const path = `/api/v2/projects/${projectId}/digital-presenters/${presenterId}/episodes${episodeId ? `/${episodeId}` : ''}`
  const response = await fetch(path, {
    method: episodeId ? 'PUT' : 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw new Error(await readError(response, '剧集保存失败。'))
  return response.json() as Promise<DigitalPresenterEpisode>
}

export function digitalPresenterMediaUrl(projectId: string, assetId: string) {
  return `/api/v2/projects/${projectId}/digital-presenters/media/${assetId}`
}

async function readError(response: Response, fallback: string) {
  const body = await response.json().catch(() => null) as { error?: string; detail?: string; title?: string } | null
  return body?.error || body?.detail || body?.title || fallback
}

export async function saveDigitalPresenterShot(projectId: string, presenterId: string, episodeId: string, shotId: string, input: { imagePrompt?: string; videoPrompt?: string }) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters/${presenterId}/episodes/${episodeId}/shots/${shotId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) })
  if (!response.ok) throw new Error('分镜提示词保存失败。')
  return response.json()
}

export async function generateDigitalPresenterImagePrompt(projectId: string, presenterId: string, episodeId: string, shotId: string) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters/${presenterId}/episodes/${episodeId}/shots/${shotId}/image-prompt`, { method: 'POST' })
  if (!response.ok) throw new Error(await readError(response, '图片提示词生成失败。'))
  return response.json() as Promise<{ id: string; imagePrompt: string }>
}

export async function generateDigitalPresenterVideoPrompt(projectId: string, presenterId: string, episodeId: string, shotId: string) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters/${presenterId}/episodes/${episodeId}/shots/${shotId}/video-prompt`, { method: 'POST' })
  if (!response.ok) throw new Error(await readError(response, '视频提示词生成失败。'))
  return response.json() as Promise<{ id: string; videoPrompt: string }>
}

export async function generateDigitalPresenterVideo(projectId: string, presenterId: string, episodeId: string, shotId: string) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters/${presenterId}/episodes/${episodeId}/shots/${shotId}/video`, { method: 'POST' })
  if (!response.ok) throw new Error(await readError(response, '视频生成失败。'))
  return response.json() as Promise<{ id: string; videoAssetId: string; status: string }>
}

export async function generateDigitalPresenterFirstFrame(projectId: string, presenterId: string, episodeId: string, shotId: string, imagePrompt?: string) {
  const response = await fetch(`/api/v2/projects/${projectId}/digital-presenters/${presenterId}/episodes/${episodeId}/shots/${shotId}/first-frame`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ imagePrompt }) })
  if (!response.ok) throw new Error((await response.json().catch(() => null))?.error || '首帧生成失败。')
  return response.json()
}

export async function getFoundryImageConfiguration() {
  const response = await fetch('/api/v2/system/foundry-configuration/')
  if (!response.ok) throw new Error('图片生成配置加载失败。')
  return response.json() as Promise<FoundryImageConfiguration>
}

export async function setFoundryImageProvider(imageProvider: string) {
  const current = await (await fetch('/api/v2/system/foundry-configuration/')).json() as Record<string, unknown>
  const response = await fetch('/api/v2/system/foundry-configuration/', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      llmProvider: current.llmProvider,
      endpoint: current.endpoint,
      clearApiKey: false,
      vllmBaseUrl: current.vllmBaseUrl,
      vllmModel: current.vllmModel,
      clearVllmApiKey: false,
      imageProvider,
      imageEndpoint: current.imageEndpoint,
      clearImageApiKey: false,
      imageQuality: current.imageQuality,
    }),
  })
  if (!response.ok) throw new Error('图片生成 provider 保存失败。')
  return response.json() as Promise<FoundryImageConfiguration>
}

