import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './app/AppShell'
import { OverviewPage } from './pages/OverviewPage'
import { ChaptersPage, DialoguePage, DurationPage, ProductionRunPage, ProjectCenterPage, ServicesPage, SkillsPage } from './pages/SupplementaryPages'
import { AssetsPage, OutlinePage, ProductionPage, ReferencesPage, ReviewPage, ScriptPage, SettingsPage, SourcePage, StoryboardPage } from './pages/WorkspacePages'

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<ProjectCenterPage />} />
      <Route path="/projects/:projectId" element={<AppShell />}>
        <Route path="overview" element={<OverviewPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="story/source/:sourceEpisodeId?" element={<SourcePage />} />
        <Route path="story/outline" element={<OutlinePage />} />
        <Route path="story/chapters" element={<ChaptersPage />} />
        <Route path="assets/:assetType" element={<AssetsPage />} />
        <Route path="script/duration" element={<DurationPage />} />
        <Route path="script/episodes/:productionEpisodeId" element={<ScriptPage />} />
        <Route path="script/dialogue" element={<DialoguePage />} />
        <Route path="references" element={<ReferencesPage />} />
        <Route path="storyboard" element={<StoryboardPage />} />
        <Route path="storyboard/episodes/:productionEpisodeId?" element={<StoryboardPage />} />
        <Route path="production" element={<ProductionPage />} />
        <Route path="production/episodes/:productionEpisodeId?" element={<ProductionPage />} />
        <Route path="production/runs/:runId" element={<ProductionRunPage />} />
        <Route path="review" element={<ReviewPage />} />
        <Route path="review/episodes/:productionEpisodeId?" element={<ReviewPage />} />
      </Route>
      <Route path="/settings/services" element={<ServicesPage />} />
      <Route path="/settings/skills" element={<SkillsPage />} />
      <Route path="*" element={<Navigate to="/projects/tianqiao/overview" replace />} />
    </Routes>
  )
}
