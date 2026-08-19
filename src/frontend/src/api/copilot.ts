export interface CopilotMessage {
  id: string
  sequence: number
  role: 'user' | 'assistant'
  content: string
  model: string | null
  createdAtUtc: string
}

export interface CopilotConversation {
  conversationId: string | null
  projectId: string
  runtime: string
  messages: CopilotMessage[]
}

async function readError(response: Response, fallback: string): Promise<Error> {
  const body = await response.json().catch(() => null) as {
    error?: string
    detail?: string
    title?: string
  } | null
  return new Error(body?.error || body?.detail || body?.title || fallback)
}

export async function getCopilotConversation(
  projectId: string,
  signal?: AbortSignal,
): Promise<CopilotConversation> {
  const response = await fetch(`/api/v2/projects/${projectId}/copilot/messages`, { signal })
  if (!response.ok) throw await readError(response, '副导演会话加载失败。')
  return response.json() as Promise<CopilotConversation>
}

export async function sendCopilotMessage(
  projectId: string,
  input: { content: string; page: string; episode: string },
): Promise<CopilotConversation> {
  const response = await fetch(`/api/v2/projects/${projectId}/copilot/messages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw await readError(response, 'AI 副导演暂时无法回复。')
  return response.json() as Promise<CopilotConversation>
}

export async function resetCopilotConversation(projectId: string): Promise<void> {
  const response = await fetch(`/api/v2/projects/${projectId}/copilot/messages`, {
    method: 'DELETE',
  })
  if (!response.ok) throw await readError(response, '副导演会话清空失败。')
}