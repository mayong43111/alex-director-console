import { Navigate, Route, Routes, useParams } from 'react-router-dom'
import { AppShell } from './app/AppShell'
import { ApplicationLayout } from './layouts'
import { AgentsPage, DemoPage, ProjectCenterPage, ServicesPage, SkillsPage } from './pages/SupplementaryPages'
import { SessionsPage } from './pages/SessionsPage'
import { VoicePackagesPage } from './pages/VoicePackagesPage'
import {
  AssetsPage,
  ProductionPage,
  ProductionRunPage,
  ReviewPage,
  ScriptLandingPage,
  ScriptPage,
  SettingsPage,
  SourcePage,
  StoryboardPage,
  StoryboardShotPage,
} from './pages/WorkspacePages'

function LegacyAdaptationRedirect() {
  const { projectId = '', sourceEpisodeId } = useParams()
  return sourceEpisodeId
    ? <Navigate to={`/projects/${projectId}/script/${sourceEpisodeId}/adaptation`} replace />
    : <SourcePage />
}

function LegacyProductionScriptRedirect() {
  const { projectId = '', productionEpisodeId = '' } = useParams()
  return <Navigate to={`/projects/${projectId}/script/${productionEpisodeId}/production`} replace />
}

function LegacyStoryRedirect({ view = 'source' }: { view?: 'source' | 'material' }) {
  const { projectId = '', sourceEpisodeId } = useParams()
  const target = sourceEpisodeId
    ? `/projects/${projectId}/story/${sourceEpisodeId}/${view}`
    : `/projects/${projectId}/story`
  return <Navigate to={target} replace />
}

export default function App() {
  return (
    <Routes>
      <Route element={<ApplicationLayout />}>
        <Route path="/" element={<ProjectCenterPage />} />
        <Route path="/settings/services" element={<ServicesPage />} />
        <Route path="/settings/voices" element={<VoicePackagesPage />} />
        <Route path="/settings/agents" element={<AgentsPage />} />
        <Route path="/settings/skills" element={<SkillsPage />} />
        <Route path="/settings/sessions" element={<SessionsPage />} />
        <Route path="/projects/:projectId/settings/services" element={<ServicesPage />} />
        <Route path="/projects/:projectId/settings/voices" element={<VoicePackagesPage />} />
        <Route path="/projects/:projectId/settings/agents" element={<AgentsPage />} />
        <Route path="/projects/:projectId/settings/skills" element={<SkillsPage />} />
        <Route path="/projects/:projectId/settings/sessions" element={<SessionsPage />} />
      </Route>
      <Route path="/projects/:projectId" element={<AppShell />}>
        <Route path="overview" element={<Navigate to="../settings" replace />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="story" element={<SourcePage />} />
        <Route path="story/:sourceId/source" element={<SourcePage />} />
        <Route path="story/:sourceId/material" element={<SourcePage />} />
        <Route path="story/source/:sourceEpisodeId?" element={<LegacyStoryRedirect />} />
        <Route path="story/material/:sourceEpisodeId?" element={<LegacyStoryRedirect view="material" />} />
        <Route path="story/adaptation/:sourceEpisodeId?" element={<LegacyAdaptationRedirect />} />
        <Route path="story/outline" element={<LegacyStoryRedirect />} />
        <Route path="story/chapters" element={<LegacyStoryRedirect />} />
        <Route path="assets/:assetType" element={<AssetsPage />} />
        <Route path="script/:sourceId/adaptation" element={<SourcePage />} />
        <Route path="script/:productionEpisodeId/production" element={<ScriptPage />} />
        <Route path="script/adaptation/:sourceEpisodeId?" element={<LegacyAdaptationRedirect />} />
        <Route path="script/draft/:sourceEpisodeId?" element={<LegacyAdaptationRedirect />} />
        <Route path="script" element={<ScriptLandingPage />} />
        <Route path="script/duration" element={<DemoPage title="时长仪表" />} />
        <Route path="script/episodes/:productionEpisodeId" element={<LegacyProductionScriptRedirect />} />
        <Route path="script/dialogue" element={<DemoPage title="台词本" />} />
        <Route path="references" element={<Navigate to="../assets/characters" replace />} />
        <Route path="storyboard" element={<StoryboardPage />} />
        <Route path="storyboard/episodes/:productionEpisodeId?" element={<StoryboardPage />} />
        <Route path="storyboard/episodes/:productionEpisodeId/shots/:shotResourceId" element={<StoryboardShotPage />} />
        <Route path="production" element={<ProductionPage />} />
        <Route path="production/episodes/:productionEpisodeId?" element={<ProductionPage />} />
        <Route path="production/runs/:runId" element={<ProductionRunPage />} />
        <Route path="review" element={<ReviewPage />} />
        <Route path="review/episodes/:productionEpisodeId?" element={<ReviewPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/projects/tianqiao/overview" replace />} />
    </Routes>
  )
}
