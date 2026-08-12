import type { AssetRecord, ShotAssetLinkRecord } from '../models'

export async function listProjectAssets(projectId: string, signal?: AbortSignal): Promise<AssetRecord[]> {
  const response = await fetch(`/api/projects/${projectId}/assets`, { signal })
  if (!response.ok) throw new Error('资产列表加载失败')
  return response.json() as Promise<AssetRecord[]>
}

export async function listAssetVersions(projectId: string, assetId: string, signal?: AbortSignal): Promise<AssetRecord[]> {
  const response = await fetch(`/api/projects/${projectId}/assets/${assetId}/versions`, { signal })
  if (!response.ok) throw new Error('资源版本加载失败')
  return response.json() as Promise<AssetRecord[]>
}

export async function listShotAssetLinks(projectId: string, assetId: string, signal?: AbortSignal): Promise<ShotAssetLinkRecord[]> {
  const response = await fetch(`/api/projects/${projectId}/assets/${assetId}/linked-assets`, { signal })
  if (!response.ok) throw new Error('镜头素材加载失败')
  return response.json() as Promise<ShotAssetLinkRecord[]>
}

export async function uploadProjectAsset(
  projectId: string,
  type: string,
  file: File,
): Promise<AssetRecord> {
  const form = new FormData()
  form.append('file', file)
  form.append('type', type)

  const response = await fetch(`/api/projects/${projectId}/assets`, {
    method: 'POST',
    body: form,
  })
  if (!response.ok) throw new Error(`上传 ${file.name} 失败`)
  return response.json() as Promise<AssetRecord>
}

export async function deleteProjectAsset(projectId: string, assetId: string): Promise<void> {
  const response = await fetch(`/api/projects/${projectId}/assets/${assetId}`, {
    method: 'DELETE',
  })
  if (response.ok) return

  if (response.status === 404) throw new Error('资产不存在或已被删除')
  const problem = await response.json().catch(() => null) as { error?: string } | null
  throw new Error(problem?.error ?? '资产删除失败')
}
