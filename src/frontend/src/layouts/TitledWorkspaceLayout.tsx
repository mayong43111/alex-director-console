import { useEffect, useRef, useState, type ReactNode, type Ref } from "react";
import { ChevronRight, Save } from "lucide-react";
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
  saveFormId?: string;
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
  saveFormId,
  compact = false,
  children,
}: TitledWorkspaceLayoutProps) {
  const frameRef = useRef<HTMLElement>(null);
  const [saveState, setSaveState] = useState<string | null>(null);
  const currentTab = tabs.find((tab) =>
    pathname === tab.to || pathname.startsWith(`${tab.to}/`));

  useEffect(() => {
    const frame = frameRef.current;
    if (!frame || !saveFormId) return;

    const syncSaveState = () => {
      const form = frame.querySelector<HTMLFormElement>(`#${CSS.escape(saveFormId)}`);
      setSaveState(form?.dataset.workspaceSaveState ?? null);
    };
    const observer = new MutationObserver(syncSaveState);
    observer.observe(frame, {
      attributes: true,
      attributeFilter: ["data-workspace-save-state"],
      childList: true,
      subtree: true,
    });
    syncSaveState();
    return () => observer.disconnect();
  }, [pathname, saveFormId]);

  const saveDisabled = saveState === null
    || saveState === "blocked"
    || saveState === "loading"
    || saveState === "idle"
    || saveState === "saving"
    || saveState === "saved";

  const renderTabs = (className: string) => (
    <nav className={className} aria-label={`${title}导航`}>
      {tabs.map((tab) => {
        const active = pathname === tab.to || pathname.startsWith(`${tab.to}/`);
        return (
          <Link key={tab.to} to={tab.to} className={active ? "active" : ""}>
            {tab.label}
          </Link>
        );
      })}
    </nav>
  );

  return (
    <section className="workspace-frame" ref={frameRef}>
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
          {compact && tabs.length > 0 && renderTabs("workspace-inline-tabs")}
          <div className="workspace-title-actions">
            {status && <div className="workspace-status">{status}</div>}
            {actions}
            {saveFormId && (
              <button
                className="primary-button workspace-save-button"
                type="submit"
                form={saveFormId}
                disabled={saveDisabled}
                aria-busy={saveState === "saving"}
              >
                <Save size={14} />
                <span>{saveState === "saving" ? "正在保存" : "保存"}</span>
              </button>
            )}
          </div>
        </div>
        {!compact && tabs.length > 0 && renderTabs("workspace-tabs")}
      </header>
      <main className="main-canvas" ref={contentRef}>
        {children}
      </main>
    </section>
  );
}