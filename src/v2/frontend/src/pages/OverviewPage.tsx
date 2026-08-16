import { ArrowRight, Check, ChevronRight, Clock3, Play, Sparkles } from 'lucide-react'
import { blockers, episodeRuns, stages } from '../data/mockData'

export function OverviewPage() {
  return (
    <div className="page overview-page">
      <header className="page-header">
        <div><span className="eyebrow">项目 / 天桥食堂</span><h1>制作驾驶舱</h1><p>全项目事实、阻断与并行生产状态</p></div>
        <button className="primary-button"><Play size={15} fill="currentColor" />继续制作</button>
      </header>
      <section className="stage-track">
        {stages.map((stage, index) => <div className={`stage-step ${stage.state}`} key={stage.label}><div className="stage-node">{stage.state === 'done' ? <Check size={14} /> : index + 1}</div><strong>{stage.label}</strong><span>{stage.detail}</span>{index < stages.length - 1 && <i />}</div>)}
      </section>
      <div className="overview-grid">
        <section className="panel"><PanelHeader title="阻断与待确认" meta="3 项需要处理" /><div>{blockers.map((item) => <button key={item.title} className="issue-row"><span className={`issue-icon ${item.level}`}>{item.level === 'decision' ? '?' : '!'}</span><span><strong>{item.title}</strong><small>{item.level === 'blocker' ? '阻断剧本锁定' : item.level === 'decision' ? '等待导演决定' : '不阻断当前生产'}</small></span><span className="row-action">{item.action}<ChevronRight size={14} /></span></button>)}</div></section>
        <section className="panel"><PanelHeader title="并行生产" meta="2 集运行中" /><div>{episodeRuns.map((run) => <button className="run-row" key={run.episode}><span className="episode-code">{run.episode}</span><span className="run-name"><strong>{run.title}</strong><small>{run.stage}</small></span><span className={`state-label ${run.state}`}>{run.state === 'running' ? '运行中' : '待开始'}</span><span className="run-progress">{run.progress}</span><ChevronRight size={15} /></button>)}</div></section>
        <section className="panel"><PanelHeader title="最近版本变化" meta="今天" /><div className="timeline-row"><span className="timeline-icon"><Check size={14} /></span><div><strong>Script E01 v4 已创建</strong><p>第 3 场对白和动作时长已重新计算</p><small>12 分钟前 · Agent Task 8f2a</small></div></div><div className="timeline-row"><span className="timeline-icon neutral"><Clock3 size={14} /></span><div><strong>人物设定 · 林墨 v3</strong><p>服装轮廓与禁止项已更新</p><small>1 小时前 · 导演手动编辑</small></div></div></section>
        <section className="panel"><PanelHeader title="最近结果" meta="可使用" /><div className="result-preview"><div className="contact-sheet"><span>林墨 / 正面</span><span>林墨 / 侧面</span><span>办公室 / 全景</span></div><div><strong>4 张参考图已生成</strong><p>参考图可直接用于 12 个镜头，缺失项可随时补充生成。</p><button className="text-button">查看视觉资产 <ArrowRight size={14} /></button></div></div></section>
      </div>
      <section className="next-action"><span className="next-icon"><Sparkles size={17} /></span><div><span className="eyebrow">建议的下一步</span><strong>先处理 E01 时长阻断，再补充林墨参考图</strong><p>完成后可继续 E01 分镜，同时不影响正在生成 E02 首帧。</p></div><button className="secondary-button">查看执行计划</button></section>
    </div>
  )
}

function PanelHeader({ title, meta }: { title: string; meta: string }) {
  return <header className="panel-header"><h2>{title}</h2><span>{meta}</span></header>
}