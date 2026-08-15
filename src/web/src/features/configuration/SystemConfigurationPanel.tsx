import { LoaderCircle, Server } from 'lucide-react'
import type { Dispatch, SetStateAction } from 'react'
import { localize, type Language } from '../../i18n'
import type {
  AgentStatus,
  GlobalFoundryConfiguration,
  ProjectRuntimeConfiguration,
  SkillDefinitionRecord,
} from '../../models'

type SaveState = 'idle' | 'loading' | 'saving' | 'saved' | 'error'

interface SystemConfigurationPanelProps {
  language: Language
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
  language,
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
  const text = (chinese: string, english: string) => localize(language, chinese, english)
  return (
    <section className="assets-panel project-settings-panel system-configuration-panel" aria-labelledby="system-configuration-title">
      <header className="assets-header">
        <div>
          <p className="section-label">GLOBAL</p>
          <h2 id="system-configuration-title">{text('系统配置', 'System Configuration')}</h2>
        </div>
      </header>
      <div className="project-settings-form">
        <section className="project-settings-group">
          <h3>Azure AI Foundry</h3>
          <label htmlFor="system-openai-endpoint">{text('语言模型 Endpoint', 'Language model endpoint')}</label>
          <input id="system-openai-endpoint" value={foundryConfiguration.openAiEndpoint} placeholder={text('https://资源名.openai.azure.com', 'https://resource.openai.azure.com')} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, openAiEndpoint: event.target.value }))} />
          <label htmlFor="system-openai-deployment">{text('语言模型部署', 'Language model deployment')}</label>
          <input id="system-openai-deployment" value={foundryConfiguration.openAiDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, openAiDeployment: event.target.value }))} />
          <label htmlFor="system-openai-key">{text('语言模型 API Key', 'Language model API key')}</label>
          <input id="system-openai-key" type="password" value={openAiApiKey} autoComplete="new-password" placeholder={foundryConfiguration.openAiApiKeyConfigured ? text('已配置，留空则保持不变', 'Configured; leave blank to keep it') : text('输入 API Key', 'Enter API key')} onChange={(event) => setOpenAiApiKey(event.target.value)} />

          <label htmlFor="system-image-endpoint">{text('图片模型 Endpoint', 'Image model endpoint')}</label>
          <input id="system-image-endpoint" value={foundryConfiguration.imageEndpoint} placeholder={text('留空则复用语言模型 Endpoint', 'Leave blank to reuse the language model endpoint')} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageEndpoint: event.target.value }))} />
          <label htmlFor="system-image-deployment">{text('图片模型部署', 'Image model deployment')}</label>
          <input id="system-image-deployment" value={foundryConfiguration.imageDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageDeployment: event.target.value }))} />
          <label htmlFor="system-image-key">{text('图片模型 API Key', 'Image model API key')}</label>
          <input id="system-image-key" type="password" value={imageApiKey} autoComplete="new-password" placeholder={foundryConfiguration.imageApiKeyConfigured ? text('已配置，留空则保持不变', 'Configured; leave blank to keep it') : text('留空则复用语言模型 API Key', 'Leave blank to reuse the language model API key')} onChange={(event) => setImageApiKey(event.target.value)} />
          <label htmlFor="system-image-api-version">{text('图片 API 版本', 'Image API version')}</label>
          <input id="system-image-api-version" value={foundryConfiguration.imageApiVersion} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageApiVersion: event.target.value }))} />
          <label htmlFor="system-image-quality">{text('默认图片质量', 'Default image quality')}</label>
          <select id="system-image-quality" value={foundryConfiguration.imageQuality} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, imageQuality: event.target.value as GlobalFoundryConfiguration['imageQuality'] }))}>
            <option value="low">{text('低', 'Low')}</option>
            <option value="medium">{text('中', 'Medium')}</option>
            <option value="high">{text('高', 'High')}</option>
          </select>

          <label htmlFor="system-speech-endpoint">{text('语音模型 Endpoint', 'Speech model endpoint')}</label>
          <input id="system-speech-endpoint" value={foundryConfiguration.speechEndpoint} placeholder={text('留空则复用语言模型 Endpoint', 'Leave blank to reuse the language model endpoint')} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, speechEndpoint: event.target.value }))} />
          <label htmlFor="system-speech-deployment">{text('语音模型部署', 'Speech model deployment')}</label>
          <input id="system-speech-deployment" value={foundryConfiguration.speechDeployment} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, speechDeployment: event.target.value }))} />
          <label htmlFor="system-speech-key">{text('语音模型 API Key', 'Speech model API key')}</label>
          <input id="system-speech-key" type="password" value={speechApiKey} autoComplete="new-password" placeholder={foundryConfiguration.speechApiKeyConfigured ? text('已配置，留空则保持不变', 'Configured; leave blank to keep it') : text('留空则复用语言模型 API Key', 'Leave blank to reuse the language model API key')} onChange={(event) => setSpeechApiKey(event.target.value)} />
          <label htmlFor="system-speech-api-version">{text('语音 API 版本', 'Speech API version')}</label>
          <input id="system-speech-api-version" value={foundryConfiguration.speechApiVersion} onChange={(event) => setFoundryConfiguration((current) => ({ ...current, speechApiVersion: event.target.value }))} />
          <button className="project-settings-save" type="button" disabled={foundryConfigurationState === 'loading' || foundryConfigurationState === 'saving'} onClick={() => void saveFoundryConfiguration()}>
            {foundryConfigurationState === 'saving' ? text('保存中…', 'Saving…') : text('保存 Foundry 配置', 'Save Foundry configuration')}
          </button>
          <div className="project-setting-readout">
            <span>{text('连接状态', 'Connection')}</span>
            <strong>{foundryConfigurationState === 'error' ? text('保存失败', 'Save failed') : agentStatus?.configured ? text('语言模型已配置', 'Language model configured') : text('语言模型待配置', 'Language model pending')}</strong>
          </div>
        </section>

        <section className="project-settings-group">
          <h3><Server size={14} aria-hidden="true" /> {text('VM 与 ComfyUI', 'VM & ComfyUI')}</h3>
          <label htmlFor="system-vm-host">{text('VM 主机或 IP', 'VM host or IP')}</label>
          <input id="system-vm-host" value={runtimeConfiguration.vmHost} placeholder="20.0.0.1" onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmHost: event.target.value }))} />
          <div className="project-resolution-inputs">
            <input type="number" min="1" max="65535" value={runtimeConfiguration.vmPort} aria-label={text('SSH 端口', 'SSH port')} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmPort: Number(event.target.value) }))} />
            <span>{text('用户', 'User')}</span>
            <input value={runtimeConfiguration.vmUsername} aria-label={text('SSH 用户', 'SSH user')} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, vmUsername: event.target.value }))} />
          </div>
          <label htmlFor="system-ssh-key">{text('本机 SSH 私钥路径', 'Local SSH private key path')}</label>
          <input id="system-ssh-key" value={runtimeConfiguration.sshPrivateKeyPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, sshPrivateKeyPath: event.target.value }))} />
          <label htmlFor="system-comfy-path">{text('远端 ComfyUI 目录', 'Remote ComfyUI directory')}</label>
          <input id="system-comfy-path" value={runtimeConfiguration.comfyUiPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPath: event.target.value }))} />
          <label htmlFor="system-python-path">{text('远端 Python', 'Remote Python')}</label>
          <input id="system-python-path" value={runtimeConfiguration.comfyUiPythonPath} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPythonPath: event.target.value }))} />
          <div className="project-resolution-inputs">
            <input type="number" min="1" max="65535" value={runtimeConfiguration.comfyUiPort} aria-label={text('远端 ComfyUI 端口', 'Remote ComfyUI port')} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, comfyUiPort: Number(event.target.value) }))} />
            <span>{text('代理到', 'Proxy to')}</span>
            <input type="number" min="1" max="65535" value={runtimeConfiguration.localProxyPort} aria-label={text('本地代理端口', 'Local proxy port')} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, localProxyPort: Number(event.target.value) }))} />
          </div>
          <label htmlFor="system-workflow-path">{text('远端 Workflow 目录', 'Remote workflow directory')}</label>
          <input id="system-workflow-path" value={runtimeConfiguration.workflowDirectory} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, workflowDirectory: event.target.value }))} />
          <label htmlFor="system-output-path">{text('远端输出目录', 'Remote output directory')}</label>
          <input id="system-output-path" value={runtimeConfiguration.outputDirectory} onChange={(event) => setRuntimeConfiguration((current) => ({ ...current, outputDirectory: event.target.value }))} />
          <button className="project-settings-save" type="button" disabled={runtimeConfigurationState === 'loading' || runtimeConfigurationState === 'saving'} onClick={() => void saveRuntimeConfiguration()}>
            {runtimeConfigurationState === 'saving' ? text('保存中…', 'Saving…') : text('保存 VM 配置', 'Save VM configuration')}
          </button>
          <div className="project-setting-readout">
            <span>{text('配置状态', 'Configuration status')}</span>
            <strong>{runtimeConfigurationState === 'error' ? text('保存失败', 'Save failed') : runtimeConfiguration.vmHost ? runtimeConfigurationState === 'saved' ? text('已保存', 'Saved') : text('已配置', 'Configured') : text('未配置', 'Not configured')}</strong>
          </div>
        </section>

        <section className="project-settings-group system-skills-group">
          <div className="system-section-heading">
            <h3>{text('Agent 技能', 'Agent Skills')}</h3>
            <span className="asset-count">{skills.filter((skill) => skill.isEnabled).length}</span>
          </div>
          {skillError && <p className="asset-error">{skillError}</p>}
          {skillsLoading ? (
            <div className="asset-empty">
              <LoaderCircle className="spin" size={22} aria-hidden="true" />
              <p>{text('正在加载技能…', 'Loading skills…')}</p>
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
                      <input type="checkbox" checked={skill.isEnabled} aria-label={`${skill.isEnabled ? text('停用', 'Disable ') : text('启用', 'Enable ')}${skill.name}`} onChange={(event) => void updateSkill(skill, event.target.checked)} />
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
