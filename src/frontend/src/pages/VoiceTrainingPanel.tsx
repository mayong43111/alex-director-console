import { useEffect, useState, type FormEvent } from 'react'
import { Button, Modal, Popconfirm, Progress, Tag } from 'antd'
import { AudioLines, PackageCheck, Play, Plus, RefreshCw, ShieldAlert, Trash2, Upload } from 'lucide-react'
import {
  createVoiceTrainingJob,
  deleteVoiceTrainingSample,
  listVoiceTrainingJobs,
  registerTrainedVoicePackage,
  startVoiceTraining,
  syncVoiceTraining,
  uploadVoiceTrainingSample,
  type CreateVoiceTrainingJobInput,
  type VoiceTrainingJob,
} from '../api/systemConfiguration'

const emptyJob: CreateVoiceTrainingJobInput = {
  name: '',
  trainingMode: 'replica',
  baseModelVersion: 'v2ProPlus',
  language: 'zh',
  dialect: '普通话',
  speakingStyle: '',
  defaultSpeed: 1,
  sourceDescription: '',
  rightsConfirmed: false,
}

function statusLabel(status: VoiceTrainingJob['status']) {
  return ({
    draft: '准备数据',
    queued: '排队中',
    running: '训练中',
    completed: '训练完成',
    failed: '训练失败',
  } as const)[status]
}

function durationLabel(seconds: number) {
  const minutes = Math.floor(seconds / 60)
  const remainder = Math.round(seconds % 60).toString().padStart(2, '0')
  return `${minutes}:${remainder}`
}

export function VoiceTrainingPanel() {
  const [jobs, setJobs] = useState<VoiceTrainingJob[]>([])
  const [editor, setEditor] = useState<CreateVoiceTrainingJobInput>({ ...emptyJob })
  const [createOpen, setCreateOpen] = useState(false)
  const [sampleJob, setSampleJob] = useState<VoiceTrainingJob | null>(null)
  const [sampleFile, setSampleFile] = useState<File | null>(null)
  const [transcript, setTranscript] = useState('')
  const [busy, setBusy] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = async (signal?: AbortSignal) => {
    try {
      setJobs(await listVoiceTrainingJobs(signal))
      setError('')
    } catch (loadError) {
      if (loadError instanceof DOMException && loadError.name === 'AbortError') return
      setError(loadError instanceof Error ? loadError.message : '音色训练任务加载失败。')
    } finally {
      if (!signal?.aborted) setLoading(false)
    }
  }

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [])

  const create = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setBusy('create')
    setError('')
    try {
      await createVoiceTrainingJob(editor)
      setCreateOpen(false)
      setEditor({ ...emptyJob })
      await load()
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : '音色训练任务创建失败。')
    } finally {
      setBusy(null)
    }
  }

  const uploadSample = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!sampleJob || !sampleFile) return
    setBusy(`sample-${sampleJob.id}`)
    setError('')
    try {
      await uploadVoiceTrainingSample(sampleJob.id, sampleFile, transcript)
      setSampleJob(null)
      setSampleFile(null)
      setTranscript('')
      await load()
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : '训练样本上传失败。')
    } finally {
      setBusy(null)
    }
  }

  const run = async (job: VoiceTrainingJob, action: 'start' | 'sync' | 'register') => {
    setBusy(`${action}-${job.id}`)
    setError('')
    try {
      if (action === 'start') await startVoiceTraining(job.id)
      else if (action === 'sync') await syncVoiceTraining(job.id)
      else await registerTrainedVoicePackage(job.id)
      await load()
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : '音色训练操作失败。')
      await load()
    } finally {
      setBusy(null)
    }
  }

  const removeSample = async (jobId: string, sampleId: string) => {
    setBusy(`delete-${sampleId}`)
    setError('')
    try {
      await deleteVoiceTrainingSample(jobId, sampleId)
      await load()
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : '训练样本删除失败。')
    } finally {
      setBusy(null)
    }
  }

  return (
    <section className="voice-training-panel" aria-busy={loading}>
      <div className="voice-training-actions">
        <span>{jobs.length} 个训练任务</span>
        <Button type="primary" icon={<Plus size={15} />} onClick={() => setCreateOpen(true)}>新建训练</Button>
      </div>
      {error && <div className="settings-error">{error}</div>}
      {loading ? (
        <div className="settings-empty">正在加载训练任务</div>
      ) : jobs.length === 0 ? (
        <div className="voice-package-empty"><AudioLines size={28} /><strong>暂无音色训练任务</strong></div>
      ) : (
        <div className="voice-training-list">
          {jobs.map((job) => (
            <article className="voice-training-row" key={job.id}>
              <header>
                <div className="voice-training-title">
                  <strong>{job.name}</strong>
                  <span>GPT-SoVITS {job.baseModelVersion} · {job.dialect} · {job.defaultSpeed.toFixed(2)}x</span>
                </div>
                <div className="voice-training-badges">
                  <Tag color={job.status === 'failed' ? 'error' : job.status === 'completed' ? 'success' : 'processing'}>{statusLabel(job.status)}</Tag>
                  {!job.canExport && <Tag color="warning" icon={<ShieldAlert size={12} />}>仅练习 · 禁止导出</Tag>}
                </div>
              </header>
              <div className="voice-training-metrics">
                <span><b>{job.sampleCount}</b> 条样本</span>
                <span><b>{durationLabel(job.totalDurationSeconds)}</b> 总时长</span>
                <span><b>{job.defaultSpeed.toFixed(2)}x</b> 默认语速</span>
              </div>
              {job.speakingStyle && <p className="voice-training-style">{job.speakingStyle}</p>}
              {(job.status === 'queued' || job.status === 'running') && <Progress percent={job.progressPercent} size="small" />}
              {job.error && <div className="settings-error">{job.error}</div>}
              {job.samples.length > 0 && (
                <div className="voice-training-samples">
                  {job.samples.map((sample) => (
                    <div className="voice-training-sample" key={sample.id}>
                      <audio controls preload="none" src={sample.contentUrl} />
                      <div><strong>{sample.fileName}</strong><span>{durationLabel(sample.durationSeconds)} · {sample.transcript}</span></div>
                      {(job.status === 'draft' || job.status === 'failed') && (
                        <Popconfirm title="删除这个训练样本？" onConfirm={() => removeSample(job.id, sample.id)}>
                          <Button type="text" danger icon={<Trash2 size={14} />} loading={busy === `delete-${sample.id}`} aria-label="删除训练样本" />
                        </Popconfirm>
                      )}
                    </div>
                  ))}
                </div>
              )}
              <footer>
                {(job.status === 'draft' || job.status === 'failed') && <Button icon={<Upload size={14} />} onClick={() => setSampleJob(job)}>添加样本</Button>}
                {(job.status === 'draft' || job.status === 'failed') && <Button type="primary" icon={<Play size={14} />} disabled={!job.canStart} loading={busy === `start-${job.id}`} onClick={() => run(job, 'start')}>开始训练</Button>}
                {(job.status === 'queued' || job.status === 'running') && <Button icon={<RefreshCw size={14} />} loading={busy === `sync-${job.id}`} onClick={() => run(job, 'sync')}>同步状态</Button>}
                {job.status === 'completed' && !job.voicePackageId && <Button type="primary" icon={<PackageCheck size={14} />} loading={busy === `register-${job.id}`} onClick={() => run(job, 'register')}>注册语音包</Button>}
                {job.voicePackageId && <Tag color="success" icon={<PackageCheck size={12} />}>已注册语音包</Tag>}
                {!job.canStart && (job.status === 'draft' || job.status === 'failed') && <span className="voice-training-requirement">至少 3 条、合计 1:00</span>}
              </footer>
            </article>
          ))}
        </div>
      )}

      <Modal open={createOpen} title="新建音色训练" footer={null} width={720} destroyOnHidden onCancel={() => busy !== 'create' && setCreateOpen(false)}>
        <form className="voice-package-form" onSubmit={create}>
          <label><span>音色名称</span><input required maxLength={200} value={editor.name} onChange={(event) => setEditor((current) => ({ ...current, name: event.target.value }))} /></label>
          <label><span>训练模式</span><select value={editor.trainingMode} onChange={(event) => setEditor((current) => ({ ...current, trainingMode: event.target.value as 'original' | 'replica' }))}><option value="replica">练习复刻（禁止导出）</option><option value="original">原创 / 已授权</option></select></label>
          <label><span>底模版本</span><select value={editor.baseModelVersion} onChange={(event) => setEditor((current) => ({ ...current, baseModelVersion: event.target.value }))}><option>v2ProPlus</option><option>v2Pro</option><option>v4</option><option>v3</option><option>v2</option><option>v1</option></select></label>
          <label><span>默认语速</span><input type="number" min="0.5" max="2" step="0.05" value={editor.defaultSpeed} onChange={(event) => setEditor((current) => ({ ...current, defaultSpeed: Number(event.target.value) }))} /></label>
          <label><span>语言</span><input required maxLength={40} value={editor.language} onChange={(event) => setEditor((current) => ({ ...current, language: event.target.value }))} /></label>
          <label><span>方言 / 口音</span><input required maxLength={100} value={editor.dialect} onChange={(event) => setEditor((current) => ({ ...current, dialect: event.target.value }))} /></label>
          <label className="span-2"><span>说话习惯</span><textarea maxLength={2000} rows={3} value={editor.speakingStyle} onChange={(event) => setEditor((current) => ({ ...current, speakingStyle: event.target.value }))} /></label>
          <label className="span-2"><span>训练来源</span><textarea maxLength={2000} rows={2} value={editor.sourceDescription} onChange={(event) => setEditor((current) => ({ ...current, sourceDescription: event.target.value }))} /></label>
          <label className="voice-training-consent span-2"><input type="checkbox" checked={editor.rightsConfirmed} onChange={(event) => setEditor((current) => ({ ...current, rightsConfirmed: event.target.checked }))} /><span>确认训练素材在所选范围内获准使用；复刻任务不得导出或发布。</span></label>
          <footer><Button onClick={() => setCreateOpen(false)} disabled={busy === 'create'}>取消</Button><Button type="primary" htmlType="submit" loading={busy === 'create'} disabled={!editor.rightsConfirmed}>创建训练任务</Button></footer>
        </form>
      </Modal>

      <Modal open={Boolean(sampleJob)} title={`添加训练样本${sampleJob ? ` · ${sampleJob.name}` : ''}`} footer={null} width={620} destroyOnHidden onCancel={() => !busy?.startsWith('sample-') && setSampleJob(null)}>
        <form className="voice-package-form" onSubmit={uploadSample}>
          <label className="voice-package-upload span-2"><span>单人干净 WAV</span><span className="voice-package-file"><Upload size={15} />{sampleFile?.name || '选择 WAV 文件'}</span><input type="file" accept="audio/wav,.wav" required onChange={(event) => setSampleFile(event.target.files?.[0] ?? null)} /></label>
          <label className="span-2"><span>准确文本</span><textarea required maxLength={2000} rows={4} value={transcript} onChange={(event) => setTranscript(event.target.value)} /></label>
          <footer><Button onClick={() => setSampleJob(null)} disabled={Boolean(busy)}>取消</Button><Button type="primary" htmlType="submit" loading={Boolean(sampleJob && busy === `sample-${sampleJob.id}`)} disabled={!sampleFile || !transcript.trim()}>上传样本</Button></footer>
        </form>
      </Modal>
    </section>
  )
}
