import type { ReactNode, Ref } from "react";
import { ChevronRight } from "lucide-react";
import { Link } from "react-router-dom";

export type WorkspaceTab = {
  label: string;
  to: string;
};

type TitledWorkspaceLayoutProps = {
  title: string;
  description?: string;
  projectName: string;
  projectHome: string;
  pathname: string;
  tabs?: WorkspaceTab[];
  contentRef?: Ref<HTMLElement>;
  status?: ReactNode;
  actions?: ReactNode;
  compact?: boolean;
  children: ReactNode;
};

export function TitledWorkspaceLayout({
  title,
  description,
  projectName,
  projectHome,
  pathname,
  tabs = [],
  contentRef,
  status,
  actions,
  compact = false,
  children,
}: TitledWorkspaceLayoutProps) {
  const currentTab = tabs.find((tab) =>
    pathname === tab.to || pathname.startsWith(`${tab.to}/`));

  return (
    <section className="workspace-frame">
      <header className={`workspace-header ${compact ? "compact" : ""}`}>
        <div className="workspace-title-row">
          <div className="workspace-title-copy">
            <nav className="workflow-breadcrumb" aria-label="面包屑导航">
              <Link to={projectHome}>{projectName}</Link>
              <ChevronRight size={13} />
              <span aria-current="page">{currentTab?.label ?? title}</span>
            </nav>
            {!compact && <h1>{title}</h1>}
            {!compact && description && <p>{description}</p>}
          </div>
          {(status || actions) && (
            <div className="workspace-title-actions">
              {status && <div className="workspace-status">{status}</div>}
              {actions}
            </div>
          )}
        </div>
        {tabs.length > 0 && (
          <nav className="workspace-tabs" aria-label={`${title}导航`}>
            {tabs.map((tab) => {
              const active = pathname === tab.to || pathname.startsWith(`${tab.to}/`);
              return (
                <Link key={tab.to} to={tab.to} className={active ? "active" : ""}>
                  {tab.label}
                </Link>
              );
            })}
          </nav>
        )}
      </header>
      <main className="main-canvas" ref={contentRef}>
        {children}
      </main>
    </section>
  );
}