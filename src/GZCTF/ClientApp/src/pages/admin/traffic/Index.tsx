import {
  ActionIcon,
  Badge,
  Group,
  Paper,
  ScrollArea,
  Stack,
  Switch,
  Table,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { mdiDownloadOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import api from '@Api'

/// 靶机流量监控：展示谁在打哪个靶机、抓包大小、是否已保留 pcap
const TrafficMonitor: FC = () => {
  const { data, isValidating, mutate } = api.trafficMonitor.useTrafficMonitorStatus(undefined, true)
  const [onlySolved, setOnlySolved] = useState(false)

  const items = useMemo(() => {
    const list = data?.items ?? []
    return onlySolved ? list.filter((i) => i.solved || i.frozen) : list
  }, [data, onlySolved])

  const solvedCount = (data?.items ?? []).filter((i) => i.solved || i.frozen).length

  return (
    <AdminPage
      isLoading={false}
      head={
        <>
          <Group gap="sm">
            <Title order={4}>靶机流量监控</Title>
            <Badge variant="light" color="teal">
              在跑 {data?.items?.length ?? 0}
            </Badge>
            <Badge variant="light" color={solvedCount > 0 ? 'violet' : 'gray'}>
              已保留 {solvedCount}
            </Badge>
          </Group>
          <Group gap="md">
            <Text size="xs" c="dimmed">
              更新于 {data?.updatedAt ? data.updatedAt.replace('T', ' ') : '—'}（每 30 秒）
            </Text>
            <Switch
              size="xs"
              checked={onlySolved}
              onChange={(e) => setOnlySolved(e.currentTarget.checked)}
              label="只看已解出"
            />
            <Tooltip label="刷新">
              <ActionIcon variant="subtle" loading={isValidating} onClick={() => mutate()}>
                <Icon path={mdiRefresh} size={0.9} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </>
      }
    >
      <Stack w="100%" gap="md">
        <Paper p="md" withBorder>
          <ScrollArea h="calc(100vh - 280px)" type="auto" offsetScrollbars>
            <Table highlightOnHover verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>题目</Table.Th>
                  <Table.Th w="13rem">队伍</Table.Th>
                  <Table.Th w="12rem">队员</Table.Th>
                  <Table.Th w="9rem">节点</Table.Th>
                  <Table.Th w="6rem">抓包</Table.Th>
                  <Table.Th w="6rem">状态</Table.Th>
                  <Table.Th w="14rem">保留的 pcap</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.map((item) => (
                  <Table.Tr key={item.pod}>
                    <Table.Td>
                      <Text size="sm" lineClamp={1}>
                        {item.title}
                      </Text>
                      <Text size="xs" c="dimmed" ff="monospace">
                        {item.pod}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" truncate>
                        {item.team}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" truncate c="teal">
                        {item.users || '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">
                        {item.node === 'nb7facnlqc4crm' ? 'challenge1' : item.node === 'nbo58xvr62tje3' ? 'challenge2' : item.node}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="monospace">
                        {item.sizeMB ?? 0} MB
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      {item.solved || item.frozen ? (
                        <Badge size="sm" variant="light" color="violet">
                          已保留
                        </Badge>
                      ) : (
                        <Badge size="sm" variant="light" color="gray">
                          抓包中
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td>
                      {(item.keepFiles ?? []).length === 0 ? (
                        <Text size="xs" c="dimmed">
                          —
                        </Text>
                      ) : (
                        <Stack gap={2}>
                          {(item.keepFiles ?? []).slice(-3).map((f) => (
                            <Group key={f} gap={4} wrap="nowrap">
                              <Tooltip label={f} multiline maw={420}>
                                <Text size="xs" ff="monospace" truncate style={{ maxWidth: 200 }}>
                                  {f.split('/').pop()}
                                </Text>
                              </Tooltip>
                              <ActionIcon
                                size="sm"
                                variant="subtle"
                                component="a"
                                href={api.trafficMonitor.trafficMonitorDownloadUrl(f)}
                              >
                                <Icon path={mdiDownloadOutline} size={0.8} />
                              </ActionIcon>
                            </Group>
                          ))}
                        </Stack>
                      )}
                    </Table.Td>
                  </Table.Tr>
                ))}
                {items.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={7}>
                      <Text c="dimmed" ta="center" py="xl">
                        暂无正在抓包的靶机
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </Paper>
      </Stack>
    </AdminPage>
  )
}

export default TrafficMonitor
