import { Box, Button, Card, Divider, Group, Paper, SimpleGrid, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { DateTimePicker } from '@mantine/dates'
import { notifications } from '@mantine/notifications'
import { mdiCheck, mdiContentSaveOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { useAdminGame } from '@Hooks/useGame'
import api, { GameInfoModel } from '@Api'
import { showErrorMsg } from '@Utils/Shared'

const WEEKS = [1, 2, 3, 4, 5] as const

/// 题目归属分桶固定值：6 = 挑战题，7 = 其他题
const CHALLENGE_BUCKET = 6
const MISC_BUCKET = 7

type WeekField = `week${number}StartUtc` | `week${number}EndUtc` | `week${number}Name`

const WeekSettings: FC = () => {
  const { id } = useParams()
  const gameId = Number(id)
  const { game: source, mutate } = useAdminGame(gameId)
  const { t } = useTranslation()
  const [game, setGame] = useState<GameInfoModel>()
  const [saving, setSaving] = useState(false)

  useEffect(() => setGame(source), [source])

  const patch = (values: Partial<GameInfoModel>) =>
    setGame((current) => (current ? { ...current, ...values } : current))

  /// 时间选择器用本地时间展示，存储仍是 UTC，避免时区错位
  const toLocal = (utc?: string | null) => (utc ? dayjs(utc).toDate() : null)
  const toUtc = (date: Date | null) => (date ? dayjs(date).toISOString() : null)

  const weekRows = useMemo(
    () =>
      WEEKS.map((week) => ({
        week,
        start: game?.[`week${week}StartUtc` as keyof GameInfoModel] as string | null | undefined,
        end: game?.[`week${week}EndUtc` as keyof GameInfoModel] as string | null | undefined,
        name: game?.[`week${week}Name` as keyof GameInfoModel] as string | null | undefined,
      })),
    [game]
  )

  const save = async () => {
    if (!game?.id) return
    setSaving(true)
    try {
      await api.edit.editUpdateGame(game.id, game)
      await mutate()
      notifications.show({
        color: 'teal',
        message: '周次设置已保存',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setSaving(false)
    }
  }

  return (
    <WithGameEditTab
      isLoading={!game}
      head={
        <Button
          leftSection={<Icon path={mdiContentSaveOutline} size={1} />}
          loading={saving}
          onClick={save}
        >
          {t('admin.button.save')}
        </Button>
      }
    >
      <Stack>
        <Title order={2}>周次设置</Title>

        <Paper withBorder p="md">
          <Stack>
            <Switch
              checked={game?.weekModeEnabled ?? false}
              onChange={(event) => patch({ weekModeEnabled: event.currentTarget.checked })}
              label={SwitchLabel(
                '启用周次模式',
                '启用后按周次组织题目：仅在该周时间窗口内可以提交 flag，窗口外题目置灰但可查看。'
              )}
            />
            <Text size="sm" c="dimmed">
              每周单独设置起止时间（留空表示该端不限制）。起止时间之外的题目会显示为灰色、可以点开查看，但无法提交
              flag；挑战题与其他题不受时间限制。
            </Text>
          </Stack>
        </Paper>

        <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
          {weekRows.map(({ week, start, end, name }) => {
            const configured = Boolean(start || end)
            return (
              <Card key={week} withBorder radius="md" padding="md">
                <Stack gap="sm">
                  <Group justify="space-between" wrap="nowrap">
                    <Title order={4}>{`第 ${week} 周`}</Title>
                    <Text size="xs" c={configured ? 'teal' : 'dimmed'}>
                      {configured ? '已配置时间' : '未配置时间（不限制提交）'}
                    </Text>
                  </Group>

                  <TextInput
                    label="周次名称"
                    description="留空显示「第 N 周」"
                    placeholder={`第 ${week} 周`}
                    maxLength={64}
                    disabled={!game?.weekModeEnabled}
                    value={name ?? ''}
                    onChange={(event) =>
                      patch({ [`week${week}Name`]: event.currentTarget.value } as Partial<GameInfoModel>)
                    }
                  />

                  <SimpleGrid cols={{ base: 1, sm: 2 }}>
                    <DateTimePicker
                      label="开始时间"
                      placeholder="留空不限制"
                      valueFormat="YYYY-MM-DD HH:mm"
                      clearable
                      disabled={!game?.weekModeEnabled}
                      value={toLocal(start)}
                      onChange={(value) =>
                        patch({
                          [`week${week}StartUtc`]: toUtc(value as Date | null),
                        } as Partial<GameInfoModel>)
                      }
                    />
                    <DateTimePicker
                      label="结束时间"
                      placeholder="留空不限制"
                      valueFormat="YYYY-MM-DD HH:mm"
                      clearable
                      disabled={!game?.weekModeEnabled}
                      value={toLocal(end)}
                      onChange={(value) =>
                        patch({
                          [`week${week}EndUtc`]: toUtc(value as Date | null),
                        } as Partial<GameInfoModel>)
                      }
                    />
                  </SimpleGrid>

                  {(start || end) && (
                    <Text size="xs" c="dimmed">
                      {start ? `开始 ${dayjs(start).format('YYYY-MM-DD HH:mm')}` : '开始不限'}
                      {' ～ '}
                      {end ? `结束 ${dayjs(end).format('YYYY-MM-DD HH:mm')}` : '结束不限'}
                    </Text>
                  )}
                </Stack>
              </Card>
            )
          })}
        </SimpleGrid>

        <Paper withBorder p="md">
          <Stack>
            <Title order={4}>非周次分组</Title>
            <Text size="sm" c="dimmed">
              这两个分组的题目不绑定具体周次，任何时候都可以提交。
            </Text>
            <Divider />
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <TextInput
                label="挑战题分组名称"
                description="留空显示「挑战题」"
                placeholder="挑战题"
                maxLength={64}
                disabled={!game?.weekModeEnabled}
                value={game?.challengeBucketName ?? ''}
                onChange={(event) => patch({ challengeBucketName: event.currentTarget.value })}
              />
              <TextInput
                label="其他题分组名称"
                description="留空显示「其他题」"
                placeholder="其他题"
                maxLength={64}
                disabled={!game?.weekModeEnabled}
                value={game?.miscBucketName ?? ''}
                onChange={(event) => patch({ miscBucketName: event.currentTarget.value })}
              />
            </SimpleGrid>
            <Box>
              <Text size="xs" c="dimmed">
                题目归属在「题目管理 → 编辑题目 → 所属周次」中设置，可选 1-5
                周或上述两个分组，未归类题目不受时间限制。
              </Text>
            </Box>
          </Stack>
        </Paper>
      </Stack>
    </WithGameEditTab>
  )
}

export default WeekSettings
