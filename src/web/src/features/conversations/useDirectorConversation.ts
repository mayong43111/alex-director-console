import { useEffect, useState } from 'react'
import { deleteConversationFrom, listConversationMessages } from '../../api/conversations'
import { consumeNdjsonStream } from '../../api/streamProtocol'
import type {
  AssetRecord,
  ConversationMessageRecord,
  MessageStreamEvent,
  Project,
} from '../../models'

interface UseDirectorConversationOptions {
  project?: Project
  agentConfigured: boolean
  selectedAsset: AssetRecord | null
  aspectRatio: string
  resolution: string
  imageSize: string
  onMessageStart: () => void
  onAssetGenerated: (asset: AssetRecord) => void
  onCompleted: (event: MessageStreamEvent, sourceShot: AssetRecord | null) => void
}

export function useDirectorConversation({
  project,
  agentConfigured,
  selectedAsset,
  aspectRatio,
  resolution,
  imageSize,
  onMessageStart,
  onAssetGenerated,
  onCompleted,
}: UseDirectorConversationOptions) {
  const [messages, setMessages] = useState<ConversationMessageRecord[]>([])
  const [messagesLoading, setMessagesLoading] = useState(false)
  const [sendingMessage, setSendingMessage] = useState(false)
  const [conversationError, setConversationError] = useState<string | null>(null)

  useEffect(() => {
    if (!project) return

    const controller = new AbortController()
    setMessages([])
    setMessagesLoading(true)
    setConversationError(null)

    listConversationMessages(project.id, controller.signal)
      .then(setMessages)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setConversationError(error instanceof Error ? error.message : '对话历史加载失败')
      })
      .finally(() => {
        if (!controller.signal.aborted) setMessagesLoading(false)
      })

    return () => controller.abort()
  }, [project])

  async function retryDirectorMessage(message: ConversationMessageRecord) {
    if (!project || sendingMessage || message.role !== 'user') return

    setConversationError(null)
    try {
      await deleteConversationFrom(project.id, message.id)
      setMessages((current) => {
        const messageIndex = current.findIndex((item) => item.id === message.id)
        return messageIndex < 0 ? current : current.slice(0, messageIndex)
      })
      await sendDirectorMessage(message.content, message.model)
    } catch (error) {
      setConversationError(error instanceof Error ? error.message : '重试失败')
    }
  }

  async function sendDirectorMessage(message: string, model: string, assetId?: string) {
    if (!project || !agentConfigured) return

    const sourceShot = selectedAsset?.type === 'shot' ? selectedAsset : null
    onMessageStart()
    setSendingMessage(true)
    setConversationError(null)
    const now = new Date().toISOString()
    const temporaryUserId = `pending-user-${crypto.randomUUID()}`
    const temporaryAssistantId = `pending-assistant-${crypto.randomUUID()}`
    const temporaryUser: ConversationMessageRecord = {
      id: temporaryUserId,
      projectId: project.id,
      role: 'user',
      content: message,
      model,
      createdAtUtc: now,
    }
    const temporaryAssistant: ConversationMessageRecord = {
      id: temporaryAssistantId,
      projectId: project.id,
      role: 'assistant',
      content: '',
      model,
      createdAtUtc: now,
      processEvents: [],
      isStreaming: true,
    }
    setMessages((current) => [...current, temporaryUser, temporaryAssistant])

    try {
      const response = await fetch(`/api/projects/${project.id}/messages/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message,
          model,
          assetId,
          projectAspectRatio: aspectRatio,
          projectResolution: resolution,
          imageSize,
          projectName: project.name,
          projectDescription: project.description,
          previewResolution: project.previewResolution,
          imageModel: project.imageModel,
          videoModel: project.videoModel,
        }),
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as {
          detail?: string
          error?: string
        } | null
        throw new Error(problem?.detail ?? problem?.error ?? '执行副导演响应失败')
      }
      if (!response.body) throw new Error('浏览器未提供流式响应')

      let completed = false
      const consumeEvent = (streamEvent: MessageStreamEvent) => {
        if (streamEvent.type === 'process' || streamEvent.type === 'message.accepted') {
          const generatedAsset = streamEvent.data?.asset
          setMessages((current) => current.map((item) => item.id === temporaryAssistantId
            ? {
                ...item,
                processEvents: [
                  ...(item.processEvents ?? []),
                  {
                    stage: streamEvent.stage ?? streamEvent.type,
                    message: streamEvent.message ?? '处理中',
                    data: streamEvent.data,
                  },
                ],
                generatedAssets: generatedAsset
                  ? [
                      ...(item.generatedAssets ?? []).filter((asset) => asset.id !== generatedAsset.id),
                      generatedAsset,
                    ]
                  : item.generatedAssets,
              }
            : item))
          if (generatedAsset) onAssetGenerated(generatedAsset)
          return
        }

        if (streamEvent.type === 'assistant.delta' && streamEvent.delta) {
          setMessages((current) => current.map((item) => item.id === temporaryAssistantId
            ? { ...item, content: item.content + streamEvent.delta }
            : item))
          return
        }

        if (
          streamEvent.type === 'completed'
          && streamEvent.userMessage
          && streamEvent.assistantMessage
        ) {
          completed = true
          setMessages((current) => current.map((item) => {
            if (item.id === temporaryUserId) return streamEvent.userMessage!
            if (item.id === temporaryAssistantId) {
              return {
                ...streamEvent.assistantMessage!,
                processEvents: item.processEvents,
                generatedAssets: streamEvent.generatedAssets,
                isStreaming: false,
              }
            }
            return item
          }))
          onCompleted(streamEvent, sourceShot)
          return
        }

        if (streamEvent.type === 'error') {
          throw new Error(streamEvent.detail ?? streamEvent.message ?? '执行副导演响应失败')
        }
      }

      await consumeNdjsonStream<MessageStreamEvent>(response.body, consumeEvent)
      if (!completed) throw new Error('流式响应在完成前中断')
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : '执行副导演响应失败'
      setConversationError(errorMessage)
      setMessages((current) => current.map((item) => item.id === temporaryAssistantId
        ? {
            ...item,
            content: item.content || '本次执行未完成。',
            isStreaming: false,
            processEvents: [
              ...(item.processEvents ?? []),
              { stage: 'error', message: errorMessage },
            ],
          }
        : item))
    } finally {
      setSendingMessage(false)
    }
  }

  return {
    messages,
    messagesLoading,
    sendingMessage,
    conversationError,
    sendDirectorMessage,
    retryDirectorMessage,
  }
}
