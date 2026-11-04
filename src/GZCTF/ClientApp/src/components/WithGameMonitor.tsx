import { Button, Center, Group, LoadingOverlay, Stack, Tabs, Text } from '@mantine/core'
import { mdiExclamationThick, mdiFlag, mdiLightningBolt, mdiPackageVariant, mdiTableArrowDown } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useParams } from 'react-router'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { useMonitorAccess, useMonitorFullAccess } from '@Components/WithRole'
import { downloadBlob } from '@Utils/ApiHelper'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import api from '@Api'
import misc from '@Styles/Misc.module.css'

interface WithGameMonitorProps extends React.PropsWithChildren {
  isLoading?: boolean
}

export const WithGameMonitor: FC<WithGameMonitorProps> = ({ children, isLoading }) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')

  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useTranslation()
  const canMonitor = useMonitorAccess()
  const fullMonitor = useMonitorFullAccess()

  // 出题人 / 学校负责人只开放「作弊」面板，其余面板接口仍需监考权限
  const pages = fullMonitor
    ? [
        { icon: mdiLightningBolt, title: t('game.tab.monitor.events'), path: 'events' },
        { icon: mdiFlag, title: t('game.tab.monitor.submissions'), path: 'submissions' },
        { icon: mdiExclamationThick, title: t('game.tab.monitor.cheatinfo'), path: 'cheatinfo' },
        { icon: mdiPackageVariant, title: t('game.tab.monitor.traffic'), path: 'traffic' },
      ]
    : [{ icon: mdiExclamationThick, title: t('game.tab.monitor.cheatinfo'), path: 'cheatinfo' }]

  const getTab = (path: string) => pages.find((page) => path.endsWith(page.path))

  const [activeTab, setActiveTab] = useState(getTab(location.pathname)?.path ?? pages[0].path)
  const [disabled, setDisabled] = useState(false)

  useEffect(() => {
    if (!canMonitor) navigate('/404', { replace: true })
  }, [canMonitor, navigate])

  useEffect(() => {
    const tab = getTab(location.pathname)
    if (tab) {
      setActiveTab(tab.path ?? '')
    } else {
      navigate(pages[0].path)
    }
  }, [location])

  const onDownloadScoreboardSheet = () =>
    downloadBlob(
      api.game.gameScoreboardSheet(numId, { format: 'blob' }),
      setDisabled,
      t,
      `Scoreboard_${numId}_${Date.now()}.xlsx`
    )

  return (
    <WithNavBar width="90%">
      {canMonitor ? (
        <WithGameTab>
          <Group justify="space-between" align="flex-start">
            <Stack>
              <Button
                disabled={disabled}
                style={{ display: fullMonitor ? undefined : 'none' }}
                w="10rem"
                classNames={{ inner: misc.justifyBetween }}
                leftSection={<Icon path={mdiTableArrowDown} size={1} />}
                onClick={onDownloadScoreboardSheet}
              >
                {t('game.button.download.scoreboard')}
              </Button>
              <Tabs
                orientation="vertical"
                value={activeTab}
                onChange={(value) => value && navigate(`/games/${id}/monitor/${value}`)}
                classNames={{
                  root: misc.w10rem,
                  list: misc.w10rem,
                }}
              >
                <Tabs.List>
                  {pages.map((page) => (
                    <Tabs.Tab key={page.path} leftSection={<Icon path={page.icon} size={1} />} value={page.path}>
                      {page.title}
                    </Tabs.Tab>
                  ))}
                </Tabs.List>
              </Tabs>
            </Stack>
            <Stack w="calc(100% - 11rem)" pos="relative">
              <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
              {children}
            </Stack>
          </Group>
        </WithGameTab>
      ) : (
        <Center h="60vh">
          <Text c="dimmed">无权访问监考数据</Text>
        </Center>
      )}
    </WithNavBar>
  )
}
