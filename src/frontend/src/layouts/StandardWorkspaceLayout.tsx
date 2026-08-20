import type { ReactNode } from "react";

type StandardWorkspaceLayoutProps = {
  navigation: ReactNode;
  navigationLabel?: string;
  agent?: ReactNode;
  agentOverlay?: boolean;
  onCloseAgent?: () => void;
  children: ReactNode;
};

export function StandardWorkspaceLayout({
  navigation,
  navigationLabel = "项目功能导航",
  agent,
  agentOverlay = false,
  onCloseAgent,
  children,
}: StandardWorkspaceLayoutProps) {
  return (
    <div className={`director-body ${agentOverlay ? "agent-overlay" : ""}`}>
      <aside className="director-rail" aria-label={navigationLabel}>
        {navigation}
      </aside>
      {children}
      {agent && agentOverlay && onCloseAgent && (
        <button
          className="agent-backdrop"
          onClick={onCloseAgent}
          aria-label="关闭副导演"
        />
      )}
      {agent}
    </div>
  );
}