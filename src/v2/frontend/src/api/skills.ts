export interface SkillRecord {
  id: string
  name: string
  description: string
  version: string
  isEnabled: boolean
  isSystem: boolean
  allowedTools: string[]
  content: string
  sourcePath: string
}

export async function listSkills(signal?: AbortSignal): Promise<SkillRecord[]> {
  const response = await fetch('/api/v2/skills', { signal })
  if (!response.ok) throw new Error('Skill 目录加载失败。')
  return response.json() as Promise<SkillRecord[]>
}

export async function updateSkill(
  skillId: string,
  isEnabled: boolean,
): Promise<SkillRecord> {
  const response = await fetch(`/api/v2/skills/${encodeURIComponent(skillId)}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ isEnabled }),
  })
  if (!response.ok) throw new Error('Skill 状态更新失败。')
  return response.json() as Promise<SkillRecord>
}