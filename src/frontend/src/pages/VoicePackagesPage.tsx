import { useEffect, useState, type FormEvent } from 'react'
import { Button, Modal, Popconfirm } from 'antd'
import { AudioLines, ExternalLink, Pencil, Plus, Save, Trash2, Upload } from 'lucide-react'
import {
  archiveVoicePackage,
  createVoicePackage,
  listVoicePackages,
  updateVoicePackage,
  type SaveVoicePackageInput,
  type VoicePackage,
} from '../api/systemConfiguration'

const cosyVoice3Model = 'FunAudioLLM/Fun-CosyVoice3-0.5B-2512'

const emptyEditor: SaveVoicePackageInput = {
  name: '',
  description: '',
  engine: 'gpt-sovits',
  baseModelVersion: 'v2ProPlus',
  gptWeightsPath: '',
  soVitsWeightsPath: '',
  referenceText: '',
  language: 'zh',
  dialect: '普通话',
  speakingStyle: '',
  defaultSpeed: 1,
  license: '',
  sourceUrl: '',
  referenceAudio: null,
}

function toEditor(voicePackage: VoicePackage): SaveVoicePackageInput {
  return {
    name: voicePackage.name,
    description: voicePackage.description,
    engine: voicePackage.engine,
    baseModelVersion: voicePackage.baseModelVersion,
    gptWeightsPath: voicePackage.gptWeightsPath,
    soVitsWeightsPath: voicePackage.soVitsWeightsPath,
    referenceText: voicePackage.referenceText,
    language: voicePackage.language,
    dialect: voicePackage.dialect,
    speakingStyle: voicePackage.speakingStyle,
    defaultSpeed: voicePackage.defaultSpeed,
    license: voicePackage.license,
    sourceUrl: voicePackage.sourceUrl ?? '',
    referenceAudio: null,
  }
}

export function VoicePackagesPage() {
  const [packages, setPackages] = useState<VoicePackage[]>([])
  const [editing, setEditing] = useState<VoicePackage | null>(null)
  const [editor, setEditor] = useState<SaveVoicePackageInput>(emptyEditor)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const load = async (signal?: AbortSignal) => {
    try {
      setPackages(await listVoicePackages(signal))
      setError('')
    } catch (loadError) {
      if (loadError instanceof DOMException && loadError.name === 'AbortError') return
      setError(loadError instanceof Error ? loadError.message : '语音包加载失败。')
    } finally {
      if (!signal?.aborted) setLoading(false)
    }
  }

  useEffect(() => {
    const controller = new AbortController()
    listVoicePackages(controller.signal)
      .then((loaded) => {
        setPackages(loaded)
        setError('')
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === 'AbortError') return
        setError(loadError instanceof Error ? loadError.message : '语音包加载失败。')
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [])

  const openCreate = () => {
    setEditing(null)
    setEditor({ ...emptyEditor })
    setError('')
    setDialogOpen(true)
  }

  const openEdit = (voicePackage: VoicePackage) => {
    setEditing(voicePackage)
    setEditor(toEditor(voicePackage))
    setError('')
    setDialogOpen(true)
  }

  const selectEngine = (engine: SaveVoicePackageInput['engine']) => {
    setEditor((current) => ({
      ...current,
      engine,
      baseModelVersion: engine === 'cosyvoice' ? cosyVoice3Model : 'v2ProPlus',
      gptWeightsPath: engine === 'cosyvoice' ? '' : current.gptWeightsPath,
      soVitsWeightsPath: engine === 'cosyvoice' ? '' : current.soVitsWeightsPath,
    }))
  }

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!editing && !editor.referenceAudio) {
      setError('新语音包必须上传参考 WAV。')
      return
    }
    setSaving(true)
    setError('')
    try {
      if (editing) await updateVoicePackage(editing.resourceId, editor)
      else await createVoicePackage(editor)
      setDialogOpen(false)
      setLoading(true)
      await load()
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : '语音包保存失败。')
    } finally {
      setSaving(false)
    }
  }

  const archive = async (voicePackage: VoicePackage) => {
    setError('')
    try {
      await archiveVoicePackage(voicePackage.resourceId)
      setPackages((current) => current.filter((item) => item.resourceId !== voicePackage.resourceId))
    } catch (archiveError) {
      setError(archiveError instanceof Error ? archiveError.message : '语音包停用失败。')
    }
  }

  return (
    <div className="voice-packages-page">
      <header className="voice-packages-toolbar">
        <div>
          <span className="eyebrow">TTS VOICE LIBRARY</span>
          <strong>{packages.length} 个可用语音包</strong>
        </div>
        <Button type="primary" icon={<Plus size={15} />} onClick={openCreate}>添加语音包</Button>
      </header>

      {error && !dialogOpen && <div className="settings-error">{error}</div>}
      <section className="voice-package-list" aria-busy={loading}>
        {loading ? (
          <div className="settings-empty">正在加载语音包</div>
        ) : packages.length === 0 ? (
          <div className="voice-package-empty">
            <AudioLines size={28} />
            <strong>暂无可用语音包</strong>
          </div>
        ) : packages.map((voicePackage) => (
          <article className="voice-package-row" key={voicePackage.id}>
            <span className="voice-package-mark"><AudioLines size={18} /></span>
            <div className="voice-package-identity">
              <strong>{voicePackage.name}</strong>
              <span>{voicePackage.engine === 'cosyvoice' ? 'CosyVoice' : 'GPT-SoVITS'} · {voicePackage.language} · {voicePackage.dialect} · {voicePackage.baseModelVersion}</span>
              <small>{voicePackage.speakingStyle || voicePackage.description}</small>
            </div>
            <div className="voice-package-license">
              <span>v{voicePackage.version}</span>
              <small>{voicePackage.license}</small>
            </div>
            <audio controls preload="none" src={voicePackage.referenceAudioUrl} />
            <div className="voice-package-actions">
              {voicePackage.sourceUrl && (
                <Button type="text" icon={<ExternalLink size={15} />} href={voicePackage.sourceUrl} target="_blank" aria-label="打开来源" />
              )}
              <Button type="text" icon={<Pencil size={15} />} onClick={() => openEdit(voicePackage)} aria-label="编辑语音包" />
              <Popconfirm title="停用这个语音包？" description="已绑定旧版本的角色不受影响。" onConfirm={() => archive(voicePackage)}>
                <Button type="text" danger icon={<Trash2 size={15} />} aria-label="停用语音包" />
              </Popconfirm>
            </div>
          </article>
        ))}
      </section>

      <Modal
        open={dialogOpen}
        title={editing ? `更新 ${editing.name}` : '添加语音包'}
        footer={null}
        width={760}
        destroyOnHidden
        onCancel={() => !saving && setDialogOpen(false)}
      >
        <form className="voice-package-form" onSubmit={save}>
          {error && <div className="settings-error voice-package-form-error">{error}</div>}
          <label><span>名称</span><input required maxLength={200} value={editor.name} onChange={(event) => setEditor((current) => ({ ...current, name: event.target.value }))} /></label>
          <label><span>TTS 引擎</span><select value={editor.engine} onChange={(event) => selectEngine(event.target.value as SaveVoicePackageInput['engine'])}><option value="gpt-sovits">GPT-SoVITS</option><option value="cosyvoice">CosyVoice 3 0.5B</option></select></label>
          <label><span>{editor.engine === 'cosyvoice' ? '模型' : '底模版本'}</span>{editor.engine === 'cosyvoice' ? <select value={editor.baseModelVersion} onChange={(event) => setEditor((current) => ({ ...current, baseModelVersion: event.target.value }))}><option value={cosyVoice3Model}>CosyVoice 3 0.5B</option></select> : <select value={editor.baseModelVersion} onChange={(event) => setEditor((current) => ({ ...current, baseModelVersion: event.target.value }))}><option>v1</option><option>v2</option><option>v3</option><option>v4</option><option>v2Pro</option><option>v2ProPlus</option></select>}</label>
          <label><span>语言</span><input required maxLength={40} value={editor.language} onChange={(event) => setEditor((current) => ({ ...current, language: event.target.value }))} /></label>
          <label><span>方言 / 口音</span><input required maxLength={100} value={editor.dialect} onChange={(event) => setEditor((current) => ({ ...current, dialect: event.target.value }))} /></label>
          {editor.engine === 'gpt-sovits' && <>
            <label className="span-2"><span>GPT 权重路径</span><input required maxLength={1000} value={editor.gptWeightsPath} onChange={(event) => setEditor((current) => ({ ...current, gptWeightsPath: event.target.value }))} /></label>
            <label className="span-2"><span>SoVITS 权重路径</span><input required maxLength={1000} value={editor.soVitsWeightsPath} onChange={(event) => setEditor((current) => ({ ...current, soVitsWeightsPath: event.target.value }))} /></label>
          </>}
          <label className="span-2"><span>参考文本</span><textarea required maxLength={2000} rows={3} value={editor.referenceText} onChange={(event) => setEditor((current) => ({ ...current, referenceText: event.target.value }))} /></label>
          <label className="span-2"><span>说话习惯</span><textarea maxLength={2000} rows={2} value={editor.speakingStyle} onChange={(event) => setEditor((current) => ({ ...current, speakingStyle: event.target.value }))} /></label>
          <label><span>默认语速</span><input type="number" min="0.5" max="2" step="0.05" value={editor.defaultSpeed} onChange={(event) => setEditor((current) => ({ ...current, defaultSpeed: Number(event.target.value) }))} /></label>
          <label><span>许可证</span><input required maxLength={200} value={editor.license} onChange={(event) => setEditor((current) => ({ ...current, license: event.target.value }))} /></label>
          <label className="span-2"><span>来源 URL</span><input type="url" maxLength={2000} value={editor.sourceUrl} onChange={(event) => setEditor((current) => ({ ...current, sourceUrl: event.target.value }))} /></label>
          <label className="span-2"><span>描述</span><textarea maxLength={2000} rows={2} value={editor.description} onChange={(event) => setEditor((current) => ({ ...current, description: event.target.value }))} /></label>
          <label className="voice-package-upload span-2">
            <span>参考 WAV {editing && <small>不选择则沿用 v{editing.version}</small>}</span>
            <span className="voice-package-file"><Upload size={15} />{editor.referenceAudio?.name || editing?.referenceAudioFileName || '选择 WAV 文件'}</span>
            <input type="file" accept="audio/wav,.wav" required={!editing} onChange={(event) => setEditor((current) => ({ ...current, referenceAudio: event.target.files?.[0] ?? null }))} />
          </label>
          <footer>
            <Button onClick={() => setDialogOpen(false)} disabled={saving}>取消</Button>
            <Button type="primary" htmlType="submit" loading={saving} icon={<Save size={15} />}>{editing ? '保存为新版本' : '创建语音包'}</Button>
          </footer>
        </form>
      </Modal>
    </div>
  )
}
