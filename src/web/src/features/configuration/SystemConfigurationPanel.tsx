import { LoaderCircle, Server } from 'lucide-react'
import type { Dispatch, SetStateAction } from 'react'
import type {
  AgentStatus,
  GlobalFoundryConfiguration,
  ProjectRuntimeConfiguration,
  SkillDefinitionRecord,
} from '../../models'

type SaveState = 'idle' | 'loading' | 'saving' | 'saved' | 'error'

interface SystemConfigurationPanelProps {
  agentStatus: AgentStatus | null
  foundryConfiguration: GlobalFoundryConfiguration
  setFoundryConfiguration: Dispatch<SetStateAction<GlobalFoundryConfiguration>>
  foundryConfigurationState: SaveState
  openAiApiKey: string
  setOpenAiApiKey: Dispatch<SetStateAction<string>>
  imageApiKey: string
  setImageApiKey: Dispatch<SetStateAction<string>>
  speechApiKey: string
  setSpeechApiKey: Dispatch<SetStateAction<string>>
  saveFoundryConfiguration: () => Promise<void>
  runtimeConfiguration: ProjectRuntimeConfiguration
  setRuntimeConfiguration: Dispatch<SetStateAction<ProjectRuntimeConfiguration>>
  runtimeConfigurationState: SaveState
  saveRuntimeConfiguration: () => Promise<void>
  skills: SkillDefinitionRecord[]
  skillsLoading: boolean
  skillError: string | null
  selectedSkill: SkillDefinitionRecord | null
  onSelectSkill: (skill: SkillDefinitionRecord) => void
  updateSkill: (skill: SkillDefinitionRecord, isEnabled: boolean) => Promise<void>
}

export function SystemConfigurationPanel({
  agentStatus,
  foundryConfiguration,
  setFoundryConfiguration,
  foundryConfigurationState,
  openAiApiKey,
  setOpenAiApiKey,
  imageApiKey,
  setImageApiKey,
  speechApiKey,
  setSpeechApiKey,
  saveFoundryConfiguration,
  runtimeConfiguration,
  setRuntimeConfiguration,
  runtimeConfigurationState,
  saveRuntimeConfiguration,
  skills,
  skillsLoading,
  skillError,
  selectedSkill,
  onSelectSkill,
  updateSkill,
}: SystemConfigurationPanelProps) {
  return (
    <section className="assets-panel project-settings-panel system-configuration-panel" aria-labelledby="system-configuration-title">
      <header className="assets-header">
        <div>
          <p className="section-label">GLOBAL</p>
          <h2 id="system-configuration-title">系统配置</h2>
        </div>
      </header>
      <div className="project-settings-form">
        <section className="project-settings-group">
          <h3>Azure AI Foundry</h3>
          <label htmlFor="system-openai-endpoint">语言模型 Endpoint</label>
          <input id="system-openai-endpoint" value={foundryConfiguration.openAiEndpoint} placeholder="https://资源名.openai.azure.com" onChange={(event) => setFoundryConfiguration((current) => ({ ...current, openAiEndpoint: event.target.value }))} />
          <label htmlFor="system-openai-deployment">语言模型部署</label>
          <input id="system-openai-deployment" value={foundryConfiguration.openAiDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, openAiDeployment: event.target.value }))} />
          <label htmlFor="system-openai-key">语言模型 API Key</label>
          <input id="system-openai-key" type="password" value={openAiApiKey} autoComplete="new-password" placeholder={foundryConfiguration.openAiApiKeyConfigured ? '已配置，留空则保持不变' : '输入 API Key'} onChange={(event) => setOpenAiApiKey(event.target.value)} />

          <label htmlFor="system-image-endpoint">图片模型 Endpoint</label>
          <input id="system-image-endpoint" value={foundryConfiguration.imageEndpoint} placeholder="留空则复用语言模型 Endpoint" onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageEndpoint: event.target.value }))} />
          <label htmlFor="system-image-deployment">图片模型部署</label>
          <input id="system-image-deployment" value={foundryConfiguration.imageDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageDeployment: event.target.value }))} />
          <label htmlFor="system-image-key">图片模型 API Key</label>
          <input id="system-image-key" type="password" value={imageApiKey} autoComplete="new-password" placeholder={foundryConfiguration.imageApiKeyConfigured ? '已配置，留空则保持不变' : '留空则复用语言模型 API Key'} onChange={(event) => setImageApiKey(event.target.value)} />
          <label htmlFor="system-image-api-version">图片 API 版本</label>
          <input id="system-image-api-version" value={foundryConfiguration.imageApiVersion} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageApiVersion: event.target.value }))} />
          <label htmlFor="system-image-quality">默认图片质量</label>
          <select id="system-image-quality" value={foundryConfiguration.imageQuality} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageQuality: event.target.value as GlobalFoundryConfiguration['imageQuality'] }))}>
            <option value="low">低</option>
            <option value="medium">中</option>
            <option value="high">高</option>
          </select>

          <label htmlFor="system-speech-endpoint">语音模型 Endpoint</label>
          <input id="system-speech-endpoint" value={foundryConfiguration.speechEndpoint} placeholder="留空则复用语言模型 Endpoint" onChange={(event) => setFoundryConfiguration((current) => ({ ...current, speechEndpoint: event.target.value }))} />
          <label htmlFor="system-speech-deployment">语音模型部署</label>
          <input id="system-speech-deployment" value={foundryConfiguration.speechDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, speechDeployment: event.target.value }))} />
          <label htmlFor="system-speech-key">语音模型 API Key</label>
          <input id="system-speech-key" type="password" value={speechApiKey} autoComplete="new-password" placeholder={foundryConfiguration.speechApiKeyConfigured ? '已配置，留空则保持不变' : '留空则复用语言模型 API Key'} onChange={(event) => setSpeechApiKey(event.target.value)} />
          <label htmlFor="system-speech-api-version">语音 API 版本</label>
          <input id="system-speech-api-version" value={foundryConfiguration.speechApiVersion} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, speechApiVersion: event.target.value }))} />
          <button className="project-settings-save" type="button" disabled={foundryConfigurationState === 'loading' || foundryConfigurationState === 'saving'} onClick={() => void saveFoundryConfiguration()}>
            {foundryConfigurationState === 'saving' ? '保存中…' : '保存 Foundry 配置'}
          </button>
          <div className="project-setting-readout">
            <span>连接状态</span>
            <strong>{foundryConfigurationState === 'error' ? '保存失败' : agentStatus?.configured ? '语言模型已配置' : '语言模型待配置'}</strong>
          </div>
        </section>

        <section className="project-settings-group">
          <h3><Server size={14} aria-hidden="true" /> VM 与 ComfyUI</h3>
          <label htmlFor="system-vm-host">VM 主机或 IP</label>
          <input id="system-vm-host" value={runtimeConfiguration.vmHost} placeholder="20.0.0.1" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmHost: event.target.value }))} />
          <div className="project-resolution-inputs">
            <input type="number" min="1" max="65535" value={runtimeConfiguration.vmPort} aria-label="SSH 端口" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmPort: Number(event.target.value) }))} />
            <span>用户</span>
            <input value={runtimeConfiguration.vmUsername} aria-label="SSH 用户" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmUsername: event.target.value }))} />
          </div>
          <label htmlFor="system-ssh-key">本机 SSH 私钥路径</label>
          <input id="system-ssh-key" value={runtimeConfiguration.sshPrivateKeyPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, sshPrivateKeyPath: event.target.value }))} />
          <label htmlFor="system-comfy-path">远端 ComfyUI 目录</label>
          <input id="system-comfy-path" value={runtimeConfiguration.comfyUiPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPath: event.target.value }))} />
          <label htmlFor="system-python-path">远端 Python</label>
          <input id="system-python-path" value={runtimeConfiguration.comfyUiPythonPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPythonPath: event.target.value }))} />
          <div className="project-resolution-inputs">
            <input type="number" min="1" max="65535" value={runtimeConfiguration.comfyUiPort} aria-label="远端 ComfyUI 端口" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPort: Number(event.target.value) }))} />
            <span>代理到</span>
            <input type="number" min="1" max="65535" value={runtimeConfiguration.localProxyPort} aria-label="本地代理端口" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, localProxyPort: Number(event.target.value) }))} />
          </div>
          <label htmlFor="system-workflow-path">远端 Workflow 目录</label>
          <input id="system-workflow-path" value={runtimeConfiguration.workflowDirectory} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, workflowDirectory: event.target.value }))} />
          <label htmlFor="system-output-path">远端输出目录</label>
          <input id="system-output-path" value={runtimeConfiguration.outputDirectory} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, outputDirectory: event.target.value }))} />
          <button className="project-settings-save" type="button" disabled={runtimeConfigurationState === 'loading' || runtimeConfigurationState === 'saving'} onClick={() => void saveRuntimeConfiguration()}>
            {runtimeConfigurationState === 'saving' ? '保存中…' : '保存 VM 配置'}
          </button>
          <div className="project-setting-readout">
            <span>配置状态</span>
            <strong>{runtimeConfigurationState === 'error' ? '保存失败' : runtimeConfiguration.vmHost ? runtimeConfigurationState === 'saved' ? '已保存' : '已配置' : '未配置'}</strong>
          </div>
        </section>

        <section className="project-settings-group system-skills-group">
          <div className="system-section-heading">
            <h3>Agent 技能</h3>
            <span className="asset-count">{skills.filter((skill) => skill.isEnabled).length}</span>
          </div>
          {skillError && <p className="asset-error">{skillError}</p>}
          {skillsLoading ? (
            <div className="asset-empty">
              <LoaderCircle className="spin" size={22} aria-hidden="true" />
              <p>正在加载技能…</p>
            </div>
          ) : (
            <div className="skill-list">
              {skills.map((skill) => (
                <article className={`skill-card ${selectedSkill?.id === skill.id ? 'active' : ''}`} key={skill.id}>
                  <header>
                    <button className="skill-card-select" type="button" aria-pressed={selectedSkill?.id === skill.id} onClick={() => onSelectSkill(skill)}>
                      <strong>{skill.title}</strong>
                      <small>{skill.name} · v{skill.version}</small>
                      <p>{skill.description}</p>
                    </button>
                    <label className="skill-switch">
                      <input type="checkbox" checked={skill.isEnabled} aria-label={`${skill.isEnabled ? '停用' : '启用'}${skill.name}`} onChange={(event) => void updateSkill(skill, event.target.checked)} />
                      <span aria-hidden="true" />
                    </label>
                  </header>
                </article>
              ))}
            </div>
          )}
        </section>
      </div>
    </section>
  )
}
