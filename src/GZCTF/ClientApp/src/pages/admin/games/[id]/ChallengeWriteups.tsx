import {
  ActionIcon,
  Badge,
  Button,
  Group,
  NumberInput,
  Select,
  Paper,
  ScrollArea,
  SegmentedControl,
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
import { useUser } from '@Hooks/useUser'
import api, { ChallengeWriteupReviewItemModel, Role } from '@Api'

/// 挑战题 WP 审核：查看提交并手动加分
const ChallengeWriteups: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()

  const { data, isLoading, isValidating, mutate } = api.challengeWriteup.useChallengeWriteupReview(numId)
  const { user } = useUser()
  // 学校学籍审核只能查看与下载，不能加分
  const canAward =
    user?.role === Role.Admin || user?.isChallengeAuthor === true || user?.isChallengeManager === true

  const [scores, setScores] = useState<Record<number, number | string>>({})
  const [notes, setNotes] = useState<Record<number, string>>({})
  const [saving, setSaving] = useState<number | null>(null)

  const allItems = useMemo(() => data?.items ?? [], [data])

  const [kind, setKind] = useState<'all' | 'week' | 'challenge'>('all')
  const [weekFilter, setWeekFilter] = useState<string | null>(null)
  const [schoolFilter, setSchoolFilter] = useState<string | null>(null)
  const [divisionFilter, setDivisionFilter] = useState<string | null>(null)

  const weekOptions = useMemo(() => {
    const weeks = Array.from(
      new Set(allItems.filter((i) => !i.isChallengeBucket).map((i) => i.week ?? 0))
    ).sort((a, b) => a - b)
    return weeks.map((w) => ({ value: String(w), label: `第 ${w} 周` }))
  }, [allItems])

  const schoolOptions = useMemo(() => {
    const schools = Array.from(new Set(allItems.map((i) => i.school || '').filter(Boolean))).sort()
    return schools.map((s) => ({ value: s, label: s }))
  }, [allItems])

  const divisionOptions = useMemo(() => {
    const divisions = Array.from(
      new Set(allItems.map((i) => i.division || '').filter(Boolean))
    ).sort()
    return divisions.map((d) => ({ value: d, label: d }))
  }, [allItems])

  const items = useMemo(
    () =>
      allItems.filter((i) => {
        if (kind === 'week' && i.isChallengeBucket) return false
        if (kind === 'challenge' && !i.isChallengeBucket) return false
        if (weekFilter && String(i.week ?? '') !== weekFilter) return false
        if (schoolFilter && (i.school || '') !== schoolFilter) return false
        if (divisionFilter && (i.division || '') !== divisionFilter) return false
        return true
      }),
    [allItems, kind, weekFilter, schoolFilter, divisionFilter]
  )

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
        message: `已为「${item.teamName}」第 ${item.week} 周解题思路加 ${score} 分`,
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
            <Title order={4}>WP 审核</Title>
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
          <Group gap="sm" wrap="wrap">
            <SegmentedControl
              size="xs"
              value={kind}
              onChange={(v) => setKind(v as 'all' | 'week' | 'challenge')}
              data={[
                { value: 'all', label: '全部' },
                { value: 'week', label: '按周报告' },
                { value: 'challenge', label: '挑战题' },
              ]}
            />
            <Select
              size="xs"
              w="8rem"
              clearable
              placeholder="全部周次"
              data={weekOptions}
              value={weekFilter}
              onChange={setWeekFilter}
              disabled={kind === 'challenge'}
            />
            <Select
              size="xs"
              w="12rem"
              clearable
              searchable
              placeholder="全部学校"
              data={schoolOptions}
              value={schoolFilter}
              onChange={setSchoolFilter}
            />
            <Select
              size="xs"
              w="12rem"
              clearable
              searchable
              placeholder="全部组别"
              data={divisionOptions}
              value={divisionFilter}
              onChange={setDivisionFilter}
            />
          </Group>
        </Paper>
        <Paper p="md" withBorder>
          <ScrollArea h="calc(100vh - 300px)" type="auto" offsetScrollbars>
            <Table highlightOnHover verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w="9rem">队伍</Table.Th>
                  <Table.Th w="7rem">类型</Table.Th>
                  <Table.Th w="13rem">学校</Table.Th>
                  <Table.Th w="13rem">组别</Table.Th>
                  <Table.Th maw="10rem">题目</Table.Th>
                  <Table.Th w="5rem">方向</Table.Th>
                  <Table.Th w="8rem">提交时间</Table.Th>
                  <Table.Th w="7rem">当前加分</Table.Th>
                  <Table.Th w="8rem">加分数值</Table.Th>
                  <Table.Th w="9rem">备注</Table.Th>
                  <Table.Th w="7rem">操作</Table.Th>
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
                      <Badge size="sm" variant="light" color={item.isChallengeBucket ? 'orange' : 'indigo'}>
                        {item.isChallengeBucket ? '挑战题' : `第 ${item.week} 周`}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed" lineClamp={1}>
                        {item.school || '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed" lineClamp={1}>
                        {item.division || '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" truncate>
                        {item.challengeTitle || '按周提交'}
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
                        disabled={!canAward}
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
                        <Button
                          size="xs"
                          disabled={!canAward}
                          loading={saving === item.id}
                          onClick={() => onAward(item)}
                        >
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
