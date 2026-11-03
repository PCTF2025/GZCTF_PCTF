import { ActionIcon, Badge, Group, Paper, ScrollArea, Stack, Switch, Table, Text, Title, Tooltip } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiDownloadOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import api from '@Api'

const labels: Record<string, string> = {
  waiting: '等待采集器',
  capturing: '抓包中',
  stopping: '正在结束',
  stopped: '已停止',
  uploading: '上传校验中',
  archived: '已归档',
  error: '采集异常',
}

const TrafficMonitor: FC = () => {
  const { data, isValidating, mutate } = api.trafficMonitor.useTrafficMonitorStatus({ refreshInterval: 5000 })
  const [busy, setBusy] = useState(false)
  const changeCapture = async (enabled: boolean) => {
    setBusy(true)
    try {
      await api.trafficMonitor.trafficMonitorSettings(enabled)
      await mutate()
      showNotification({
        color: 'teal',
        message: enabled
          ? '采集已开启，仅对之后新建的靶机生效'
          : '采集已关闭，节点正在结束当前抓包并上传校验；失败文件会保留重试',
      })
    } catch {
      showNotification({ color: 'red', message: '更新采集开关失败，请刷新后重试' })
    } finally {
      setBusy(false)
    }
  }

  const downloads = (files: string[], label: string) =>
    files.length > 0 && (
      <details>
        <summary>
          {label}（{files.length}）
        </summary>
        <Stack gap={2}>
          {files.map((file) => (
            <Group key={file} gap={4} wrap="nowrap">
              <Tooltip label={file}>
                <Text size="xs" ff="monospace" truncate maw={220}>
                  {file.split('/').pop()}
                </Text>
              </Tooltip>
              <ActionIcon
                size="sm"
                variant="subtle"
                component="a"
                href={api.trafficMonitor.trafficMonitorDownloadUrl(file)}
              >
                <Icon path={mdiDownloadOutline} size={0.8} />
              </ActionIcon>
            </Group>
          ))}
        </Stack>
      </details>
    )

  return (
    <AdminPage
      isLoading={!data}
      head={
        <>
          <Group gap="sm">
            <Title order={4}>靶机流量监控</Title>
            <Badge variant="light">抓包中 {data?.items.filter((i) => i.state === 'capturing').length ?? 0}</Badge>
          </Group>
          <Group gap="md">
            <Switch
              checked={data?.enabled ?? false}
              disabled={busy || !data}
              onChange={(event) => void changeCapture(event.currentTarget.checked)}
              label="流量与日志采集"
            />
            <Tooltip label="刷新">
              <ActionIcon variant="subtle" loading={isValidating} onClick={() => void mutate()}>
                <Icon path={mdiRefresh} size={0.9} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </>
      }
    >
      <Stack w="100%" gap="md">
        <Text size="sm" c="dimmed">
          开启后仅采集新建靶机；关闭后结束所有当前抓包。无论是否解题均归档流量，系统日志实时上传。
          文件上传并回读校验成功后自动清理本地副本，上传失败会保留并重试。
        </Text>
        <Paper p="md" withBorder>
          <ScrollArea h="calc(100vh - 280px)" type="auto" offsetScrollbars>
            <Table highlightOnHover verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>题目 / Pod</Table.Th>
                  <Table.Th>队伍</Table.Th>
                  <Table.Th>启动用户 / IP</Table.Th>
                  <Table.Th>节点</Table.Th>
                  <Table.Th>流量</Table.Th>
                  <Table.Th>状态</Table.Th>
                  <Table.Th>OSS 归档</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {data?.items.map((item) => (
                  <Table.Tr key={item.id}>
                    <Table.Td>
                      <Text size="sm">{item.title}</Text>
                      <Text size="xs" c="dimmed" ff="monospace">
                        {item.pod}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{item.team}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{item.users}</Text>
                      <Text size="xs" c="dimmed">
                        {item.ip ?? '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs">{item.node ?? '—'}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{item.sizeMB ?? 0} MB</Text>
                    </Table.Td>
                    <Table.Td>
                      <Badge color={item.state === 'error' ? 'red' : item.state === 'archived' ? 'violet' : 'teal'}>
                        {labels[item.state ?? 'waiting'] ?? item.state}
                      </Badge>
                      {(item.pendingLogs ?? 0) > 0 && (
                        <Text size="xs" c="orange">
                          待上传日志 {item.pendingLogs}
                        </Text>
                      )}
                      {item.error && (
                        <Text size="xs" c="red" maw={260}>
                          {item.error}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Stack gap={4}>
                        {downloads(item.keepFiles ?? [], '流量')}
                        {downloads(item.logFiles ?? [], '日志与元数据')}
                      </Stack>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {!data?.items.length && (
                  <Table.Tr>
                    <Table.Td colSpan={7}>
                      <Text c="dimmed" ta="center" py="xl">
                        暂无采集记录；开启后新建靶机开始采集
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
