import { useEffect, useRef, useState } from 'react'
import ForceGraph2D, {
  type ForceGraphMethods,
  type GraphData,
  type LinkObject,
  type NodeObject,
} from 'react-force-graph-2d'
import type {
  StoryCharacterMaterial,
  StoryLocationMaterial,
  StoryPlotBeatMaterial,
  StoryRelationMaterial,
} from '../api/projectSources'

type GraphNodeKind = 'character' | 'location' | 'beat'

type StoryNodeData = {
  id: string
  kind: GraphNodeKind
  name: string
}

type StoryLinkData = {
  evidence: string
  layoutOnly: boolean
  relationType: string
  sourceName: string
  targetName: string
  storyLink: boolean
}

type StoryNode = NodeObject<StoryNodeData>
type StoryLink = LinkObject<StoryNodeData, StoryLinkData>

const nodeColors: Record<GraphNodeKind, { fill: string; stroke: string; text: string }> = {
  character: { fill: '#fff7f3', stroke: '#e9906d', text: '#4d3026' },
  location: { fill: '#f3f8ff', stroke: '#78aade', text: '#244765' },
  beat: { fill: '#f3faf5', stroke: '#70aa86', text: '#28583b' },
}

function createRelationGraph({
  characters,
  locations,
  plotBeats,
  relations,
}: {
  characters: StoryCharacterMaterial[]
  locations: StoryLocationMaterial[]
  plotBeats: StoryPlotBeatMaterial[]
  relations: StoryRelationMaterial[]
}): GraphData<StoryNodeData, StoryLinkData> {
  const nodes = new Map<string, StoryNode>()
  const links: StoryLink[] = []

  const addNode = (kind: GraphNodeKind, name: string, id = `${kind}:${name}`) => {
    if (!nodes.has(id)) nodes.set(id, { id, kind, name })
    return id
  }
  const addLink = (
    source: string,
    target: string,
    relationType: string,
    evidence: string,
    sourceName: string,
    targetName: string,
    storyLink: boolean,
    layoutOnly = false,
  ) => links.push({ source, target, relationType, evidence, sourceName, targetName, storyLink, layoutOnly })

  characters.forEach((character) => addNode('character', character.name))
  locations.forEach((location) => addNode('location', location.name))
  relations.forEach((relation) => {
    addNode('character', relation.source)
    addNode('character', relation.target)
    addLink(
      `character:${relation.source}`,
      `character:${relation.target}`,
      relation.type,
      relation.evidence,
      relation.source,
      relation.target,
      false,
    )
  })
  plotBeats.forEach((beat) => {
    const beatName = `${String(beat.order).padStart(2, '0')} ${beat.title}`
    const beatId = addNode('beat', beatName, `beat:${beat.order}:${beat.title}`)
    beat.characterNames.forEach((name) => {
      const characterId = addNode('character', name)
      addLink(beatId, characterId, '参与', beat.summary, beatName, name, true)
    })
    if (beat.locationName) {
      const locationId = addNode('location', beat.locationName)
      addLink(beatId, locationId, '发生于', beat.summary, beatName, beat.locationName, true)
    }
  })

  const parents = new Map(Array.from(nodes.keys(), (id) => [id, id]))
  const findRoot = (id: string): string => {
    const parent = parents.get(id) ?? id
    if (parent === id) return id
    const root = findRoot(parent)
    parents.set(id, root)
    return root
  }
  links.forEach((link) => {
    const source = String(link.source)
    const target = String(link.target)
    const sourceRoot = findRoot(source)
    const targetRoot = findRoot(target)
    if (sourceRoot !== targetRoot) parents.set(targetRoot, sourceRoot)
  })
  const components = new Map<string, string[]>()
  nodes.forEach((_, id) => {
    const root = findRoot(id)
    components.set(root, [...(components.get(root) ?? []), id])
  })
  const orderedComponents = Array.from(components.values()).sort((left, right) => right.length - left.length)
  const anchor = orderedComponents[0]?.[0]
  if (anchor) {
    orderedComponents.slice(1).forEach((component) => {
      addLink(anchor, component[0], '', '', '', '', false, true)
    })
  }

  return { nodes: Array.from(nodes.values()), links }
}

function getNodeRadius(node: StoryNode) {
  return node.kind === 'character' ? 24 : node.kind === 'location' ? 22 : 20
}

function getEndpoint(endpoint: StoryLink['source'] | StoryLink['target']) {
  return typeof endpoint === 'object' ? endpoint : null
}

function drawWrappedName(node: StoryNode, context: CanvasRenderingContext2D) {
  const radius = getNodeRadius(node)
  const charactersPerLine = node.kind === 'beat' ? 9 : 8
  const lines = Array.from({ length: Math.ceil(node.name.length / charactersPerLine) }, (_, index) =>
    node.name.slice(index * charactersPerLine, (index + 1) * charactersPerLine)).slice(0, 4)
  if (lines.length === 4 && node.name.length > charactersPerLine * 4) {
    lines[3] = `${lines[3].slice(0, -1)}…`
  }

  context.fillStyle = nodeColors[node.kind].text
  context.font = `600 ${node.kind === 'beat' ? 3.6 : 3.8}px "Noto Sans SC", sans-serif`
  context.textAlign = 'center'
  context.textBaseline = 'middle'
  const lineHeight = 5
  const startY = (node.y ?? 0) - ((lines.length - 1) * lineHeight) / 2
  lines.forEach((line, index) => context.fillText(line, node.x ?? 0, startY + index * lineHeight, radius * 1.65))
}

export function RelationGraph({
  characters,
  locations,
  plotBeats,
  relations,
}: {
  characters: StoryCharacterMaterial[]
  locations: StoryLocationMaterial[]
  plotBeats: StoryPlotBeatMaterial[]
  relations: StoryRelationMaterial[]
}) {
  const containerRef = useRef<HTMLDivElement>(null)
  const graphRef = useRef<ForceGraphMethods<StoryNodeData, StoryLinkData> | undefined>(undefined)
  const [size, setSize] = useState({ width: 960, height: 620 })
  const [graphData] = useState(() => createRelationGraph({ characters, locations, plotBeats, relations }))
  const [selectedRelation, setSelectedRelation] = useState<StoryLinkData | null>(null)

  useEffect(() => {
    const element = containerRef.current
    if (!element) return
    const observer = new ResizeObserver(([entry]) => {
      if (!entry) return
      setSize({ width: Math.floor(entry.contentRect.width), height: Math.floor(entry.contentRect.height) })
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    const linkForce = graphRef.current?.d3Force('link')
    const chargeForce = graphRef.current?.d3Force('charge')
    linkForce?.distance?.((link: StoryLink) => link.layoutOnly ? 170 : link.storyLink ? 108 : 140)
    chargeForce?.strength?.(-155)
    graphRef.current?.d3ReheatSimulation()
    const fitTimer = window.setTimeout(() => graphRef.current?.zoomToFit(400, 40), 2400)
    const zoomTimer = window.setTimeout(() => {
      const fittedZoom = graphRef.current?.zoom()
      if (fittedZoom) graphRef.current?.zoom(Math.min(fittedZoom * 1.12, 4), 300)
    }, 2900)
    return () => {
      window.clearTimeout(fitTimer)
      window.clearTimeout(zoomTimer)
    }
  }, [])

  return (
    <div className="relation-graph-shell" ref={containerRef}>
      <ForceGraph2D<StoryNodeData, StoryLinkData>
        ref={graphRef}
        graphData={graphData}
        width={size.width}
        height={size.height}
        backgroundColor="#fff"
        nodeLabel={(node) => node.name}
        nodeVal={(node) => node.kind === 'character' ? 18 : 14}
        nodeCanvasObject={(node, context) => {
          const radius = getNodeRadius(node)
          const colors = nodeColors[node.kind]
          context.beginPath()
          context.arc(node.x ?? 0, node.y ?? 0, radius, 0, 2 * Math.PI)
          context.fillStyle = colors.fill
          context.fill()
          context.strokeStyle = colors.stroke
          context.lineWidth = 1.2
          context.stroke()
          drawWrappedName(node, context)
        }}
        nodePointerAreaPaint={(node, color, context) => {
          context.beginPath()
          context.arc(node.x ?? 0, node.y ?? 0, getNodeRadius(node), 0, 2 * Math.PI)
          context.fillStyle = color
          context.fill()
        }}
        linkLabel={(link) => link.layoutOnly ? '' : `${link.sourceName} · ${link.relationType} · ${link.targetName}`}
        linkVisibility={(link) => !link.layoutOnly}
        linkColor={(link) => link.storyLink ? '#9db7aa' : '#efaa8f'}
        linkLineDash={(link) => link.storyLink ? [3, 2] : null}
        linkWidth={1.15}
        linkCanvasObjectMode={() => 'after'}
        linkCanvasObject={(link, context, globalScale) => {
          if (link.layoutOnly) return
          const source = getEndpoint(link.source)
          const target = getEndpoint(link.target)
          if (!source || !target || source.x === undefined || source.y === undefined || target.x === undefined || target.y === undefined) return
          const x = (source.x + target.x) / 2
          const y = (source.y + target.y) / 2
          const fontSize = Math.max(3.2, 8 / globalScale)
          context.font = `500 ${fontSize}px "Noto Sans SC", sans-serif`
          const textWidth = context.measureText(link.relationType).width
          context.fillStyle = 'rgba(255,255,255,.9)'
          context.fillRect(x - textWidth / 2 - 1.5, y - fontSize / 2 - 1, textWidth + 3, fontSize + 2)
          context.fillStyle = link.storyLink ? '#587264' : '#76594e'
          context.textAlign = 'center'
          context.textBaseline = 'middle'
          context.fillText(link.relationType, x, y)
        }}
        onNodeDragEnd={(node) => {
          node.fx = node.x
          node.fy = node.y
        }}
        onLinkClick={(link) => {
          if (!link.layoutOnly) setSelectedRelation(link)
        }}
        onBackgroundClick={() => setSelectedRelation(null)}
        warmupTicks={80}
        cooldownTicks={120}
        d3VelocityDecay={0.34}
        minZoom={0.35}
        maxZoom={4}
        enableNodeDrag
        enablePanInteraction
        enableZoomInteraction
      />
      {selectedRelation && (
        <aside className="relation-graph-evidence">
          <div>
            <strong>{selectedRelation.sourceName}</strong>
            <span>{selectedRelation.relationType}</span>
            <strong>{selectedRelation.targetName}</strong>
          </div>
          <p>{selectedRelation.evidence}</p>
        </aside>
      )}
    </div>
  )
}