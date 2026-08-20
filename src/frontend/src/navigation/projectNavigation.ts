import {
  AudioLines,
  BookOpenText,
  Boxes,
  Clapperboard,
  Film,
  Gauge,
  LayoutDashboard,
  SlidersHorizontal,
  Sparkles,
  type LucideIcon,
} from "lucide-react";

export interface ProjectNavigationItem {
  key: string;
  label: string;
  icon: LucideIcon;
  to: string | null;
}

export const projectNavigation: ProjectNavigationItem[] = [
  { key: "project-center", label: "项目中心", icon: LayoutDashboard, to: null },
  { key: "settings", label: "项目设定", icon: SlidersHorizontal, to: "settings" },
  { key: "story", label: "故事", icon: BookOpenText, to: "story" },
  { key: "script", label: "剧本", icon: Film, to: "script" },
  { key: "assets", label: "资产", icon: Boxes, to: "assets/characters" },
  { key: "audio", label: "音频素材", icon: AudioLines, to: "assets/audio" },
  { key: "storyboard", label: "分镜", icon: Clapperboard, to: "storyboard" },
  { key: "production", label: "生产", icon: Gauge, to: "production" },
  { key: "review", label: "审阅", icon: Sparkles, to: "review" },
];
