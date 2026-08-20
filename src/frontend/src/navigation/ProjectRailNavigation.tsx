import { Badge, Tooltip } from "antd";
import { Settings } from "lucide-react";
import { Link } from "react-router-dom";
import { projectNavigation } from "./projectNavigation";

type ProjectRailNavigationProps = {
  activeKey: string;
  projectBase?: string;
  productionEpisodeId?: string;
};

export function ProjectRailNavigation({
  activeKey,
  projectBase,
  productionEpisodeId,
}: ProjectRailNavigationProps) {
  return (
    <>
      <nav className="director-rail-nav">
        {projectNavigation.map(({ key, label, icon: Icon, to }) => {
          const resolvedPath = to?.replace(
            "production-e01",
            productionEpisodeId ?? "production-e01",
          );
          const target = to === null
            ? "/"
            : projectBase && resolvedPath
              ? `${projectBase}/${resolvedPath}`
              : null;
          const isActive = activeKey === key;

          return (
            <Tooltip
              title={target ? label : `${label}（请先选择项目）`}
              placement="right"
              key={key}
            >
              {target ? (
                <Link
                  className={`director-rail-link ${isActive ? "active" : ""}`}
                  to={target}
                  aria-label={label}
                  aria-current={isActive ? "page" : undefined}
                >
                  <Icon size={19} strokeWidth={1.8} />
                  <span>{label}</span>
                </Link>
              ) : (
                <div
                  className="director-rail-link disabled"
                  aria-label={`${label}，请先选择项目`}
                  aria-disabled="true"
                >
                  <Icon size={19} strokeWidth={1.8} />
                  <span>{label}</span>
                </div>
              )}
            </Tooltip>
          );
        })}
      </nav>
      <div className="director-rail-footer">
        <Tooltip title="设置" placement="right">
          <Link
            className={`director-rail-link ${activeKey === "global-settings" ? "active" : ""}`}
            to={projectBase ? `${projectBase}/settings/services` : "/settings/services"}
            aria-label="设置"
            aria-current={activeKey === "global-settings" ? "page" : undefined}
          >
            <Settings size={19} strokeWidth={1.8} />
            <span>设置</span>
          </Link>
        </Tooltip>
        <Tooltip title="服务正常 · 4 / 4" placement="right">
          <div className="director-rail-status" aria-label="服务正常，4 / 4">
            <Badge status="success" />
          </div>
        </Tooltip>
      </div>
    </>
  );
}