import { Navigate, Route, Routes, useParams } from 'react-router-dom'
import { AppShell } from './app/AppShell'
import { DemoPage, ProjectCenterPage, ServicesPage, SkillsPage } from './pages/SupplementaryPages'
import { ScriptLandingPage, ScriptPage, SettingsPage, SourcePage } from './pages/WorkspacePages'

function LegacyDraftRedirect() {
  const { projectId = '', sourceEpisodeId } = useParams()
  return <Navigate to={`/projects/${projectId}/story/adaptation${sourceEpisodeId ? `/${sourceEpisodeId}` : ''}`} replace />
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<ProjectCenterPage />} />
      <Route path="/projects/:projectId" element={<AppShell />}>
        <Route path="overview" element={<Navigate to="../settings" replace />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="story/source/:sourceEpisodeId?" element={<SourcePage />} />
        <Route path="story/material/:sourceEpisodeId?" element={<SourcePage />} />
        <Route path="story/adaptation/:sourceEpisodeId?" element={<SourcePage />} />
        <Route path="story/outline" element={<Navigate to="../source" replace />} />
        <Route path="story/chapters" element={<Navigate to="../source" replace />} />
        <Route path="assets/:assetType" element={<DemoPage title="资产圣经" />} />
        <Route path="script/draft/:sourceEpisodeId?" element={<LegacyDraftRedirect />} />
        <Route path="script" element={<ScriptLandingPage />} />
        <Route path="script/duration" element={<DemoPage title="时长仪表" />} />
        <Route path="script/episodes/:productionEpisodeId" element={<ScriptPage />} />
        <Route path="script/dialogue" element={<DemoPage title="台词本" />} />
        <Route path="references" element={<DemoPage title="视觉参考" />} />
        <Route path="storyboard" element={<DemoPage title="分镜" />} />
        <Route path="storyboard/episodes/:productionEpisodeId?" element={<DemoPage title="分镜" />} />
        <Route path="production" element={<DemoPage title="生产" />} />
        <Route path="production/episodes/:productionEpisodeId?" element={<DemoPage title="生产" />} />
        <Route path="production/runs/:runId" element={<DemoPage title="生产运行" />} />
        <Route path="review" element={<DemoPage title="审阅交付" />} />
        <Route path="review/episodes/:productionEpisodeId?" element={<DemoPage title="审阅交付" />} />
      </Route>
      <Route path="/settings/services" element={<ServicesPage />} />
      <Route path="/settings/skills" element={<SkillsPage />} />
      <Route path="*" element={<Navigate to="/projects/tianqiao/overview" replace />} />
    </Routes>
  )
}
