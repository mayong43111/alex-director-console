import { useEffect, useState } from 'react'
import { Button, Modal } from 'antd'
import { Check, Eye, History } from 'lucide-react'
import {
  getResourceVersion,
  listResourceVersions,
  setCurrentResourceVersion,
  type ResourceVersion,
  type ResourceVersionDetail,
} from '../api/resourceVersions'

const dateFormatter = new Intl.DateTimeFormat('zh-CN', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

function formatDocument(documentJson: string | null) {
  if (!documentJson) return ''
  try {
    return JSON.stringify(JSON.parse(documentJson), null, 2)
  } catch {
    return documentJson
  }
}

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
  const [open, setOpen] = useState(false)
  const [detail, setDetail] = useState<ResourceVersionDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

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

  async function viewVersion(version: ResourceVersion) {
    setDetailLoading(true)
    setError('')
    try {
      setDetail(await getResourceVersion(projectId, assetId, version.assetId))
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : '历史版本读取失败。')
    } finally {
      setDetailLoading(false)
    }
  }

  function openBrowser() {
    setOpen(true)
    const initialVersion = current ?? versions[0]
    if (initialVersion) void viewVersion(initialVersion)
  }

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
    <>
      <button
        className={`version-picker${compact ? ' compact' : ''}`}
        type="button"
        title={error || `查看${label}记录`}
        aria-label={`查看${label}记录`}
        disabled={loading || switching}
        onClick={openBrowser}
      >
        <History size={13} />
        <span>{label}</span>
        {!compact && <strong>{current ? `v${current.version}` : loading ? '读取中' : '当前版本'}</strong>}
      </button>
      <Modal
        className="version-browser-dialog"
        title={`${label}记录`}
        open={open}
        width={760}
        footer={null}
        destroyOnHidden
        onCancel={() => setOpen(false)}
      >
        <div className="version-browser-body">
          <div className="version-browser-list" aria-label={`${label}列表`}>
            {versions.map((version) => (
              <div
                className={`version-browser-row${detail?.assetId === version.assetId ? ' selected' : ''}`}
                key={version.assetId}
              >
                <div className="version-browser-version">
                  <span>v{version.version}</span>
                  <strong>{version.name}</strong>
                  <time>{dateFormatter.format(new Date(version.createdAtUtc))}</time>
                </div>
                <div className="version-browser-actions">
                  <Button
                    type="text"
                    size="small"
                    icon={<Eye size={13} />}
                    aria-label={`查看 v${version.version}`}
                    onClick={() => void viewVersion(version)}
                  >
                    查看
                  </Button>
                  {version.isCurrent ? (
                    <span className="version-browser-current"><Check size={13} />当前</span>
                  ) : (
                    <Button
                      size="small"
                      disabled={switching}
                      aria-label={`激活 v${version.version}`}
                      onClick={() => void switchVersion(version.assetId)}
                    >
                      激活
                    </Button>
                  )}
                </div>
              </div>
            ))}
          </div>
          <div className="version-browser-preview" aria-live="polite">
            {detailLoading ? (
              <div className="version-browser-empty">正在读取版本…</div>
            ) : detail ? (
              <>
                <header>
                  <div>
                    <span>版本详情</span>
                    <h3>v{detail.version} · {detail.name}</h3>
                  </div>
                  {detail.isCurrent && <span className="version-browser-current"><Check size={13} />当前版本</span>}
                </header>
                <dl>
                  <div><dt>创建时间</dt><dd>{dateFormatter.format(new Date(detail.createdAtUtc))}</dd></div>
                  <div><dt>资源类型</dt><dd>{detail.type}</dd></div>
                  <div><dt>内容大小</dt><dd>{detail.sizeBytes.toLocaleString()} 字节</dd></div>
                </dl>
                {detail.documentJson ? (
                  <pre>{formatDocument(detail.documentJson)}</pre>
                ) : (
                  <div className="version-browser-empty">该版本没有可预览的结构化内容。</div>
                )}
              </>
            ) : (
              <div className="version-browser-empty">选择一个版本查看内容。</div>
            )}
            {error && <div className="version-browser-error" role="alert">{error}</div>}
          </div>
        </div>
      </Modal>
    </>
  )
}