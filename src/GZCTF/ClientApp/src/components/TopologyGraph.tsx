import { Badge, Group, Paper, Text } from '@mantine/core'
import {
  Background,
  BackgroundVariant,
  Handle,
  Position,
  ReactFlow,
  ReactFlowProvider,
  type Node,
  type NodeProps
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { FC, useMemo } from 'react'
import { TopologyGraphModel } from '@Api'

interface TopologyGraphProps {
  topology: TopologyGraphModel
}

/** 画布节点数据 */
interface NodeData extends Record<string, unknown> {
  label: string
  port: number
  isEntry: boolean
  online: boolean
  selected?: boolean
}

type FlowNode = Node<NodeData>

/**
 * 只读拓扑节点。
 *
 * 与出题端编辑器共用同一套视觉语言：入口节点用高亮描边，
 * 已就绪的靶机用实色边框，未创建时整体降低不透明度。
 */
function GraphNode({ data }: NodeProps<FlowNode>) {
  const name = data.label?.trim() || '靶机'

  return (
    <Paper
      withBorder
      p={6}
      radius="sm"
      w={132}
      style={{
        borderColor: data.online
          ? data.isEntry
            ? 'var(--mantine-color-teal-6)'
            : 'var(--mantine-color-gray-5)'
          : 'var(--mantine-color-gray-4)',
        borderWidth: data.isEntry ? 2 : 1,
        borderStyle: data.online ? 'solid' : 'dashed',
        opacity: data.online ? 1 : 0.55,
        background: 'var(--mantine-color-body)'
      }}
    >
      <Handle type="target" position={Position.Top} style={{ opacity: 0 }} />
      <Group gap={4} justify="space-between" wrap="nowrap">
        <Text size="xs" fw={600} truncate>
          {name}
        </Text>
        {data.isEntry && (
          <Badge size="xs" color="teal" variant="light" px={4}>
            入口
          </Badge>
        )}
      </Group>
      <Text size="xs" c="dimmed">
        :{data.port}
      </Text>
      <Handle type="source" position={Position.Bottom} style={{ opacity: 0 }} />
    </Paper>
  )
}

const nodeTypes = { graph: GraphNode }

/**
 * 题目靶机拓扑（只读）。
 *
 * 以图形方式说明各靶机之间应当如何互相访问，作为题面的补充。
 * 连线只是说明性提示，平台不会据此施加任何网络隔离。
 */
export const TopologyGraph: FC<TopologyGraphProps> = ({ topology }) => {
  const { nodes, edges } = useMemo(() => {
    const flowNodes: FlowNode[] = topology.nodes.map((n) => ({
      id: n.id,
      type: 'graph',
      position: { x: n.x, y: n.y },
      draggable: false,
      selectable: false,
      data: {
        label: n.name ?? '',
        port: n.exposePort,
        isEntry: n.isEntry,
        online: n.instanceIndex >= 0
      }
    }))

    return {
      nodes: flowNodes,
      edges: topology.links.map((l) => ({
        id: l.id,
        source: l.from,
        target: l.to,
        label: l.label ?? undefined,
        animated: true,
        selectable: false
      }))
    }
  }, [topology])

  if (topology.nodes.length === 0) return null

  return (
    <Paper withBorder radius="sm" style={{ height: 220, overflow: 'hidden' }}>
      <ReactFlowProvider>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable={false}
          panOnDrag={false}
          zoomOnScroll={false}
          zoomOnDoubleClick={false}
          preventScrolling={false}
          fitView
          fitViewOptions={{ padding: 0.25 }}
          proOptions={{ hideAttribution: true }}
        >
          <Background variant={BackgroundVariant.Dots} gap={14} size={1} />
        </ReactFlow>
      </ReactFlowProvider>
    </Paper>
  )
}

export default TopologyGraph
