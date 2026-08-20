import type { ReactNode } from "react";
import { Link } from "react-router-dom";

export type SettingsMenuItem = {
  label: string;
  to: string;
  icon: ReactNode;
};

type SettingsLayoutProps = {
  pathname: string;
  items: SettingsMenuItem[];
  children: ReactNode;
};

export function SettingsLayout({ pathname, items, children }: SettingsLayoutProps) {
  return (
    <div className="settings-workspace-layout">
      <aside className="settings-workspace-navigation">
        <span className="settings-workspace-label">配置项</span>
        <nav aria-label="设置配置项">
          {items.map((item) => {
            const active = pathname === item.to || pathname.startsWith(`${item.to}/`);
            return (
              <Link
                key={item.to}
                to={item.to}
                className={active ? "active" : ""}
                aria-current={active ? "page" : undefined}
              >
                {item.icon}
                <span>{item.label}</span>
              </Link>
            );
          })}
        </nav>
      </aside>
      <div className="settings-workspace-content">{children}</div>
    </div>
  );
}