import {
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Group,
  Paper,
  Select,
  SimpleGrid,
  Stack,
  Text,
  ThemeIcon,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiCheck, mdiContentSaveOutline, mdiFlagCheckered, mdiOpenInNew, mdiShieldCrownOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useSyncOnChange } from '@Hooks/useSyncOnChange'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api, { ConfigEditModel } from '@Api'
import { showErrorMsg } from '@Utils/Shared'

interface TrackRow {
  key: 'official' | 'public'
  title: string
  description: string
  color: string
  icon: string
}

const TRACKS: TrackRow[] = [
  {
    key: 'official',
    title: '主办赛道',
    description: '由赛事主办方统一命题，面向受邀队伍开放。',
    color: 'teal',
    icon: mdiShieldCrownOutline,
  },
  {
    key: 'public',
    title: '公开赛道',
    description: '面向所有注册选手开放，可自由报名参与。',
    color: 'blue',
    icon: mdiFlagCheckered,
  },
]

const TrackSettings: FC = () => {
  usePageTitle('赛道设置')
  const { t } = useTranslation()

  const { data: configs, mutate } = api.admin.useAdminGetConfigs(OnceSWRConfig)
  // 比赛列表：count 上限为 50，故一次最多取 50 场
  const { data: gamesData } = api.game.useGameGames({ count: 50, skip: 0 })

  const [officialGameId, setOfficialGameId] = useState<string | null>(null)
  const [publicGameId, setPublicGameId] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const syncFromConfigs = () => {
    const tracks = configs?.trackConfig
    if (!tracks) return
    // 0 / 空 均视为未绑定
    setOfficialGameId(tracks.officialGameId ? String(tracks.officialGameId) : null)
    setPublicGameId(tracks.publicGameId ? String(tracks.publicGameId) : null)
  }

  useSyncOnChange([configs], syncFromConfigs)
  useEffect(syncFromConfigs, [configs])

  const gameOptions = (gamesData?.data ?? []).map((g) => ({
    value: g.id!.toString(),
    label: `${g.title}（#${g.id}）`,
  }))

  const pick = (key: TrackRow['key']) => (key === 'official' ? officialGameId : publicGameId)
  const setPick = (key: TrackRow['key'], value: string | null) => {
    if (key === 'official') setOfficialGameId(value)
    else setPublicGameId(value)
  }

  const save = async () => {
    setSaving(true)
    try {
      // 用 0 表示未绑定：配置机制会跳过 null 值，传 null 无法取消绑定
      const toId = (v: string | null) => (v ? Number(v) : 0)
      const conf: ConfigEditModel = {
        ...configs,
        trackConfig: {
          officialGameId: toId(officialGameId),
          publicGameId: toId(publicGameId),
        },
      }
      await api.admin.adminUpdateConfigs(conf)
      await mutate()
      notifications.show({
        color: 'teal',
        message: '赛道设置已保存',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setSaving(false)
    }
  }

  return (
    <AdminPage
      isLoading={!configs}
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
        <Title order={2}>赛道设置</Title>

        <Paper withBorder p="md">
          <Stack gap="xs">
            <Text size="sm" c="dimmed">
              为首页的赛道入口绑定具体赛事。绑定后选手点击卡片将<strong>直达该赛事详情页</strong>
              ；未绑定时保持原有的按赛道筛选列表页。
            </Text>
          </Stack>
        </Paper>

        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
          {TRACKS.map((track) => {
            const value = pick(track.key)
            return (
              <Card key={track.key} withBorder radius="md" padding="lg">
                <Stack gap="md">
                  <Group justify="space-between" wrap="nowrap">
                    <Group gap="sm">
                      <ThemeIcon size={40} radius="md" variant="light" color={track.color}>
                        <Icon path={track.icon} size={1.2} />
                      </ThemeIcon>
                      <Box>
                        <Title order={4}>{track.title}</Title>
                        <Text size="xs" c="dimmed">
                          {track.description}
                        </Text>
                      </Box>
                    </Group>
                    {value ? (
                      <Badge color={track.color} variant="light">
                        已绑定
                      </Badge>
                    ) : (
                      <Badge color="gray" variant="light">
                        未绑定
                      </Badge>
                    )}
                  </Group>

                  <Select
                    label="绑定赛事"
                    description="留空表示不绑定，点击卡片进入赛事列表"
                    placeholder="未绑定"
                    searchable
                    clearable
                    data={gameOptions}
                    value={value}
                    nothingFoundMessage="没有匹配的赛事"
                    onChange={(v) => setPick(track.key, v)}
                  />

                  {value && (
                    <Anchor href={`/games/${value}`} target="_blank" rel="noopener noreferrer" size="sm">
                      <Group gap={6} wrap="nowrap" align="center">
                        <Icon path={mdiOpenInNew} size={0.7} />
                        <Text size="sm">预览赛事页 /games/{value}</Text>
                      </Group>
                    </Anchor>
                  )}
                </Stack>
              </Card>
            )
          })}
        </SimpleGrid>
      </Stack>
    </AdminPage>
  )
}

export default TrackSettings
