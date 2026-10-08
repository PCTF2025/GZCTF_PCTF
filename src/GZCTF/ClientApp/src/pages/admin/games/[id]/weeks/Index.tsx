import { Button, NumberInput, Paper, SimpleGrid, Stack, Switch, Text, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiCheck, mdiContentSaveOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { useAdminGame } from '@Hooks/useGame'
import api, { GameInfoModel } from '@Api'
import { getInputNumber, showErrorMsg } from '@Utils/Shared'

const WEEKS = [1, 2, 3, 4, 5] as const

const WeekSettings: FC = () => {
  const { id } = useParams()
  const gameId = Number(id)
  const { game: source, mutate } = useAdminGame(gameId)
  const { t } = useTranslation()
  const [game, setGame] = useState<GameInfoModel>()
  const [saving, setSaving] = useState(false)

  useEffect(() => setGame(source), [source])

  const updateDuration = (week: (typeof WEEKS)[number], value: string | number) => {
    const duration = getInputNumber(value)
    if (Number.isNaN(duration)) return
    setGame((current) => current && { ...current, [`week${week}DurationDays`]: duration })
  }

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
              onChange={(event) =>
                setGame((current) => current && { ...current, weekModeEnabled: event.currentTarget.checked })
              }
              label={SwitchLabel(
                '启用周次模式',
                '启用后题目编辑、题目列表和比赛答题页会显示周次；关闭后周次信息不会对参赛者显示。'
              )}
            />
            <Text size="sm" c="dimmed">
              每周从比赛开始时间起依次计算。扩展题不受周次时长限制，始终作为独立分类保留。
            </Text>
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 5 }}>
              {WEEKS.map((week) => (
                <NumberInput
                  key={week}
                  label={`第 ${week} 周持续时间`}
                  description="单位：天"
                  min={1}
                  max={365}
                  value={game?.[`week${week}DurationDays`] ?? 7}
                  onChange={(value) => updateDuration(week, value)}
                  disabled={!game?.weekModeEnabled}
                />
              ))}
            </SimpleGrid>
          </Stack>
        </Paper>
      </Stack>
    </WithGameEditTab>
  )
}

export default WeekSettings
