import {
  useEffect,
  useRef,
  useState,
  type ComponentProps,
} from "react";
import { Button, Input } from "antd";
import { Check, RotateCcw, WandSparkles } from "lucide-react";
import { invokeAgent } from "../api/agents";

type TextAreaProps = ComponentProps<typeof Input.TextArea>;

export type AgentTextAreaStatus = "idle" | "loading" | "review";

export type AgentTextAreaProps = Omit<TextAreaProps, "value" | "onChange"> & {
  agentId: string;
  value: string;
  onChange: (value: string) => void;
  context?: unknown;
  invokeDisabled?: boolean;
  onStatusChange?: (status: AgentTextAreaStatus) => void;
};

export function AgentTextArea({
  agentId,
  value,
  onChange,
  context,
  invokeDisabled = false,
  onStatusChange,
  disabled,
  maxLength,
  ...textAreaProps
}: AgentTextAreaProps) {
  const [candidate, setCandidate] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sourceValue = useRef(value);
  const status: AgentTextAreaStatus = loading
    ? "loading"
    : candidate === null
      ? "idle"
      : "review";

  useEffect(() => {
    onStatusChange?.(status);
  }, [onStatusChange, status]);

  useEffect(() => {
    if (candidate !== null && value !== sourceValue.current) {
      setCandidate(null);
    }
  }, [candidate, value]);

  async function generateCandidate() {
    if (loading || disabled || invokeDisabled) return;

    setLoading(true);
    setError(null);
    sourceValue.current = value;
    try {
      const result = await invokeAgent(agentId, {
        input: value,
        context,
        maxLength: typeof maxLength === "number" ? maxLength : undefined,
      });
      setCandidate(result.value);
    } catch (invokeError) {
      setError(invokeError instanceof Error ? invokeError.message : "Agent 调用失败，请稍后重试。");
    } finally {
      setLoading(false);
    }
  }

  function acceptCandidate() {
    if (candidate === null) return;
    const acceptedValue = candidate;
    setCandidate(null);
    setError(null);
    onChange(acceptedValue);
  }

  function discardCandidate() {
    setCandidate(null);
    setError(null);
  }

  return (
    <div className={`agent-textarea-control${candidate === null ? "" : " is-reviewing"}${textAreaProps.showCount ? " has-count" : ""}`}>
      <div className="agent-textarea-input">
        <Input.TextArea
          {...textAreaProps}
          value={candidate ?? value}
          maxLength={maxLength}
          disabled={disabled || loading}
          onChange={(event) => {
            if (candidate === null) onChange(event.target.value);
            else setCandidate(event.target.value);
          }}
        />
        {candidate === null && (
          <button
            className="ai-field-icon"
            type="button"
            onClick={generateCandidate}
            disabled={disabled || invokeDisabled || loading}
            title="调用 Agent"
            aria-label="调用 Agent"
          >
            {loading ? <span className="spinner" /> : <WandSparkles size={15} />}
          </button>
        )}
      </div>
      {candidate !== null && (
        <div className="agent-textarea-review" role="status">
          <span>Agent 已生成候选内容</span>
          <Button size="small" icon={<RotateCcw size={14} />} onClick={discardCandidate}>
            撤销
          </Button>
          <Button type="primary" size="small" icon={<Check size={14} />} onClick={acceptCandidate}>
            接收
          </Button>
        </div>
      )}
      {error && <div className="agent-textarea-error" role="alert">{error}</div>}
    </div>
  );
}