import {
  ActionIcon,
  Badge,
  Button,
  Group,
  NumberInput,
  Paper,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiDownloadOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { showErrorMsg } from '@Utils/Shared'
import api, { ChallengeWriteupReviewItemModel } from '@Api'

/// 挑战题 WP 审核：查看提交并手动加分
const ChallengeWriteups: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()

  const { data, isLoading, isValidating, mutate } = api.challengeWriteup.useChallengeWriteupReview(numId)

  const [scores, setScores] = useState<Record<number, number | string>>({})
  const [notes, setNotes] = useState<Record<number, string>>({})
  const [saving, setSaving] = useState<number | null>(null)

  const items = useMemo(() => data?.items ?? [], [data])
  const pending = useMemo(() => items.filter((i) => !i.awardedAtUtc).length, [items])

  const onAward = async (item: ChallengeWriteupReviewItemModel) => {
    const raw = scores[item.id] ?? item.awardedScore ?? 0
    const score = typeof raw === 'number' ? raw : Number(raw)

    if (!Number.isFinite(score) || score < 0) {
      showNotification({ color: 'red', message: '请输入有效的加分值', icon: <Icon path={mdiCheck} size={1} /> })
      return
    }

    setSaving(item.id)
    try {
      await api.challengeWriteup.challengeWriteupAward(item.id, {
        score,
        note: notes[item.id] ?? item.reviewNote ?? null,
      })
      await mutate()
      showNotification({
        color: 'teal',
        message: `已为「${item.teamName}」的《${item.challengeTitle}》加 ${score} 分`,
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
      isLoading={isLoading}
      head={
        <>
          <Group gap="sm">
            <Title order={4}>挑战题 WP 审核</Title>
            <Badge variant="light" color={pending > 0 ? 'orange' : 'teal'}>
              待处理 {pending} / 共 {items.length}
            </Badge>
          </Group>
          <Group gap="xs">
            <Text size="sm" c="dimmed">
              加分即时计入排行榜
            </Text>
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
          <ScrollArea h="calc(100vh - 300px)" type="auto" offsetScrollbars>
            <Table highlightOnHover verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w="11rem">队伍</Table.Th>
                  <Table.Th>题目</Table.Th>
                  <Table.Th w="6rem">方向</Table.Th>
                  <Table.Th w="8rem">提交时间</Table.Th>
                  <Table.Th w="9rem">当前加分</Table.Th>
                  <Table.Th w="9rem">加分数值</Table.Th>
                  <Table.Th w="14rem">备注</Table.Th>
                  <Table.Th w="10rem">操作</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.map((item) => (
                  <Table.Tr key={item.id}>
                    <Table.Td>
                      <Text size="sm" fw={600} truncate>
                        {item.teamName}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" truncate>
                        {item.challengeTitle}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed">
                        {item.category}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">
                        {item.submitTimeUtc ? dayjs(item.submitTimeUtc).format('MM-DD HH:mm') : '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      {item.awardedAtUtc ? (
                        <Badge variant="light" color="teal">
                          +{item.awardedScore ?? 0}
                        </Badge>
                      ) : (
                        <Badge variant="light" color="orange">
                          待审核
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <NumberInput
                        size="xs"
                        min={0}
                        max={100000}
                        value={scores[item.id] ?? item.awardedScore ?? ''}
                        onChange={(v) => setScores((prev) => ({ ...prev, [item.id]: v }))}
                        placeholder="分数"
                      />
                    </Table.Td>
                    <Table.Td>
                      <TextInput
                        size="xs"
                        value={notes[item.id] ?? item.reviewNote ?? ''}
                        onChange={(e) => setNotes((prev) => ({ ...prev, [item.id]: e.currentTarget.value }))}
                        placeholder="可选"
                      />
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4} wrap="nowrap">
                        <Button size="xs" loading={saving === item.id} onClick={() => onAward(item)}>
                          保存
                        </Button>
                        <Tooltip label="下载 WP">
                          <ActionIcon
                            variant="subtle"
                            component="a"
                            href={api.challengeWriteup.challengeWriteupFileUrl(item.id)}
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <Icon path={mdiDownloadOutline} size={0.9} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {items.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={8}>
                      <Text c="dimmed" ta="center" py="xl">
                        暂无解题思路提交
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

export default ChallengeWriteups
