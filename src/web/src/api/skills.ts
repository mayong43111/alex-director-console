import type { SkillDefinitionRecord } from '../models'

export async function listSkills(signal?: AbortSignal): Promise<SkillDefinitionRecord[]> {
  const response = await fetch('/api/skills', { signal })
  if (!response.ok) throw new Error('技能列表加载失败')
  return response.json() as Promise<SkillDefinitionRecord[]>
}

export async function setSkillEnabled(
  skillId: string,
  isEnabled: boolean,
): Promise<SkillDefinitionRecord> {
  const response = await fetch(`/api/skills/${skillId}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ isEnabled }),
  })
  if (!response.ok) throw new Error('技能状态更新失败')
  return response.json() as Promise<SkillDefinitionRecord>
}
