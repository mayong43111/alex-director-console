import { useEffect, useState } from 'react'
import { ChevronDown, History } from 'lucide-react'
import {
  listResourceVersions,
  setCurrentResourceVersion,
  type ResourceVersion,
} from '../api/resourceVersions'

export function VersionPicker({
  projectId,
  assetId,
  label = '版本',
  compact = false,
}: {
  projectId: string
  assetId: string
  label?: string
  compact?: boolean
}) {
  const [versions, setVersions] = useState<ResourceVersion[]>([])
  const [loadedAssetId, setLoadedAssetId] = useState('')
  const [switching, setSwitching] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    listResourceVersions(projectId, assetId, controller.signal)
      .then((loaded) => {
        setVersions(loaded)
        setLoadedAssetId(assetId)
        setError('')
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === 'AbortError') return
        setLoadedAssetId(assetId)
        setError(loadError instanceof Error ? loadError.message : '历史版本加载失败。')
      })
    return () => controller.abort()
  }, [assetId, projectId])

  const loading = loadedAssetId !== assetId
  const current = versions.find((item) => item.isCurrent)

  async function switchVersion(nextAssetId: string) {
    if (!nextAssetId || nextAssetId === current?.assetId || switching) return
    setSwitching(true)
    setError('')
    try {
      await setCurrentResourceVersion(projectId, assetId, nextAssetId)
      window.location.reload()
    } catch (switchError) {
      setError(switchError instanceof Error ? switchError.message : '当前版本切换失败。')
      setSwitching(false)
    }
  }

  return (
    <label className={`version-picker${compact ? ' compact' : ''}`} title={error || `切换${label}`}>
      <History size={13} />
      <span>{label}</span>
      <select
        aria-label={`切换${label}`}
        value={current?.assetId ?? assetId}
        disabled={loading || switching || versions.length < 2}
        onChange={(event) => void switchVersion(event.target.value)}
      >
        {versions.length === 0 && <option value={assetId}>{loading ? '读取中' : '当前版本'}</option>}
        {versions.map((version) => (
          <option value={version.assetId} key={version.assetId}>
            v{version.version}{version.isCurrent ? ' · 当前' : ''}
          </option>
        ))}
      </select>
      <ChevronDown size={12} />
    </label>
  )
}