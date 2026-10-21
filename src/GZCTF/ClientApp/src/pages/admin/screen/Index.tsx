import { ActionIcon, Badge, Group, Paper, Progress, SimpleGrid, Stack, Text, Tooltip } from '@mantine/core'
import { mdiAccountCheckOutline, mdiAccountClockOutline, mdiAccountGroupOutline, mdiDomain, mdiFullscreen, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useCallback, useMemo, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import api, { SchoolStatItemModel } from '@Api'

/// 自动刷新间隔（毫秒）
const REFRESH_INTERVAL = 20_000

/// 学校报名数据大屏：适合投屏展示各校报名人数
const Screen: FC = () => {
  const [refreshing, setRefreshing] = useState(false)

  const { data, isValidating, mutate } = api.school.useSchoolStats({
    refreshInterval: REFRESH_INTERVAL,
    revalidateOnFocus: true,
  })

  const schools = useMemo(() => data?.schools ?? [], [data])
  const total = data?.total ?? 0
  const approved = data?.approvedTotal ?? 0
  const pending = useMemo(() => schools.reduce((sum, s) => sum + (s.pendingCount ?? 0), 0), [schools])
  const maxCount = useMemo(() => schools.reduce((max, s) => Math.max(max, s.userCount ?? 0), 0), [schools])

  const onRefresh = useCallback(async () => {
    setRefreshing(true)
    try {
      await mutate()
    } finally {
      setRefreshing(false)
    }
  }, [mutate])

  const onFullscreen = useCallback(() => {
    if (document.fullscreenElement) void document.exitFullscreen()
    else void document.documentElement.requestFullscreen()
  }, [])

  const updatedAt = data?.generatedAtUtc ? dayjs(data.generatedAtUtc).format('HH:mm:ss') : '—'

  return (
    <AdminPage
      isLoading={!data}
      head={
        <>
          <Group gap="sm">
            <Icon path={mdiDomain} size={1.1} />
            <Text fw="bold" size="lg">
              报名数据大屏
            </Text>
            <Badge variant="light" size="lg">
              每 {REFRESH_INTERVAL / 1000} 秒自动刷新
            </Badge>
          </Group>
          <Group gap="xs">
            <Text size="sm" c="dimmed">
              更新于 {updatedAt}
            </Text>
            <Tooltip label="立即刷新">
              <ActionIcon variant="subtle" loading={refreshing || isValidating} onClick={onRefresh}>
                <Icon path={mdiRefresh} size={0.9} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="全屏展示">
              <ActionIcon variant="subtle" onClick={onFullscreen}>
                <Icon path={mdiFullscreen} size={0.9} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </>
      }
    >
      <Stack gap="md">
        {/* 总量概览 */}
        <SimpleGrid cols={{ base: 2, md: 4 }} spacing="md">
          <SummaryCard icon={mdiAccountGroupOutline} label="报名总人数" value={total} color="blue" />
          <SummaryCard icon={mdiAccountCheckOutline} label="学籍已通过" value={approved} color="teal" />
          <SummaryCard icon={mdiAccountClockOutline} label="待审核" value={pending} color="orange" />
          <SummaryCard icon={mdiDomain} label="覆盖学校/组" value={schools.length} color="grape" />
        </SimpleGrid>

        {/* 各校报名人数 */}
        <Paper p="md" withBorder>
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="sm" verticalSpacing="xs">
            {schools.map((school, index) => (
              <SchoolRow key={school.slug} school={school} rank={index + 1} maxCount={maxCount} total={total} />
            ))}
            {schools.length === 0 && (
              <Text c="dimmed" ta="center" py="xl">
                暂无报名数据
              </Text>
            )}
          </SimpleGrid>
        </Paper>
      </Stack>
    </AdminPage>
  )
}

interface SummaryCardProps {
  icon: string
  label: string
  value: number
  color: string
}

const SummaryCard: FC<SummaryCardProps> = ({ icon, label, value, color }) => (
  <Paper p="md" withBorder>
    <Group gap="xs" wrap="nowrap">
      <Icon path={icon} size={1.4} color={`var(--mantine-color-${color}-5)`} />
      <Stack gap={0}>
        <Text size="xs" c="dimmed">
          {label}
        </Text>
        <Text
          fw="bold"
          style={{ fontSize: 'clamp(1.6rem, 3.2vw, 2.6rem)', lineHeight: 1.1, fontVariantNumeric: 'tabular-nums' }}
        >
          {value}
        </Text>
      </Stack>
    </Group>
  </Paper>
)

interface SchoolRowProps {
  school: SchoolStatItemModel
  rank: number
  maxCount: number
  total: number
}

const SchoolRow: FC<SchoolRowProps> = ({ school, rank, maxCount, total }) => {
  const count = school.userCount ?? 0
  // 进度条按「相对第一名的比例」绘制，视觉对比更明显
  const barRate = maxCount > 0 ? (count / maxCount) * 100 : 0
  const shareRate = total > 0 ? (count / total) * 100 : 0

  const color = rank === 1 ? 'yellow' : rank === 2 ? 'gray' : rank === 3 ? 'orange' : 'blue'

  return (
    <Group gap="sm" wrap="nowrap" align="center">
      <Badge
        variant={rank <= 3 ? 'filled' : 'light'}
        color={color}
        size="lg"
        miw="3.1rem"
        px={8}
        style={{ flexShrink: 0, fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}
      >
        {rank}
      </Badge>

      <Stack gap={2} style={{ flex: 1, minWidth: 0 }}>
        <Group justify="space-between" gap="xs" wrap="nowrap">
          <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
            <Text
              fw={600}
              truncate
              c={school.enabled === false ? 'dimmed' : undefined}
              style={{ fontSize: 'clamp(0.95rem, 1.4vw, 1.25rem)' }}
            >
              {school.name}
            </Text>
            {school.enabled === false && (
              <Badge size="xs" variant="outline" color="gray" style={{ flexShrink: 0 }}>
                未启用
              </Badge>
            )}
          </Group>
          <Group gap={6} wrap="nowrap">
            <Text fw="bold" style={{ fontSize: 'clamp(1.05rem, 1.8vw, 1.5rem)', fontVariantNumeric: 'tabular-nums' }}>
              {count}
            </Text>
            <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
              {shareRate.toFixed(1)}%
            </Text>
          </Group>
        </Group>

        <Progress value={barRate} size="md" radius="xl" color={color} />
      </Stack>
    </Group>
  )
}

export default Screen
