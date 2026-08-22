export const assistantDirectorAgent = {
  id: '9b695559-9d9d-492d-8ee7-f1a76438b20c',
  name: '副导演',
} as const

export interface SessionMessage {
  id: string
  sequence: number
  role: 'user' | 'assistant'
  content: string
  model: string | null
  createdAtUtc: string
}

export interface SessionRecord {
  id: string
  agentId: string
  agentName: string
  scopeKey: string
  projectId: string | null
  projectName: string | null
  title: string
  runtime: string
  createdAtUtc: string
  updatedAtUtc: string
  messages: SessionMessage[]
}

export interface SessionSummary {
  id: string
  agentId: string
  agentName: string
  scopeKey: string
  projectId: string | null
  projectName: string | null
  title: string
  runtime: string
  messageCount: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface SessionAgentTask {
  id: string
  sessionId: string | null
  status: 'queued' | 'running' | 'cancellation-requested' | 'completed' | 'failed' | 'cancelled'
  currentStep: string | null
  lastError: string | null
  createdAtUtc: string
  updatedAtUtc: string
  completedAtUtc: string | null
}

export interface SessionAgentTaskEvent {
  sequence: number
  eventType: string
  stage: string | null
  message: string
  dataJson: string | null
  createdAtUtc: string
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const body = await response.json().catch(() => null) as {
    error?: string
    detail?: string
    title?: string
  } | null
  return new Error(body?.error || body?.detail || body?.title || fallback)
}

export async function getScopedSession(
  agentId: string,
  scopeKey: string,
  signal?: AbortSignal,
): Promise<SessionRecord | null> {
  const query = new URLSearchParams({ agentId, scopeKey })
  const response = await fetch(`/api/v2/sessions/scoped?${query}`, { signal })
  if (response.status === 404) return null
  if (!response.ok) throw await readError(response, '副导演会话加载失败。')
  return response.json() as Promise<SessionRecord>
}

export async function sendSessionMessage(input: {
  agentId: string
  scopeKey: string
  sessionId?: string
  projectId?: string
  title?: string
  content: string
  page: string
  episode: string
}): Promise<SessionRecord> {
  const response = await fetch('/api/v2/sessions/messages', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, 'AI 副导演暂时无法回复。')
  return response.json() as Promise<SessionRecord>
}

export async function enqueueSessionMessage(input: {
  agentId: string
  scopeKey: string
  sessionId?: string
  projectId?: string
  title?: string
  content: string
  page: string
  episode: string
}): Promise<SessionAgentTask> {
  const response = await fetch('/api/v2/sessions/messages/async', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, '副导演任务创建失败。')
  return response.json() as Promise<SessionAgentTask>
}

export async function getSessionAgentTask(taskId: string): Promise<SessionAgentTask> {
  const response = await fetch(`/api/v2/sessions/agent-tasks/${taskId}`)
  if (!response.ok) throw await readError(response, '副导演任务加载失败。')
  return response.json() as Promise<SessionAgentTask>
}

export async function stopSessionAgentTask(taskId: string): Promise<SessionAgentTask> {
  const response = await fetch(`/api/v2/sessions/agent-tasks/${taskId}/stop`, { method: 'POST' })
  if (!response.ok) throw await readError(response, '副导演任务停止失败。')
  return response.json() as Promise<SessionAgentTask>
}

export function subscribeSessionAgentTask(
  taskId: string,
  after: number,
  onEvent: (event: SessionAgentTaskEvent) => void,
  onDisconnected: () => void,
): EventSource {
  const source = new EventSource(`/api/v2/sessions/agent-tasks/${taskId}/events?after=${after}`)
  for (const eventType of ['status', 'progress', 'tool', 'result', 'failure']) {
    source.addEventListener(eventType, (event) => {
      onEvent(JSON.parse((event as MessageEvent<string>).data) as SessionAgentTaskEvent)
    })
  }
  source.onerror = () => {
    source.close()
    onDisconnected()
  }
  return source
}

export async function resetSession(sessionId: string): Promise<void> {
  const response = await fetch(`/api/v2/sessions/${sessionId}/messages`, { method: 'DELETE' })
  if (!response.ok) throw await readError(response, '副导演会话清空失败。')
}

export async function retrySessionMessage(
  sessionId: string,
  messageId: string,
  context: { page: string; episode: string },
): Promise<SessionRecord> {
  const response = await fetch(
    `/api/v2/sessions/${sessionId}/messages/${messageId}/retry`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(context),
    },
  )
  if (!response.ok) throw await readError(response, '副导演消息重试失败。')
  return response.json() as Promise<SessionRecord>
}

export async function listSessions(signal?: AbortSignal): Promise<SessionSummary[]> {
  const response = await fetch('/api/v2/sessions', { signal })
  if (!response.ok) throw await readError(response, 'Session 列表加载失败。')
  return response.json() as Promise<SessionSummary[]>
}

export async function getSession(sessionId: string, signal?: AbortSignal): Promise<SessionRecord> {
  const response = await fetch(`/api/v2/sessions/${sessionId}`, { signal })
  if (!response.ok) throw await readError(response, 'Session 详情加载失败。')
  return response.json() as Promise<SessionRecord>
}
