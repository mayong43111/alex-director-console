export interface AgentRecord {
  id: string
  name: string
  systemPrompt: string
  skillIds: string[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface SaveAgentInput {
  name: string
  systemPrompt: string
  skillIds: string[]
}

interface ApiProblem {
  title?: string
  detail?: string
  error?: string
  errors?: Record<string, string[]>
}

export async function listAgents(signal?: AbortSignal): Promise<AgentRecord[]> {
  const response = await fetch('/api/v2/agents', { signal })
  if (!response.ok) throw new Error(await readAgentError(response, 'Agent 列表加载失败。'))
  return response.json() as Promise<AgentRecord[]>
}

export async function createAgent(input: SaveAgentInput): Promise<AgentRecord> {
  const response = await fetch('/api/v2/agents', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw new Error(await readAgentError(response, 'Agent 创建失败。'))
  return response.json() as Promise<AgentRecord>
}

export async function updateAgent(agentId: string, input: SaveAgentInput): Promise<AgentRecord> {
  const response = await fetch(`/api/v2/agents/${encodeURIComponent(agentId)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) throw new Error(await readAgentError(response, 'Agent 更新失败。'))
  return response.json() as Promise<AgentRecord>
}

export async function deleteAgent(agentId: string): Promise<void> {
  const response = await fetch(`/api/v2/agents/${encodeURIComponent(agentId)}`, {
    method: 'DELETE',
  })
  if (!response.ok) throw new Error(await readAgentError(response, 'Agent 删除失败。'))
}

async function readAgentError(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null) as ApiProblem | null
  const validationMessage = problem?.errors
    ? Object.values(problem.errors).flat().join(' ')
    : null
  return validationMessage || problem?.detail || problem?.error || problem?.title || fallback
}

export const builtInAgentIds = {
  projectDescriptionWriter: 'd645b7c0-40e3-4b5c-9208-4f7dd1d34e81',
  artDirectionWriter: 'f623cbe9-0852-4003-83a5-5990ad4470e5',
  characterDesignWriter: '7c7b6d83-89b7-4939-a1dd-80a67326558b',
  colorPaletteWriter: 'b725ec2a-ecc7-428c-b5c3-f048f11b17d0',
  cameraLanguageWriter: 'c86c50ec-8dd5-4889-85af-075c500256e7',
  soundStrategyWriter: 'f430be3f-ee6f-4e03-9475-82af31f699b5',
  imagePromptPrefixWriter: 'b25d162a-691c-4382-a3f2-e430d04a43c8',
} as const

export const builtInAgentLabels = {
  projectDescriptionWriter: '项目介绍助手',
  artDirectionWriter: '项目美术方向助手',
  characterDesignWriter: '角色造型约束助手',
  colorPaletteWriter: '项目色彩策略助手',
  cameraLanguageWriter: '项目摄影语言助手',
  soundStrategyWriter: '项目声音策略助手',
  imagePromptPrefixWriter: '图像生成约束助手',
} as const

export interface AgentInvocationInput {
  input: string
  context?: unknown
  maxLength?: number
}

export interface AgentInvocationResult {
  value: string
  model: string
  runtime: string
}

export async function invokeAgent(
  agentId: string,
  input: AgentInvocationInput,
  signal?: AbortSignal,
): Promise<AgentInvocationResult> {
  const response = await fetch(`/api/v2/agents/${encodeURIComponent(agentId)}/invoke`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...input, context: input.context ?? {} }),
    signal,
  })
  if (!response.ok) throw new Error(await readAgentError(response, 'Agent 调用失败。'))
  return response.json() as Promise<AgentInvocationResult>
}