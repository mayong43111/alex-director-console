import { useEffect, useRef, useState, type FormEvent } from "react";
import { Bot, RotateCcw, SendHorizontal, Sparkles, X } from "lucide-react";
import {
  getScopedSession,
  resetSession,
  retrySessionMessage,
  sendSessionMessage,
  type SessionMessage,
  type SessionRecord,
} from "../api/sessions";

export interface AssistantDirectorAgent {
  id: string;
  name: string;
}

export interface AssistantDirectorSession {
  scopeKey: string;
  projectId?: string;
  title: string;
  page: string;
  episode?: string;
}

export function AssistantDirectorPanel({
  agent,
  session,
  onClose,
}: {
  agent: AssistantDirectorAgent;
  session: AssistantDirectorSession;
  onClose?: () => void;
}) {
  const [message, setMessage] = useState("");
  const [activeSession, setActiveSession] = useState<SessionRecord | null>(null);
  const [messages, setMessages] = useState<SessionMessage[]>([]);
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const scrollAnchor = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    getScopedSession(agent.id, session.scopeKey, controller.signal)
      .then((loadedSession) => {
        setActiveSession(loadedSession);
        setMessages(loadedSession?.messages ?? []);
      })
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "副导演会话加载失败。");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [agent.id, session.scopeKey]);

  useEffect(() => {
    scrollAnchor.current?.scrollIntoView({ block: "end" });
  }, [messages, sending]);

  async function sendMessage(content: string) {
    const trimmed = content.trim();
    if (!trimmed || sending) return;

    const pendingId = `pending-${Date.now()}`;
    setSending(true);
    setError(null);
    setMessage("");
    setMessages((current) => [
      ...current,
      {
        id: pendingId,
        sequence: current.length + 1,
        role: "user",
        content: trimmed,
        model: null,
        createdAtUtc: new Date().toISOString(),
      },
    ]);
    try {
      const updatedSession = await sendSessionMessage({
        agentId: agent.id,
        scopeKey: session.scopeKey,
        sessionId: activeSession?.id,
        projectId: session.projectId,
        title: session.title,
        content: trimmed,
        page: session.page,
        episode: session.episode ?? "未选择",
      });
      setActiveSession(updatedSession);
      setMessages(updatedSession.messages);
    } catch (sendError) {
      setMessages((current) => current.filter((item) => item.id !== pendingId));
      setMessage(trimmed);
      setError(sendError instanceof Error ? sendError.message : "AI 副导演暂时无法回复。");
    } finally {
      setSending(false);
    }
  }

  async function clearConversation() {
    if (sending || !activeSession) return;
    setError(null);
    try {
      await resetSession(activeSession.id);
      setActiveSession({ ...activeSession, messages: [] });
      setMessages([]);
    } catch (resetError) {
      setError(resetError instanceof Error ? resetError.message : "副导演会话清空失败。");
    }
  }

  async function retryMessage(messageId: string) {
    if (sending || !activeSession) return;
    setSending(true);
    setError(null);
    try {
      const updatedSession = await retrySessionMessage(activeSession.id, messageId, {
        page: session.page,
        episode: session.episode ?? "未选择",
      });
      setActiveSession(updatedSession);
      setMessages(updatedSession.messages);
    } catch (retryError) {
      setError(retryError instanceof Error ? retryError.message : "副导演消息重试失败。");
    } finally {
      setSending(false);
    }
  }

  function submitMessage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void sendMessage(message);
  }

  return (
    <aside className="agent-panel">
      <header className="agent-header">
        <div className="agent-header-main">
          <div className="agent-identity">
            <Bot size={18} />
            <strong>{agent.name}</strong>
            <span className="online-dot" />
          </div>
          <div className="agent-header-actions">
            <button
              className="icon-button"
              onClick={clearConversation}
              disabled={sending || messages.length === 0}
              aria-label="清空副导演会话"
              title="清空副导演会话"
            >
              <RotateCcw size={16} />
            </button>
            {onClose && (
              <button className="icon-button" onClick={onClose} aria-label="关闭副导演">
                <X size={17} />
              </button>
            )}
          </div>
        </div>
      </header>
      <div className="agent-content">
        {loading ? (
          <div className="agent-loading"><span className="spinner" />正在加载会话...</div>
        ) : messages.length === 0 ? (
          <div className="agent-empty">
            <div className="agent-avatar"><Sparkles size={16} /></div>
            <strong>副导演已就绪</strong>
            <p>可以查询、创建和更新项目。项目删除需要在项目中心手动操作。</p>
            <button onClick={() => void sendMessage("请列出当前项目，并指出最值得优先处理的事项。")}>查看项目概况</button>
            <button onClick={() => void sendMessage("请根据当前上下文给出下一步建议，不要声称已经执行。")}>建议下一步</button>
          </div>
        ) : (
          <div className="agent-messages">
            {messages.map((item) => (
              <article className={`agent-message ${item.role}`} key={item.id}>
                <header>
                  <span>{item.role === "user" ? "导演" : "副导演"}</span>
                  {item.role === "user" && (
                    <button
                      className="agent-message-retry"
                      onClick={() => void retryMessage(item.id)}
                      disabled={sending}
                      aria-label="从这条消息重试"
                      title="从这条消息重试；已执行的项目操作不会撤销"
                    >
                      <RotateCcw size={12} />
                    </button>
                  )}
                </header>
                <p>{item.content}</p>
              </article>
            ))}
            {sending && (
              <div className="agent-thinking"><span className="spinner" />副导演正在思考...</div>
            )}
          </div>
        )}
        {error && <p className="agent-error">{error}</p>}
        <div ref={scrollAnchor} />
      </div>
      <form className="composer" onSubmit={submitMessage}>
        <div className="composer-input">
          <textarea
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                event.currentTarget.form?.requestSubmit();
              }
            }}
            placeholder="向副导演提问…"
            disabled={loading || sending}
          />
          <button
            className="send-button"
            disabled={!message.trim() || loading || sending}
            aria-label="发送消息"
            title="发送消息"
          >
            <SendHorizontal size={16} />
          </button>
        </div>
        <small className="composer-agent-name">Agent：{agent.name}</small>
      </form>
    </aside>
  );
}
