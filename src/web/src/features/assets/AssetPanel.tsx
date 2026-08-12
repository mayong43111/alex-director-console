import { ChevronDown, ChevronUp, LoaderCircle, RefreshCw, Trash2, Upload, type LucideIcon } from 'lucide-react'
import type { ChangeEvent } from 'react'
import type { AssetRecord } from '../../models'

export interface AssetSection {
  id: string
  type: string
  label: string
  accept: string
  icon: LucideIcon
  contentTypePrefix?: string
}

interface AssetPanelProps {
  projectName: string
  selectedSection: AssetSection
  groupedSections: AssetSection[]
  isGroupedSection: boolean
  groupedAssetCount: number
  visibleAssetCount: number
  assets: AssetRecord[]
  selectedAsset: AssetRecord | null
  activeSectionId: string
  expandedSectionIds: Set<string>
  assetsLoading: boolean
  refreshingAssets: boolean
  uploadingAssets: boolean
  deletingAssetId: string | null
  assetError: string | null
  onSwitchProject: () => void
  onRefresh: () => void
  onUpload: (event: ChangeEvent<HTMLInputElement>, section: AssetSection) => void
  onReviewAsset: (asset: AssetRecord) => void
  onDeleteAsset: (asset: AssetRecord) => void
  onToggleSection: (sectionId: string) => void
}

function formatFileSize(sizeBytes: number) {
  if (sizeBytes < 1024) return `${sizeBytes} B`
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`
}

export function AssetPanel({
  projectName,
  selectedSection,
  groupedSections,
  isGroupedSection,
  groupedAssetCount,
  visibleAssetCount,
  assets,
  selectedAsset,
  activeSectionId,
  expandedSectionIds,
  assetsLoading,
  refreshingAssets,
  uploadingAssets,
  deletingAssetId,
  assetError,
  onSwitchProject,
  onRefresh,
  onUpload,
  onReviewAsset,
  onDeleteAsset,
  onToggleSection,
}: AssetPanelProps) {
  return (
    <section className="assets-panel" aria-labelledby="assets-title">
      <header className="assets-header">
        <div>
          <p className="section-label">当前项目</p>
          <p className="current-project-name">{projectName}</p>
        </div>
        <button className="switch-project" type="button" onClick={onSwitchProject}>
          切换
        </button>
      </header>
      <div className="assets-title-row">
        <div>
          <p className="section-label">ASSETS</p>
          <h2 id="assets-title">{isGroupedSection ? '资产' : selectedSection.label}</h2>
        </div>
        <div className="assets-actions">
          <span className="asset-count">
            {isGroupedSection ? groupedAssetCount : visibleAssetCount}
          </span>
          <button
            className="asset-refresh-button"
            type="button"
            title="刷新资产"
            aria-label="刷新资产"
            disabled={assetsLoading || refreshingAssets}
            onClick={onRefresh}
          >
            <RefreshCw className={refreshingAssets ? 'spin' : undefined} size={14} aria-hidden="true" />
          </button>
          {!isGroupedSection && (
            <label className="asset-upload-button" title={`上传${selectedSection.label}`}>
              <Upload size={14} aria-hidden="true" />
              <span className="sr-only">上传{selectedSection.label}</span>
              <input
                type="file"
                multiple
                accept={selectedSection.accept}
                aria-label={`上传${selectedSection.label}`}
                disabled={uploadingAssets}
                onChange={(event) => onUpload(event, selectedSection)}
              />
            </label>
          )}
        </div>
      </div>
      {assetError && <p className="asset-error">{assetError}</p>}
      {assetsLoading || uploadingAssets ? (
        <div className="asset-empty">
          <Upload size={22} strokeWidth={1.4} aria-hidden="true" />
          <p>{uploadingAssets ? '正在上传…' : '正在加载…'}</p>
        </div>
      ) : (
        <div className="asset-groups">
          {(isGroupedSection ? groupedSections : [selectedSection]).map((section) => {
            const Icon = section.icon
            const sectionAssets = assets
              .filter((asset) => asset.type === section.type
                && (!section.contentTypePrefix || asset.contentType.startsWith(section.contentTypePrefix)))
              .sort((left, right) => section.type === 'shot'
                ? left.name.localeCompare(right.name, 'zh-CN')
                : new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime())
            const isExpanded = expandedSectionIds.has(section.id)

            if (!isGroupedSection) {
              return sectionAssets.length > 0 ? (
                <div className="asset-list standalone-asset-list" key={section.id}>
                  {sectionAssets.map((asset) => (
                    <AssetRow
                      key={asset.id}
                      asset={asset}
                      icon={Icon}
                      selected={selectedAsset?.id === asset.id}
                      deleting={deletingAssetId === asset.id}
                      onReview={onReviewAsset}
                      onDelete={onDeleteAsset}
                    />
                  ))}
                </div>
              ) : (
                <div className="asset-empty" key={section.id}>
                  <Icon size={22} strokeWidth={1.4} aria-hidden="true" />
                  <p>暂无{section.label}</p>
                </div>
              )
            }

            return (
              <section className="asset-group" key={section.id}>
                <div className="asset-group-header">
                  <button
                    type="button"
                    className={activeSectionId === section.id ? 'active' : undefined}
                    aria-expanded={isExpanded}
                    onClick={() => onToggleSection(section.id)}
                  >
                    <Icon size={16} strokeWidth={1.6} aria-hidden="true" />
                    <strong>{section.label}</strong>
                    <span>{sectionAssets.length}</span>
                    {isExpanded
                      ? <ChevronUp size={14} aria-hidden="true" />
                      : <ChevronDown size={14} aria-hidden="true" />}
                  </button>
                  <label className="asset-upload-button" title={`上传${section.label}`}>
                    <Upload size={14} aria-hidden="true" />
                    <span className="sr-only">上传{section.label}</span>
                    <input
                      type="file"
                      multiple
                      accept={section.accept}
                      aria-label={`上传${section.label}`}
                      disabled={uploadingAssets}
                      onChange={(event) => onUpload(event, section)}
                    />
                  </label>
                </div>
                {isExpanded && (sectionAssets.length > 0 ? (
                  <div className="asset-list">
                    {sectionAssets.map((asset) => (
                      <AssetRow
                        key={asset.id}
                        asset={asset}
                        icon={Icon}
                        selected={selectedAsset?.id === asset.id}
                        deleting={deletingAssetId === asset.id}
                        onReview={onReviewAsset}
                        onDelete={onDeleteAsset}
                      />
                    ))}
                  </div>
                ) : (
                  <p className="asset-group-empty">暂无{section.label}</p>
                ))}
              </section>
            )
          })}
        </div>
      )}
    </section>
  )
}

interface AssetRowProps {
  asset: AssetRecord
  icon: LucideIcon
  selected: boolean
  deleting: boolean
  onReview: (asset: AssetRecord) => void
  onDelete: (asset: AssetRecord) => void
}

function AssetRow({ asset, icon: Icon, selected, deleting, onReview, onDelete }: AssetRowProps) {
  return (
    <div
      className={`asset-row ${selected ? 'active' : ''}`}
    >
      <button
        type="button"
        className="asset-row-review"
        title={`审阅 ${asset.fileName}`}
        aria-pressed={selected}
        onClick={() => onReview(asset)}
      >
        <span className="asset-row-number">{asset.number.toString().padStart(3, '0')}</span>
        <Icon size={17} strokeWidth={1.5} aria-hidden="true" />
        <span>
          <strong>{asset.name}</strong>
          <small>
            {asset.versionCount > 1
              ? `当前 v${asset.version} · 共 ${asset.versionCount} 版 · `
              : ''}
            {formatFileSize(asset.sizeBytes)}
          </small>
        </span>
      </button>
      <button
        type="button"
        className="asset-row-delete"
        title={`删除 ${asset.name}`}
        aria-label={`删除 ${asset.name}`}
        disabled={deleting}
        onClick={() => onDelete(asset)}
      >
        {deleting
          ? <LoaderCircle className="spin" size={14} aria-hidden="true" />
          : <Trash2 size={14} aria-hidden="true" />}
      </button>
    </div>
  )
}
