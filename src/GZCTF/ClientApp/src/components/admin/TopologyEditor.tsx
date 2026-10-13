import type { ExtraContainerImage, TopologyEdge, TopologyNode } from '@Api'
import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MiniMap,
  Position,
  ReactFlow,
  ReactFlowProvider,
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  type Connection,
  type Edge,
  type EdgeChange,
  type Node,
  type NodeChange,
  type NodeProps
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { ActionIcon, Badge, Button, Group, Paper, Select, Stack, Text, TextInput, Tooltip } from '@mantine/core'
import { mdiAutoFix, mdiDelete, mdiPlus, mdiServerNetwork } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useCallback, useEffect, useMemo, useState } from 'react'

const MAX_NODES = 8

/** 画布默认网格步长（像素），新增节点按此间距横向排布 */
const GRID_X = 220
const GRID_Y = 140

/// 节点卡片尺寸，与 TargetNode 中的样式保持一致
const NODE_W = 160
const NODE_H = 82

export interface TopologyEditorValue {
  nodes: TopologyNode[]
  edges: TopologyEdge[]
}

interface Props {
  value: TopologyEditorValue
  onChange: (value: TopologyEditorValue) => void
  imagePrefix?: string
}

/** 画布上呈现的节点数据 */
interface NodeData extends Record<string, unknown> {
  index: number
  label: string
  port: number
  isEntry: boolean
  image: string
  selected?: boolean
}

type FlowNode = Node<NodeData>

/**
 * 自定义节点：展示序号、名称、端口与入口徽章。
 *
 * 上下各留一个连接桩，便于出题人从任意方向拖出连线。
 */
function TargetNode({ data }: NodeProps<FlowNode>) {
  const name = data.label?.trim() || `靶机 ${data.index + 1}`

  return (
    <Paper
      withBorder
      p="xs"
      radius="sm"
      w={NODE_W}
      style={{
        borderColor: data.isEntry ? 'var(--mantine-color-teal-6)' : undefined,
        borderWidth: data.isEntry ? 2 : 1,
        background: 'var(--mantine-color-body)'
      }}
    >
      <Handle type="target" position={Position.Top} style={HANDLE_STYLE} />
      <Stack gap={2}>
        <Group gap={4} justify="space-between" wrap="nowrap">
          <Text size="xs" c="dimmed">
            #{data.index + 1}
          </Text>
          {data.isEntry && (
            <Badge size="xs" color="teal" variant="light">
              入口
            </Badge>
          )}
        </Group>
        <Text size="sm" fw={600} truncate>
          {name}
        </Text>
        <Text size="xs" c="dimmed">
          :{data.port}
        </Text>
      </Stack>
      <Handle type="source" position={Position.Bottom} style={HANDLE_STYLE} />
    </Paper>
  )
}

/// 连接桩热区。
///
/// 注意：这里只能设置尺寸/配色，不能覆盖 position/transform 等定位属性，
/// 否则会破坏 React Flow 自身对 handle 的定位与命中区域计算，导致拖不出连线。
const HANDLE_STYLE = {
  width: 16,
  height: 16,
  background: 'var(--mantine-color-teal-6)',
  border: '2px solid var(--mantine-color-body)',
  cursor: 'crosshair'
} as const

const nodeTypes = { target: TargetNode }

/** 把外部数据转成 React Flow 的节点/边 */
function toFlow(nodes: TopologyNode[], edges: TopologyEdge[]): { nodes: FlowNode[]; edges: Edge[] } {
  return {
    nodes: nodes.map((n, i) => ({
      id: n.id,
      type: 'target',
      position: { x: n.x || (i % 3) * GRID_X, y: n.y || Math.floor(i / 3) * GRID_Y },
      // 显式声明尺寸：画布在弹窗/标签页中挂载时父容器尺寸可能尚未稳定，
      // 缺少该信息会让 React Flow 迟迟不把节点从测量态切换为可见。
      width: NODE_W,
      height: NODE_H,
      data: {
        index: i,
        label: n.name ?? '',
        port: n.exposePort,
        isEntry: n.isEntry,
        image: n.image
      }
    })),
    edges: edges.map((e) => ({
      id: e.id,
      source: e.from,
      target: e.to,
      label: e.label ?? undefined,
      animated: true,
      style: { strokeWidth: 2 },
      markerEnd: { type: 'arrowclosed' as never }
    }))
  }
}

/** 把 React Flow 的节点/边转回业务数据 */
function fromFlow(flowNodes: FlowNode[], flowEdges: Edge[]): TopologyEditorValue {
  const nodes: TopologyNode[] = flowNodes.map((n, i) => ({
    id: n.id,
    name: (n.data.label as string)?.trim() || null,
    image: (n.data.image as string) ?? '',
    exposePort: Number(n.data.port) || 80,
    isEntry: Boolean(n.data.isEntry),
    enableTrafficCapture: false,
    x: n.position.x,
    y: n.position.y
  }))

  return {
    nodes,
    edges: flowEdges.map((e) => ({
      id: e.id,
      from: e.source,
      to: e.target,
      label: typeof e.label === 'string' ? e.label : null
    }))
  }
}

/**
 * 靶机拓扑可视化编辑器。
 *
 * 出题人拖拽节点描述靶机编排，连线表示靶机之间应当如何互相访问。
 * 拓扑仅用于编排与题面说明，平台不会据此施加任何网络隔离。
 */
export function TopologyEditor({ value, onChange, imagePrefix = '' }: Props) {
  const flow = useMemo(() => toFlow(value.nodes, value.edges), [value.nodes, value.edges])

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selectedEdge, setSelectedEdge] = useState<string | null>(null)

  /// 节点数量变化后重新适配视图，避免新增节点落在可视区之外
  useEffect(() => {
    const timer = setTimeout(() => {
      document
        .querySelectorAll<HTMLElement>('.topology-canvas .react-flow__controls-fitview')
        .forEach((el) => el.click())
    }, 60)
    return () => clearTimeout(timer)
  }, [value.nodes.length])

  const node = value.nodes.find((n) => n.id === selectedId) ?? null
  const edge = value.edges.find((e) => e.id === selectedEdge) ?? null

  const nodeName = useCallback(
    (id: string, index?: number) => {
      const n = value.nodes.find((v) => v.id === id)
      return n?.name?.trim() || `靶机 ${(index ?? value.nodes.indexOf(n!)) + 1}`
    },
    [value.nodes]
  )

  const onNodesChange = useCallback(
    (changes: NodeChange<FlowNode>[]) => {
      const next = applyNodeChanges(changes, flow.nodes) as FlowNode[]
      onChange(fromFlow(next, flow.edges))
    },
    [flow, onChange]
  )

  const onEdgesChange = useCallback(
    (changes: EdgeChange[]) => {
      const next = applyEdgeChanges(changes, flow.edges)
      onChange(fromFlow(flow.nodes, next))
    },
    [flow, onChange]
  )

  const onConnect = useCallback(
    (conn: Connection) => {
      if (!conn.source || !conn.target || conn.source === conn.target) return
      const id = `${conn.source}--${conn.target}`
      // 同一对节点之间只保留一条连线，重复拖拽不叠加
      if (flow.edges.some((e) => e.id === id)) return
      const next = addEdge({ ...conn, id }, flow.edges)
      onChange(fromFlow(flow.nodes, next))
    },
    [flow, onChange]
  )

  /// 拖拽过程中的实时校验：自连与重复连线直接判定为非法，
  /// 连接桩会给出红色反馈，避免出现「松手后什么都没发生」的困惑。
  const isValidConnection = useCallback(
    (conn: Connection | Edge) => {
      const { source, target } = conn
      if (!source || !target || source === target) return false
      return !flow.edges.some((e) => e.id === `${source}--${target}`)
    },
    [flow.edges]
  )

  const addNode = () => {
    if (value.nodes.length >= MAX_NODES) return

    const i = value.nodes.length
    const id = `n${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`
    const isFirst = value.nodes.length === 0

    onChange({
      ...value,
      nodes: [
        ...value.nodes,
        {
          id,
          name: null,
          image: '',
          exposePort: 80,
          isEntry: isFirst, // 第一个节点默认作为入口
          enableTrafficCapture: false,
          x: (i % 3) * GRID_X,
          y: Math.floor(i / 3) * GRID_Y
        }
      ]
    })
    setSelectedId(id)
    setSelectedEdge(null)
  }

  const updateNode = (patch: Partial<TopologyNode>) => {
    if (!node) return
    onChange({
      ...value,
      nodes: value.nodes.map((n) => (n.id === node.id ? { ...n, ...patch } : n))
    })
  }

  const removeNode = (id: string) => {
    onChange({
      nodes: value.nodes.filter((n) => n.id !== id),
      // 同步清理悬空连线，避免保存后出现指向不存在节点的边
      edges: value.edges.filter((e) => e.from !== id && e.to !== id)
    })
    setSelectedId(null)
  }

  const removeEdge = (id: string) => {
    onChange({ ...value, edges: value.edges.filter((e) => e.id !== id) })
    setSelectedEdge(null)
  }

  /** 入口节点置底排列，其余按纵向位置层级展开 */
  const autoLayout = () => {
    const entries = value.nodes.filter((n) => n.isEntry)
    const rest = value.nodes.filter((n) => !n.isEntry)

    const order = [...entries, ...rest]
    const laid = order.map((n, i) => ({
      ...n,
      x: (i % 3) * GRID_X,
      y: Math.floor(i / 3) * GRID_Y
    }))

    onChange({ ...value, nodes: laid })
  }

  return (
    <Stack gap="sm">
      <Group gap="xs" justify="space-between">
        <Group gap="xs">
          <Icon path={mdiServerNetwork} size={0.9} />
          <Text fw={600}>靶机拓扑</Text>
          <Badge size="sm" variant="light">
            {value.nodes.length} / {MAX_NODES}
          </Badge>
        </Group>
        <Group gap="xs">
          <Button
            size="xs"
            variant="light"
            leftSection={<Icon path={mdiPlus} size={0.8} />}
            disabled={value.nodes.length >= MAX_NODES}
            onClick={addNode}
          >
            添加靶机
          </Button>
          <Button
            size="xs"
            variant="subtle"
            leftSection={<Icon path={mdiAutoFix} size={0.8} />}
            disabled={value.nodes.length === 0}
            onClick={autoLayout}
          >
            自动布局
          </Button>
        </Group>
      </Group>

      <Text size="xs" c="dimmed">
        拖拽节点描述靶机编排；<b>按住节点上/下边缘的圆点拖到另一个节点</b>即可建立连线，
        表示它们之间应当如何互相访问（如 HTTP、SQL）。
        拓扑仅用于编排与题面说明，平台不会施加任何网络隔离；勾选「下发给选手」的节点才会把地址展示给选手。
      </Text>

      <Group align="flex-start" gap="sm" wrap="nowrap" style={{ width: '100%' }}>
        <Paper
          withBorder
          className="topology-canvas"
          style={{ height: 420, flex: '1 1 auto', minWidth: 0, overflow: 'hidden' }}
        >
          <ReactFlowProvider>
            <ReactFlow
              nodes={flow.nodes}
              edges={flow.edges}
              nodeTypes={nodeTypes}
              onNodesChange={onNodesChange}
              onEdgesChange={onEdgesChange}
              onConnect={onConnect}
              isValidConnection={isValidConnection}
              onNodeClick={(_, n) => {
                setSelectedId(n.id)
                setSelectedEdge(null)
              }}
              onEdgeClick={(_, e) => {
                setSelectedEdge(e.id)
                setSelectedId(null)
              }}
              onPaneClick={() => {
                setSelectedId(null)
                setSelectedEdge(null)
              }}
              fitView
              fitViewOptions={{ padding: 0.3, maxZoom: 1 }}
              minZoom={0.3}
              maxZoom={1.5}
              deleteKeyCode={null}
              proOptions={{ hideAttribution: true }}
              connectionLineStyle={{ strokeWidth: 2, stroke: 'var(--mantine-color-teal-6)' }}
            >
              <Background variant={BackgroundVariant.Dots} gap={16} size={1} />
              <Controls showInteractive={false} />
              <MiniMap
                pannable
                zoomable
                position="bottom-right"
                style={{ width: 120, height: 80 }}
                maskColor="rgba(0,0,0,0.08)"
              />
            </ReactFlow>
          </ReactFlowProvider>
        </Paper>

        <Paper withBorder p="sm" style={{ width: 260, flex: '0 0 260px', alignSelf: 'stretch' }}>
          {node ? (
            <Stack gap="xs">
              <Group justify="space-between">
                <Text size="sm" fw={600}>
                  靶机属性
                </Text>
                <Tooltip label="删除该靶机" withArrow>
                  <ActionIcon color="red" variant="subtle" onClick={() => removeNode(node.id)}>
                    <Icon path={mdiDelete} size={0.8} />
                  </ActionIcon>
                </Tooltip>
              </Group>

              <TextInput
                size="xs"
                label="名称"
                placeholder="Web / 数据库 / Node-1"
                value={node.name ?? ''}
                onChange={(e) => updateNode({ name: e.currentTarget.value })}
              />

              <TextInput
                size="xs"
                label="镜像"
                placeholder="pctf2026/web-xxx"
                description={imagePrefix ? `前缀已固定：${imagePrefix}` : undefined}
                value={
                  imagePrefix && node.image.startsWith(imagePrefix)
                    ? node.image.slice(imagePrefix.length)
                    : node.image
                }
                onChange={(e) => {
                  const v = e.currentTarget.value.trim()
                  updateNode({ image: imagePrefix && v ? `${imagePrefix}${v}` : v })
                }}
              />

              <TextInput
                size="xs"
                label="暴露端口"
                type="number"
                value={String(node.exposePort)}
                onChange={(e) => updateNode({ exposePort: Number(e.currentTarget.value) || 80 })}
              />

              <Button
                size="xs"
                variant={node.isEntry ? 'filled' : 'light'}
                color="teal"
                onClick={() => updateNode({ isEntry: !node.isEntry })}
              >
                {node.isEntry ? '✓ 下发给选手' : '下发给选手'}
              </Button>
            </Stack>
          ) : edge ? (
            <Stack gap="xs">
              <Group justify="space-between">
                <Text size="sm" fw={600}>
                  连线属性
                </Text>
                <Tooltip label="删除该连线" withArrow>
                  <ActionIcon color="red" variant="subtle" onClick={() => removeEdge(edge.id)}>
                    <Icon path={mdiDelete} size={0.8} />
                  </ActionIcon>
                </Tooltip>
              </Group>

              <Text size="xs" c="dimmed">
                {nodeName(edge.from)} → {nodeName(edge.to)}
              </Text>

              <Select
                size="xs"
                label="访问方式"
                placeholder="不标注"
                clearable
                data={['HTTP', 'HTTPS', 'TCP', 'SQL', 'MySQL', 'PostgreSQL', 'Redis', 'RPC', 'MQTT']}
                value={edge.label ?? null}
                onChange={(v) =>
                  onChange({
                    ...value,
                    edges: value.edges.map((e) => (e.id === edge.id ? { ...e, label: v ?? null } : e))
                  })
                }
              />
            </Stack>
          ) : (
            <Stack gap="xs" align="center" justify="center" h="100%">
              <Icon path={mdiServerNetwork} size={1.6} color="var(--mantine-color-gray-5)" />
              <Text size="xs" c="dimmed" ta="center">
                选中画布上的节点或连线
                <br />
                即可在此编辑属性
                <br />
                <br />
                连线方式：按住节点
                <b>上/下边缘的圆点</b>
                <br />
                拖到另一个节点边缘松手
              </Text>
            </Stack>
          )}
        </Paper>
      </Group>
    </Stack>
  )
}

export default TopologyEditor
