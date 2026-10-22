import {
  ActionIcon,
  Badge,
  Group,
  Paper,
  Progress,
  ScrollArea,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Tooltip,
} from '@mantine/core'
import { mdiAccountCheckOutline, mdiAccountClockOutline, mdiAccountGroupOutline, mdiDomain, mdiFullscreen, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import * as echarts from 'echarts'
import type { EChartsOption } from 'echarts'
import { FC, useCallback, useMemo, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import chinaGeo from '@Resources/china-provinces.json'
import api, { SchoolStatItemModel } from '@Api'

/// 注册中国省级地图（只需一次）
echarts.registerMap('china-provinces', chinaGeo as never)

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

  // 按省份聚合报名人数（后端按校名推断省份）
  const provinces = useMemo(() => {
    const map = new Map<string, number>()
    schools.forEach((s) => {
      const key = s.province || '其他'
      map.set(key, (map.get(key) ?? 0) + (s.userCount ?? 0))
    })
    return [...map.entries()]
      .map(([name, count]) => ({ name, count }))
      .sort((a, b) => b.count - a.count)
  }, [schools])

  const provinceMax = useMemo(
    () => provinces.reduce((max, p) => (p.name === '其他' ? max : Math.max(max, p.count)), 0),
    [provinces]
  )

  /// 地图数值标签阈值：约前 8 名
  const labelThreshold = useMemo(() => provinces[7]?.count ?? 0, [provinces])

  /// 省份地图：按 GeoJSON 全名匹配（短名 → 「江苏省」/「北京市」…）
  const provinceMapOption = useMemo<EChartsOption>(() => {
    const fullName = (short: string) => {
      const hit = (chinaGeo.features as { properties: { name: string } }[]).find((f) =>
        f.properties.name.startsWith(short)
      )
      return hit?.properties.name ?? short
    }

    return {
      tooltip: { trigger: 'item', formatter: '{b}<br/>报名 {c} 人' },
      visualMap: {
        min: 0,
        max: provinceMax || 1,
        left: 10,
        bottom: 10,
        text: ['多', '少'],
        calculable: true,
        inRange: { color: ['#2b3a55', '#3b6ea5', '#4d9de0', '#7fd1ff'] },
        textStyle: { color: '#adb5bd' },
      },
      series: [
        {
          type: 'map',
          map: 'china-provinces',
          roam: false,
          // 铺满面板，避免四周大片留白
          layoutCenter: ['50%', '52%'],
          layoutSize: '125%',
          label: { show: false },
          itemStyle: { borderColor: '#495057', borderWidth: 0.6 },
          emphasis: { label: { show: true, color: '#fff' }, itemStyle: { areaColor: '#f08c00' } },
          data: provinces
            .filter((p) => p.name !== '其他')
            .map((p) => ({
              name: fullName(p.name),
              value: p.count,
              // 只给报名人数靠前的省份标注数值，避免东部小省挤在一起
              label: {
                show: p.count >= labelThreshold,
                formatter: '{c}',
                fontSize: 11,
                fontWeight: 'bold',
                color: '#f8f9fa',
                textBorderColor: '#1a1b1e',
                textBorderWidth: 2,
              },
            })),
        },
      ],
    }
  }, [provinces, provinceMax, labelThreshold])

  /// 省份饼图：前 8 名 + 其他合并
  const provincePieOption = useMemo<EChartsOption>(() => {
    const top = provinces.slice(0, 8)
    const rest = provinces.slice(8).reduce((sum, p) => sum + p.count, 0)
    const data = [...top.map((p) => ({ name: p.name, value: p.count }))]
    if (rest > 0) data.push({ name: '其他省份', value: rest })

    return {
      tooltip: { trigger: 'item', formatter: '{b}<br/>{c} 人（{d}%）' },
      legend: { type: 'scroll', orient: 'vertical', right: 4, top: 'center', textStyle: { fontSize: 11 } },
      series: [
        {
          type: 'pie',
          radius: ['45%', '72%'],
          center: ['38%', '50%'],
          avoidLabelOverlap: true,
          itemStyle: { borderColor: '#1a1b1e', borderWidth: 2 },
          label: { show: false },
          emphasis: { label: { show: true, fontSize: 14, fontWeight: 'bold', formatter: '{b}\n{c} 人' } },
          data,
        },
      ],
    }
  }, [provinces])

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
      <Stack gap="md" w="100%">
        {/* 总量概览 */}
        <SimpleGrid cols={{ base: 2, md: 4 }} spacing="md">
          <SummaryCard icon={mdiAccountGroupOutline} label="报名总人数" value={total} color="blue" />
          <SummaryCard icon={mdiAccountCheckOutline} label="学籍已通过" value={approved} color="teal" />
          <SummaryCard icon={mdiAccountClockOutline} label="待审核" value={pending} color="orange" />
          <SummaryCard icon={mdiDomain} label="覆盖学校/组" value={schools.length} color="grape" />
        </SimpleGrid>

        {/* 左右分栏：左侧地图 + 省份明细/占比，右侧学校排名 */}
        <SimpleGrid cols={{ base: 1, lg: 12 }} spacing="md" verticalSpacing="md">
          {/* 左侧 */}
          <Stack gap="md" style={{ gridColumn: 'span 8' }}>
            <Paper p="md" withBorder>
              <Group justify="space-between" mb="xs">
                <Text fw="bold">各省报名分布</Text>
                <Text size="xs" c="dimmed">
                  按学校所在省份汇总
                </Text>
              </Group>
              <EchartsContainer option={provinceMapOption} style={{ height: 'clamp(280px, 36vh, 500px)' }} />
            </Paper>

            <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
              <Paper p="md" withBorder>
                <Group justify="space-between" mb="xs">
                  <Text fw="bold">省份明细</Text>
                  <Text size="xs" c="dimmed">
                    共 {provinces.length} 个
                  </Text>
                </Group>
                <ScrollArea h="clamp(190px, 21vh, 290px)" type="auto" offsetScrollbars>
                  <Table highlightOnHover striped withRowBorders={false} verticalSpacing={4}>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>省份</Table.Th>
                        <Table.Th ta="right">人数</Table.Th>
                        <Table.Th ta="right" w="4.5rem">
                          占比
                        </Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {provinces.map((p) => (
                        <Table.Tr key={p.name}>
                          <Table.Td>
                            <Text size="sm" fw={500}>
                              {p.name}
                            </Text>
                          </Table.Td>
                          <Table.Td ta="right">
                            <Text size="sm" fw="bold" style={{ fontVariantNumeric: 'tabular-nums' }}>
                              {p.count}
                            </Text>
                          </Table.Td>
                          <Table.Td ta="right">
                            <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
                              {total > 0 ? ((p.count / total) * 100).toFixed(1) : '0.0'}%
                            </Text>
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              </Paper>

              <Paper p="md" withBorder>
                <Group justify="space-between" mb="xs">
                  <Text fw="bold">省份占比</Text>
                  <Text size="xs" c="dimmed">
                    前 8 名 + 其他
                  </Text>
                </Group>
                <EchartsContainer option={provincePieOption} style={{ height: 'clamp(190px, 21vh, 290px)' }} />
              </Paper>
            </SimpleGrid>
          </Stack>

          {/* 右侧：学校排名 */}
          <Paper p="md" withBorder style={{ gridColumn: 'span 4' }}>
            <Group justify="space-between" mb="xs">
              <Text fw="bold">各校报名人数</Text>
              <Text size="xs" c="dimmed">
                {schools.length} 个
              </Text>
            </Group>
            <ScrollArea h="calc(100vh - 330px)" type="auto" offsetScrollbars>
              <Stack gap="sm">
                {schools.map((school, index) => (
                  <SchoolRow key={school.slug} school={school} rank={index + 1} maxCount={maxCount} total={total} />
                ))}
                {schools.length === 0 && (
                  <Text c="dimmed" ta="center" py="xl">
                    暂无报名数据
                  </Text>
                )}
              </Stack>
            </ScrollArea>
          </Paper>
        </SimpleGrid>
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
