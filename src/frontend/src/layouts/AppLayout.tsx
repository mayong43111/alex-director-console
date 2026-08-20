import type { ReactNode } from "react";

type AppLayoutProps = {
  headerStart: ReactNode;
  headerActions?: ReactNode;
  agentOpen?: boolean;
  children: ReactNode;
};

export function AppLayout({
  headerStart,
  headerActions,
  agentOpen = false,
  children,
}: AppLayoutProps) {
  return (
    <div className={`director-layout ${agentOpen ? "agent-open" : "agent-closed"}`}>
      <header className="director-header">
        <div className="director-header-brand">{headerStart}</div>
        {headerActions && (
          <div className="director-header-actions">{headerActions}</div>
        )}
      </header>
      {children}
    </div>
  );
}