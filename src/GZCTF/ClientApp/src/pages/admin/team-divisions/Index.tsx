import {
  Badge,
  Group,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { showErrorMsg } from '@Utils/Shared'
import api, { TeamDivisionItemModel } from '@Api'

/// 审核状态 → 展示文案与颜色
const statusMeta: Record<string, { label: string; color: string }> = {
  Pending: { label: '待审核', color: 'orange' },
  Accepted: { label: '已通过', color: 'teal' },
  Rejected: { label: '已驳回', color: 'red' },
  Unsubmitted: { label: '未提交', color: 'gray' },
}

/// 队伍组别：学校负责人可调整本校学生所在队伍的组别
const TeamDivisions: FC = () => {
  const { t } = useTranslation()
  const { data: games } = api.teamDivision.useTeamDivisionGames()
  const [gameId, setGameId] = useState<number>(0)
  const [saving, setSaving] = useState<number | null>(null)

  const gameOptions = useMemo(
    () => (games ?? []).map((g) => ({ value: String(g.id), label: g.title || `#${g.id}` })),
    [games]
  )

  // 默认选中第一场比赛
  const activeGameId = gameId || Number(gameOptions[0]?.value ?? 0)

  const { data, mutate } = api.teamDivision.useTeamDivisionList(activeGameId, undefined, activeGameId > 0)

  const divisionOptions = useMemo(
    () => (data?.divisions ?? []).map((d) => ({ value: String(d.id), label: d.name })),
    [data]
  )

  const onChangeDivision = async (item: TeamDivisionItemModel, value: string | null) => {
    setSaving(item.participationId)
    try {
      await api.teamDivision.teamDivisionUpdate(item.participationId, {
        divisionId: value ? Number(value) : null,
      })
      await mutate()
      showNotification({
        color: 'teal',
        message: `已将「${item.teamName}」调整为 ${divisionOptions.find((d) => d.value === value)?.label ?? '未分组'}`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(null)
    }
  }

  return (
    <AdminPage
      isLoading={!data && activeGameId > 0}
      head={
        <>
          <Title order={4}>队伍组别</Title>
          <Select
            w="16rem"
            placeholder="选择比赛"
            data={gameOptions}
            value={activeGameId ? String(activeGameId) : null}
            onChange={(v) => setGameId(Number(v ?? 0))}
            allowDeselect={false}
          />
        </>
      }
    >
      <Stack w="100%" gap="md">
        <Text size="sm" c="dimmed">
          仅显示包含本校学生的队伍。可调整队伍组别（如新生组 / 老生组），审核状态需由赛事管理员处理。
        </Text>

        <Paper p="md" withBorder>
          <ScrollArea h="calc(100vh - 320px)" type="auto" offsetScrollbars>
            <Table highlightOnHover striped verticalSpacing="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w="18rem">队伍</Table.Th>
                  <Table.Th w="6rem">审核状态</Table.Th>
                  <Table.Th>成员</Table.Th>
                  <Table.Th w="14rem">组别</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {(data?.items ?? []).map((item) => (
                  <Table.Tr key={item.participationId}>
                    <Table.Td>
                      <Text fw={600} size="sm">
                        {item.teamName}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Badge
                        size="sm"
                        variant="light"
                        color={statusMeta[item.status]?.color ?? 'gray'}
                      >
                        {statusMeta[item.status]?.label ?? item.status}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Stack gap={2}>
                        {item.members.map((m) => (
                          <Group key={m.userName} gap="xs" wrap="nowrap">
                            <Text size="sm">{m.realName || m.userName}</Text>
                            <Text size="xs" c="dimmed">
                              {m.school ?? '—'}
                              {m.grade ? ` · ${m.grade}` : ''}
                            </Text>
                          </Group>
                        ))}
                      </Stack>
                    </Table.Td>
                    <Table.Td>
                      <Select
                        size="xs"
                        placeholder="未分组"
                        data={divisionOptions}
                        value={item.divisionId ? String(item.divisionId) : null}
                        disabled={saving === item.participationId}
                        onChange={(v) => onChangeDivision(item, v)}
                        allowDeselect
                        clearable
                      />
                    </Table.Td>
                  </Table.Tr>
                ))}
                {(data?.items ?? []).length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={4}>
                      <Text c="dimmed" ta="center" py="xl">
                        该比赛下没有你负责学校的学生队伍
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

export default TeamDivisions
