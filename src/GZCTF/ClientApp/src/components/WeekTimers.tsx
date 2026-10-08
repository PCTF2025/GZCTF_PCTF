import { Box, Group, Paper, Stack, Text } from '@mantine/core'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { WeekBucketInfo } from '@Api'

interface WeekTimersProps {
  /// 后台配置的分桶信息（含各周时间窗口）
  buckets: WeekBucketInfo[]
  /// 比赛总结束时间
  endTimeUtc?: string | null
}

/// 把毫秒差格式化为「2天3小时5分」这类紧凑文案
const formatRemain = (ms: number) => {
  if (ms <= 0) return '已结束'

  const totalSeconds = Math.floor(ms / 1000)
  const days = Math.floor(totalSeconds / 86400)
  const hours = Math.floor((totalSeconds % 86400) / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60

  if (days > 0) return `${days}天${hours}小时${minutes}分`
  if (hours > 0) return `${hours}小时${minutes}分${seconds}秒`
  return `${minutes}分${seconds}秒`
}

/// 题目列表顶部的双计时：当前周剩余时间 + 总比赛剩余时间
export const WeekTimers: FC<WeekTimersProps> = ({ buckets, endTimeUtc }) => {
  const [now, setNow] = useState(() => dayjs())

  // 每秒刷新，保证倒计时连续
  useEffect(() => {
    const timer = setInterval(() => setNow(dayjs()), 1000)
    return () => clearInterval(timer)
  }, [])

  const current = buckets.find((b) => b.key != null && b.key <= 5 && b.isOpen)

  let weekText = '不在任何周次'
  if (current) {
    if (current.endUtc) {
      const remain = dayjs(current.endUtc).diff(now)
      weekText = remain > 0 ? formatRemain(remain) : '即将切换'
    } else {
      weekText = '不限时'
    }
  } else if (buckets.some((b) => b.key != null && b.key <= 5 && b.startUtc)) {
    // 有周次配置但当前不在窗口内：提示距下一个周次开始
    const upcoming = buckets
      .filter((b) => b.key != null && b.key <= 5 && b.startUtc && dayjs(b.startUtc).isAfter(now))
      .sort((a, b) => dayjs(a.startUtc!).valueOf() - dayjs(b.startUtc!).valueOf())[0]

    weekText = upcoming ? `${formatRemain(dayjs(upcoming.startUtc!).diff(now))}后开始` : '已全部结束'
  }

  const totalText = endTimeUtc ? formatRemain(dayjs(endTimeUtc).diff(now)) : '—'

  return (
    <Paper withBorder p="xs" radius="md">
      <Stack gap={4}>
        <Box>
          <Text size="xs" c="dimmed">
            当前周剩余
          </Text>
          <Group gap={4} align="baseline" wrap="nowrap">
            <Text size="xs" fw="bold" c={current ? 'teal' : 'dimmed'}>
              {current?.name ?? '—'}
            </Text>
          </Group>
          <Text size="sm" fw="bold">
            {weekText}
          </Text>
        </Box>
        <Box>
          <Text size="xs" c="dimmed">
            总剩余
          </Text>
          <Text size="sm" fw="bold">
            {totalText}
          </Text>
        </Box>
      </Stack>
    </Paper>
  )
}
