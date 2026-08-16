export type StageState = 'done' | 'running' | 'waiting' | 'blocked' | 'idle'

export const project = { id: 'tianqiao', name: '天桥食堂', version: 'Script Package v4' }

export const stages: Array<{ label: string; state: StageState; detail: string }> = [
  { label: '设定', state: 'done', detail: '6/6 质量门' },
  { label: '故事', state: 'done', detail: '8/8 质量门' },
  { label: '资产', state: 'waiting', detail: '2 项待确认' },
  { label: '剧本', state: 'blocked', detail: '第 3 场超载' },
  { label: '参考图', state: 'running', detail: '14/18 完成' },
  { label: '分镜', state: 'idle', detail: '尚未锁定' },
  { label: '生产', state: 'idle', detail: '2 集可并行' },
  { label: '交付', state: 'idle', detail: '未开始' },
]

export const episodeRuns = [
  { episode: 'E01', title: '失控的早晨', stage: '视频生成', progress: '11 / 18', state: 'running' },
  { episode: 'E02', title: '追查与反转', stage: '首帧生成', progress: '6 / 16', state: 'running' },
  { episode: 'E03', title: '真相回收', stage: '生产预检', progress: '0 / 14', state: 'waiting' },
]

export const blockers = [
  { level: 'blocker', title: 'E01 · 第 3 场对白超出 3.8 秒', action: '生成压缩提议' },
  { level: 'decision', title: '林墨标准人物图需要二选一', action: '进入候选审阅' },
  { level: 'warning', title: 'E02 · 2 个镜头缺少道具参考', action: '补齐参考图' },
]