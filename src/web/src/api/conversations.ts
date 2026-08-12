import type { ConversationMessageRecord } from '../models'

export async function listConversationMessages(
  projectId: string,
  signal?: AbortSignal,
): Promise<ConversationMessageRecord[]> {
  const response = await fetch(`/api/projects/${projectId}/messages`, { signal })
  if (!response.ok) throw new Error('对话历史加载失败')
  return response.json() as Promise<ConversationMessageRecord[]>
}

export async function deleteConversationFrom(projectId: string, messageId: string): Promise<void> {
  const response = await fetch(
    `/api/projects/${projectId}/messages/${messageId}/following`,
    { method: 'DELETE' },
  )
  if (response.ok) return

  const problem = await response.json().catch(() => null) as { error?: string } | null
  throw new Error(problem?.error ?? '无法清理后续对话')
}
