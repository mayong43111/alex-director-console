import { useEffect, useState } from "react";
import { ProTable, type ProColumns } from "@ant-design/pro-components";
import { Button, Drawer, Space, Tag, Tooltip } from "antd";
import { MessageSquareText, RefreshCw } from "lucide-react";
import {
  getSession,
  listSessions,
  type SessionRecord,
  type SessionSummary,
} from "../api/sessions";

const dateFormatter = new Intl.DateTimeFormat("zh-CN", {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

export function SessionsPage() {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [detail, setDetail] = useState<SessionRecord | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);

  async function loadSessions(signal?: AbortSignal) {
    setLoading(true);
    setError(null);
    try {
      setSessions(await listSessions(signal));
    } catch (loadError) {
      if (loadError instanceof DOMException && loadError.name === "AbortError") return;
      setError(loadError instanceof Error ? loadError.message : "Session 列表加载失败。");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }

  useEffect(() => {
    const controller = new AbortController();
    listSessions(controller.signal)
      .then(setSessions)
      .catch((loadError: unknown) => {
        if (loadError instanceof DOMException && loadError.name === "AbortError") return;
        setError(loadError instanceof Error ? loadError.message : "Session 列表加载失败。");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  async function openDetail(sessionId: string) {
    setDetailOpen(true);
    setDetail(null);
    setDetailError(null);
    setDetailLoading(true);
    try {
      setDetail(await getSession(sessionId));
    } catch (loadError) {
      setDetailError(loadError instanceof Error ? loadError.message : "Session 详情加载失败。");
    } finally {
      setDetailLoading(false);
    }
  }

  const columns: ProColumns<SessionSummary>[] = [
    {
      title: "Session",
      dataIndex: "title",
      width: 190,
      ellipsis: true,
      render: (_, session) => <strong>{session.title}</strong>,
    },
    {
      title: "Agent",
      dataIndex: "agentName",
      width: 110,
      render: (_, session) => <Tag color="blue">{session.agentName}</Tag>,
    },
    {
      title: "Scope",
      dataIndex: "scopeKey",
      width: 280,
      ellipsis: true,
      copyable: true,
    },
    {
      title: "项目",
      dataIndex: "projectName",
      width: 160,
      ellipsis: true,
      render: (_, session) => session.projectName ?? "全局",
    },
    {
      title: "消息",
      dataIndex: "messageCount",
      width: 72,
      align: "right",
    },
    {
      title: "更新时间",
      dataIndex: "updatedAtUtc",
      width: 168,
      render: (_, session) => dateFormatter.format(new Date(session.updatedAtUtc)),
    },
    {
      title: "操作",
      valueType: "option",
      width: 64,
      render: (_, session) => (
        <Tooltip title="查看消息">
          <Button
            type="text"
            size="small"
            icon={<MessageSquareText size={15} />}
            aria-label={`查看 ${session.title} 的消息`}
            onClick={() => void openDetail(session.id)}
          />
        </Tooltip>
      ),
    },
  ];

  return (
    <div className="global-settings">
      <div className="settings-page-toolbar">
        <Button
          icon={<RefreshCw size={14} />}
          loading={loading}
          onClick={() => void loadSessions()}
        >
          刷新
        </Button>
      </div>
      {error && <p className="settings-feedback error">{error}</p>}
      <ProTable<SessionSummary>
        className="session-table"
        rowKey="id"
        columns={columns}
        dataSource={sessions}
        loading={loading}
        size="small"
        search={false}
        options={false}
        toolBarRender={false}
        pagination={{
          pageSize: 20,
          showSizeChanger: false,
          showTotal: () => null,
          size: "small",
        }}
        locale={{ emptyText: error || "还没有 Session。" }}
      />
      <Drawer
        title={detail?.title ?? "Session 消息"}
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
        width={640}
        loading={detailLoading}
        destroyOnHidden
      >
        {detailError && <p className="settings-feedback error">{detailError}</p>}
        {detail && (
          <div className="session-detail">
            <div className="session-detail-meta">
              <span><strong>Agent</strong>{detail.agentName}</span>
              <span><strong>Scope</strong>{detail.scopeKey}</span>
              <span><strong>项目</strong>{detail.projectName ?? "全局"}</span>
              <span><strong>运行时</strong>{detail.runtime}</span>
            </div>
            <div className="session-detail-messages">
              {detail.messages.length === 0 ? (
                <p className="session-detail-empty">没有消息。</p>
              ) : detail.messages.map((message) => (
                <article className={`session-detail-message ${message.role}`} key={message.id}>
                  <header>
                    <strong>{message.role === "user" ? "导演" : "副导演"}</strong>
                    <Space size={8}>
                      {message.model && <Tag>{message.model}</Tag>}
                      <time>{dateFormatter.format(new Date(message.createdAtUtc))}</time>
                    </Space>
                  </header>
                  <p>{message.content}</p>
                </article>
              ))}
            </div>
          </div>
        )}
      </Drawer>
    </div>
  );
}
