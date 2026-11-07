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

/// WP 提交：第 1-4 周每周一份报告（覆盖该周所有题目）；挑战题解出一道交一份
const Writeups: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()
  const isMobile = useIsMobile(1080)

  const { data, isLoading, mutate } = api.challengeWriteup.useChallengeWriteupList(numId)
  const [uploading, setUploading] = useState<string | null>(null)
  const weeks = useMemo(
    () => (data?.weeks && data.weeks.length > 0 ? data.weeks : [1, 2, 3, 4]),
    [data?.weeks]
  )
  const [week, setWeek] = useState<number>(weeks[0] ?? 1)

  useEffect(() => {
    if (!weeks.includes(week)) setWeek(weeks[0] ?? 1)
  }, [weeks, week])

  const items = data?.items ?? []
  const weekItems = items.filter((i) => !i.isChallengeBucket && !i.legacy)
  const challengeItems = items.filter((i) => i.isChallengeBucket)
  const legacyItems = items.filter((i) => i.legacy)

  const checkFile = (file: File) => {
    const ok = /\.(pdf|doc|docx)$/i.test(file.name)
    if (!ok) {
      showNotification({
        color: 'red',
        message: '仅支持 PDF 或 Word（doc/docx）文件',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    }
    return ok
  }

  const onUploadWeek = async (file: File | null) => {
    if (!file || !checkFile(file)) return
    setUploading(`w-${week}`)
    try {
      await api.challengeWriteup.challengeWriteupUploadWeek(numId, week, { file })
      await mutate()
      showNotification({
        color: 'teal',
        message: `第 ${week} 周解题报告已提交，等待审核加分`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setUploading(null)
    }
  }

  const onUploadChallenge = async (challengeId: number, file: File | null) => {
    if (!file || !checkFile(file)) return
    setUploading(`c-${challengeId}`)
    try {
      await api.challengeWriteup.challengeWriteupUpload(numId, challengeId, { file })
      await mutate()
      showNotification({
        color: 'teal',
        message: '挑战题解题报告已提交，等待审核加分',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setUploading(null)
    }
  }

  const current = weekItems.find((i) => i.week === week)

  const statusCell = (submitted?: boolean, time?: string | null) =>
    submitted ? (
      <Stack gap={0}>
        <Badge size="sm" variant="light" color="teal">
          已提交
        </Badge>
        <Text size="xs" c="dimmed">
          {time ? dayjs(time).format('MM-DD HH:mm') : ''}
        </Text>
      </Stack>
    ) : (
      <Badge size="sm" variant="light" color="gray">
        未提交
      </Badge>
    )

  const awardCell = (score?: number) => (
    <Text size="sm" fw={score ? 'bold' : undefined}>
      {score ? `+${score}` : '—'}
    </Text>
  )

  const content = (
    <Stack pb="2rem" gap="md">
      <Text size="sm" c="dimmed">
        WP 提交分两部分：<b>第 1-4 周</b>每周提交一份解题报告（涵盖该周所有题目，可以反复替换）；
        <b>挑战题</b>解出一道交一份。文件支持 PDF 或 Word（doc/docx）。
      </Text>

      <Paper p="md" withBorder>
        <Stack gap="sm">
          <Text size="sm" fw={600}>
            第 1-4 周 · 每周一份
          </Text>
          <Group gap="sm" wrap="wrap">
            <SegmentedControl
              size="xs"
              value={week.toString()}
              onChange={(v) => setWeek(parseInt(v))}
              data={weeks.map((w) => ({ value: w.toString(), label: `第 ${w} 周` }))}
            />
            <FileButton onChange={onUploadWeek} accept=".pdf,.doc,.docx">
              {(props) => (
                <Button
                  {...props}
                  size="xs"
                  variant={current?.submitted ? 'light' : 'filled'}
                  loading={uploading === `w-${week}`}
                  leftSection={<Icon path={mdiFileUploadOutline} size={0.8} />}
                >
                  {current?.submitted ? `重新提交第 ${week} 周` : `上传第 ${week} 周报告`}
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
                  <Table.Th w="7rem">已加分</Table.Th>
                  <Table.Th>审核备注</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {weekItems.map((item) => (
                  <Table.Tr key={`w-${item.week}`}>
                    <Table.Td>
                      <Badge size="sm" variant="light" color={item.submitted ? 'teal' : 'gray'}>
                        第 {item.week} 周
                      </Badge>
                    </Table.Td>
                    <Table.Td>{statusCell(item.submitted, item.submitTimeUtc)}</Table.Td>
                    <Table.Td>{awardCell(item.awardedScore)}</Table.Td>
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

      <Paper p="md" withBorder>
        <Stack gap="sm">
          <Text size="sm" fw={600}>
            挑战题 · 解出一道交一份
          </Text>
          {challengeItems.length === 0 ? (
            <Text size="sm" c="dimmed" py="md">
              暂无已解出的挑战题
            </Text>
          ) : (
            <Table highlightOnHover verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>题目</Table.Th>
                  <Table.Th w="7rem">方向</Table.Th>
                  <Table.Th w="9rem">提交状态</Table.Th>
                  <Table.Th w="7rem">已加分</Table.Th>
                  <Table.Th w="12rem">操作</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {challengeItems.map((item) => (
                  <Table.Tr key={`c-${item.challengeId}`}>
                    <Table.Td>
                      <Text size="sm" fw={600} lineClamp={1}>
                        {item.title}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed">
                        {item.category}
                      </Text>
                    </Table.Td>
                    <Table.Td>{statusCell(item.submitted, item.submitTimeUtc)}</Table.Td>
                    <Table.Td>{awardCell(item.awardedScore)}</Table.Td>
                    <Table.Td>
                      <FileButton
                        onChange={(file) => onUploadChallenge(item.challengeId!, file)}
                        accept=".pdf,.doc,.docx"
                      >
                        {(props) => (
                          <Button
                            {...props}
                            size="xs"
                            variant={item.submitted ? 'light' : 'filled'}
                            loading={uploading === `c-${item.challengeId}`}
                            leftSection={<Icon path={mdiFileUploadOutline} size={0.8} />}
                          >
                            {item.submitted ? '重新提交' : '上传解题报告'}
                          </Button>
                        )}
                      </FileButton>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Stack>
      </Paper>

      {legacyItems.length > 0 && (
        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Text size="sm" fw={600}>
              历史提交
            </Text>
            <Table verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w="8rem">周次</Table.Th>
                  <Table.Th>题目</Table.Th>
                  <Table.Th w="9rem">提交时间</Table.Th>
                  <Table.Th w="7rem">已加分</Table.Th>
                  <Table.Th>审核备注</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {legacyItems.map((item) => (
                  <Table.Tr key={`l-${item.id}`}>
                    <Table.Td>
                      <Badge size="sm" variant="light" color="gray">
                        第 {item.week} 周
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
                    <Table.Td>{awardCell(item.awardedScore)}</Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed" lineClamp={2}>
                        {item.reviewNote || '—'}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Stack>
        </Paper>
      )}
    </Stack>
  )

  if (isMobile)
    return (
      <WithNavBar width="90%" minWidth={0}>
        <Stack pt="md">
          <Title order={4}>WP 提交</Title>
          {content}
        </Stack>
      </WithNavBar>
    )

  return <WithGameTab>{content}</WithGameTab>
}

export default Writeups
