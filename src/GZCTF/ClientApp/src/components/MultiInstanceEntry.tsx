import { Badge, Card, Group, Stack, Text, Tooltip } from '@mantine/core'
import { mdiAlertCircleOutline, mdiServerNetwork } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { ClientFlagContext, ContainerStatus } from '@Api'
import { InstanceEntry } from './InstanceEntry'

interface MultiInstanceEntryProps {
  context: ClientFlagContext
  label?: string
  onCreate?: () => void
  onExtend?: () => void
  onDestroy?: () => void
}

const STATUS_META: Record<ContainerStatus, { label: string; color: string }> = {
  [ContainerStatus.Pending]: { label: '创建中', color: 'yellow' },
  [ContainerStatus.Running]: { label: '运行中', color: 'teal' },
  [ContainerStatus.Destroyed]: { label: '已销毁', color: 'gray' },
}

/// 单题多靶机的靶机列表。
///
/// 一道题配了多个容器时，各靶机共享同一个实例生命周期，
/// 因此倒计时与续期/销毁按钮只用一组，逐个靶机只展示各自的连接入口。
export const MultiInstanceEntry: FC<MultiInstanceEntryProps> = ({ context, label, onCreate, onExtend, onDestroy }) => {
  const entries = context.instanceEntries ?? []

  if (entries.length === 0) {
    // 全部靶机都没起来时，回退到单入口组件，保持「创建容器」按钮行为一致
    return <InstanceEntry context={context} label={label} onCreate={onCreate} onExtend={onExtend} onDestroy={onDestroy} />
  }

  return (
    <Card withBorder radius="md" p="sm">
      <Stack gap="xs">
        {label && (
          <Group gap={6}>
            <Icon path={mdiServerNetwork} size={0.8} />
            <Text size="sm" fw={600} truncate>
              {label}
            </Text>
          </Group>
        )}

        {entries.map((entry, index) => {
          const meta = STATUS_META[entry.status] ?? STATUS_META[ContainerStatus.Pending]
          const ready = !!entry.entry && entry.status === ContainerStatus.Running

          return (
            <Stack key={index} gap={4}>
              <Group justify="space-between" wrap="nowrap" gap="xs">
                <Text size="xs" fw={600} truncate>
                  {entry.name?.trim() || `靶机 ${index + 1}`}
                </Text>
                <Badge size="xs" variant="light" color={meta.color}>
                  {meta.label}
                </Badge>
              </Group>

              {ready ? (
                // 复用单入口组件：连接地址、复制、平台代理、Wsrx 映射等能力保持一致
                <InstanceEntry
                  context={{
                    instanceEntry: entry.entry,
                    closeTime: entry.closeTime ?? context.closeTime,
                  }}
                  onCreate={onCreate}
                  onExtend={onExtend}
                  onDestroy={onDestroy}
                />
              ) : (
                <Tooltip label="靶机尚未就绪" withArrow>
                  <Group gap={6} c="dimmed">
                    <Icon path={mdiAlertCircleOutline} size={0.75} />
                    <Text size="xs">等待靶机就绪…</Text>
                  </Group>
                </Tooltip>
              )}
            </Stack>
          )
        })}
      </Stack>
    </Card>
  )
}
