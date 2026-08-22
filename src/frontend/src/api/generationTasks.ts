export interface GenerationTask {
  id: string
  taskType: string
  status: 'queued' | 'running' | 'completed' | 'failed' | 'cancelled'
  currentStep: string | null
  lastError: string | null
  progressCompleted: number
  progressTotal: number | null
  resultJson: string | null
}

const terminalStatuses = new Set<GenerationTask['status']>(['completed', 'failed', 'cancelled'])

export async function waitForGenerationResult<T>(task: GenerationTask): Promise<T> {
  let current = task
  while (!terminalStatuses.has(current.status)) {
    await new Promise(resolve => window.setTimeout(resolve, 750))
    const response = await fetch(`/api/v2/tasks/${current.id}`)
    if (!response.ok) throw new Error('生成任务状态加载失败。')
    current = await response.json() as GenerationTask
  }
  if (current.status !== 'completed') {
    throw new Error(current.lastError || (current.status === 'cancelled' ? '生成任务已取消。' : '生成任务失败。'))
  }
  if (!current.resultJson) throw new Error('生成任务没有返回结果。')
  return JSON.parse(current.resultJson) as T
}