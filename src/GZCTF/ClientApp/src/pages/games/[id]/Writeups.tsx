import {
  Badge,
  Button,
  FileButton,
  Group,
  Paper,
  SegmentedControl,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiFileUploadOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { useIsMobile } from '@Utils/ThemeOverride'
import { showErrorMsg } from '@Utils/Shared'
import api from '@Api'

/// 解题思路（WP）：按周提交，同一个按钮可反复使用；列表显示每一周的提交情况
const Writeups: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()
  const isMobile = useIsMobile(1080)

  const { data, isLoading, mutate } = api.challengeWriteup.useChallengeWriteupList(numId)
  const [uploading, setUploading] = useState(false)
  const weeks = useMemo(
    () => (data?.weeks && data.weeks.length > 0 ? data.weeks : [1, 2, 3, 4, 5]),
    [data?.weeks]
  )
  const [week, setWeek] = useState<number>(weeks[0] ?? 1)

  useEffect(() => {
    if (!weeks.includes(week)) setWeek(weeks[0] ?? 1)
  }, [weeks, week])

  const onUpload = async (file: File | null) => {
    if (!file) return

    if (!file.name.toLowerCase().endsWith('.pdf')) {
      showNotification({
        color: 'red',
        message: '仅支持上传 PDF 文件',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      return
    }

    setUploading(true)
    try {
      await api.challengeWriteup.challengeWriteupUploadWeek(numId, week, { file })
      await mutate()
      showNotification({
        color: 'teal',
        message: `第 ${week} 周解题思路已提交，等待审核加分`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setUploading(false)
    }
  }

  const rows = (data?.items ?? []).filter((item) => !item.legacy)
  const legacyRows = (data?.items ?? []).filter((item) => item.legacy)
  const current = rows.find((item) => item.week === week)

  const content = (
    <Stack pb="2rem" gap="md">
      <Text size="sm" c="dimmed">
        解题思路（WP）按周提交：选择周次后上传 PDF，同一个按钮可以反复使用，
        同一周再次提交会覆盖上一次的文件，不同周互不影响。审核通过后将获得额外加分。
      </Text>

      <Paper p="md" withBorder>
        <Stack gap="sm">
          <Group gap="sm" wrap="wrap">
            <Text size="sm" fw={600}>
              选择周次
            </Text>
            <SegmentedControl
              size="xs"
              value={week.toString()}
              onChange={(v) => setWeek(parseInt(v))}
              data={weeks.map((w) => ({ value: w.toString(), label: `第 ${w} 周` }))}
            />
            <FileButton onChange={onUpload} accept="application/pdf">
              {(props) => (
                <Button
                  {...props}
                  size="xs"
                  variant={current?.submitted ? 'light' : 'filled'}
                  loading={uploading}
                  leftSection={<Icon path={mdiFileUploadOutline} size={0.8} />}
                >
                  {current?.submitted ? `重新提交第 ${week} 周` : `上传第 ${week} 周解题思路`}
                </Button>
              )}
            </FileButton>
          </Group>

          {isLoading ? (
            <Text c="dimmed" ta="center" py="xl">
              加载中…
            </Text>
          ) : (
            <Table highlightOnHover verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w="8rem">周次</Table.Th>
                  <Table.Th w="9rem">提交状态</Table.Th>
                  <Table.Th w="10rem">提交时间</Table.Th>
                  <Table.Th w="7rem">已加分</Table.Th>
                  <Table.Th>审核备注</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {rows.map((item) => (
                  <Table.Tr key={`w-${item.week}`}>
                    <Table.Td>
                      <Badge size="sm" variant="light" color={item.submitted ? 'teal' : 'gray'}>
                        第 {item.week} 周
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      {item.submitted ? (
                        <Badge size="sm" variant="light" color="teal">
                          已提交
                        </Badge>
                      ) : (
                        <Text size="sm" c="dimmed">
                          未提交
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">
                        {item.submitTimeUtc ? dayjs(item.submitTimeUtc).format('MM-DD HH:mm') : '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" fw={item.awardedScore ? 'bold' : undefined}>
                        {item.awardedScore ? `+${item.awardedScore}` : '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed" lineClamp={2}>
                        {item.reviewNote || '—'}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {legacyRows.map((item) => (
                  <Table.Tr key={`l-${item.id}`}>
                    <Table.Td>
                      <Badge size="sm" variant="light" color="gray">
                        第 {item.week} 周 · 历史
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{item.title || '按题提交'}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">
                        {item.submitTimeUtc ? dayjs(item.submitTimeUtc).format('MM-DD HH:mm') : '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" fw={item.awardedScore ? 'bold' : undefined}>
                        {item.awardedScore ? `+${item.awardedScore}` : '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed" lineClamp={2}>
                        {item.reviewNote || '—'}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Stack>
      </Paper>
    </Stack>
  )

  if (isMobile)
    return (
      <WithNavBar width="90%" minWidth={0}>
        <Stack pt="md">
          <Title order={4}>解题思路 WP</Title>
          {content}
        </Stack>
      </WithNavBar>
    )

  return <WithGameTab>{content}</WithGameTab>
}

export default Writeups
