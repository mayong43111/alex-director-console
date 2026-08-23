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

export interface GenerationTaskEvent {
  sequence: number
  eventType: string
  stage: string | null
  message: string
  dataJson: string | null
  createdAtUtc: string
}

const terminalStatuses = new Set<GenerationTask['status']>(['completed', 'failed', 'cancelled'])

export async function getGenerationTask(taskId: string): Promise<GenerationTask> {
  const response = await fetch(`/api/v2/tasks/${taskId}`)
  if (!response.ok) throw new Error('生成任务状态加载失败。')
  return response.json() as Promise<GenerationTask>
}

export function subscribeGenerationTask(
  taskId: string,
  after: number,
  onEvent: (event: GenerationTaskEvent) => void,
  onDisconnected: () => void,
): EventSource {
  const source = new EventSource(`/api/v2/tasks/${taskId}/events?after=${after}`)
  for (const eventType of ['status', 'progress', 'result', 'failure']) {
    source.addEventListener(eventType, (event) => {
      onEvent(JSON.parse((event as MessageEvent<string>).data) as GenerationTaskEvent)
    })
  }
  source.onerror = () => {
    source.close()
    onDisconnected()
  }
  return source
}

export async function waitForGenerationResult<T>(task: GenerationTask): Promise<T> {
  let current = task
  while (!terminalStatuses.has(current.status)) {
    await new Promise(resolve => window.setTimeout(resolve, 750))
    current = await getGenerationTask(current.id)
  }
  if (current.status !== 'completed') {
    throw new Error(current.lastError || (current.status === 'cancelled' ? '生成任务已取消。' : '生成任务失败。'))
  }
  if (!current.resultJson) throw new Error('生成任务没有返回结果。')
  return JSON.parse(current.resultJson) as T
}

export async function cancelGenerationTask(taskId: string): Promise<GenerationTask> {
  const response = await fetch(`/api/v2/tasks/${taskId}/cancel`, { method: 'POST' })
  if (!response.ok) throw new Error('停止生成任务失败。')
  return response.json() as Promise<GenerationTask>
}