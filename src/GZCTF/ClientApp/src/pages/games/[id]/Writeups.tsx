import {
  Badge,
  Button,
  FileButton,
  Group,
  Paper,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiFileUploadOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { useIsMobile } from '@Utils/ThemeOverride'
import { showErrorMsg } from '@Utils/Shared'
import api from '@Api'

/// 挑战题 WP：队伍上传已解出题目的解题思路，管理员审核后加分
const Writeups: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()
  const isMobile = useIsMobile(1080)

  const { data, isLoading, mutate } = api.challengeWriteup.useChallengeWriteupList(numId)
  const [uploading, setUploading] = useState<number | null>(null)

  const onUpload = async (challengeId: number, file: File | null) => {
    if (!file) return

    if (!file.name.toLowerCase().endsWith('.pdf')) {
      showNotification({
        color: 'red',
        message: '仅支持上传 PDF 文件',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      return
    }

    setUploading(challengeId)
    try {
      await api.challengeWriteup.challengeWriteupUpload(numId, challengeId, { file })
      await mutate()
      showNotification({
        color: 'teal',
        message: '解题思路已提交，等待审核加分',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setUploading(null)
    }
  }

  const content = (
    <Stack pb="2rem" gap="md">
      <Text size="sm" c="dimmed">
        挑战题初始分较低，提交解题思路（PDF）并通过审核后将获得额外加分。
        仅可为本队已解出的题目提交，重复提交会覆盖上一次的文件。
      </Text>

      <Paper p="md" withBorder>
        {isLoading ? (
          <Text c="dimmed" ta="center" py="xl">
            加载中…
          </Text>
        ) : (data?.items ?? []).length === 0 ? (
          <Text c="dimmed" ta="center" py="xl">
            暂无可提交解题思路的题目（需先解出题目）
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
              {(data?.items ?? []).map((item) => (
                <Table.Tr key={item.challengeId}>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <Text size="sm" fw={600}>
                        {item.title}
                      </Text>
                      {item.isChallengeBucket && (
                        <Badge size="xs" variant="light" color="orange">
                          挑战题
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {item.category}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    {item.submitted ? (
                      <Stack gap={0}>
                        <Badge size="sm" variant="light" color="teal">
                          已提交
                        </Badge>
                        <Text size="xs" c="dimmed">
                          {item.submitTimeUtc ? dayjs(item.submitTimeUtc).format('MM-DD HH:mm') : ''}
                        </Text>
                      </Stack>
                    ) : (
                      <Badge size="sm" variant="light" color="gray">
                        未提交
                      </Badge>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" fw={item.awardedScore ? 'bold' : undefined}>
                      {item.awardedScore ? `+${item.awardedScore}` : '—'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <FileButton onChange={(file) => onUpload(item.challengeId, file)} accept="application/pdf">
                      {(props) => (
                        <Button
                          {...props}
                          size="xs"
                          variant={item.submitted ? 'light' : 'filled'}
                          loading={uploading === item.challengeId}
                          leftSection={<Icon path={mdiFileUploadOutline} size={0.8} />}
                        >
                          {item.submitted ? '重新提交' : '上传解题思路'}
                        </Button>
                      )}
                    </FileButton>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Paper>
    </Stack>
  )

  if (isMobile)
    return (
      <WithNavBar width="90%" minWidth={0}>
        <Stack pt="md">
          <Title order={4}>挑战题 WP</Title>
          {content}
        </Stack>
      </WithNavBar>
    )

  return <WithGameTab>{content}</WithGameTab>
}

export default Writeups
