import { Button, Group, GroupProps, LoadingOverlay, Stack, Tabs } from '@mantine/core'
import {
  mdiAccountGroupOutline,
  mdiBullhornOutline,
  mdiCalendarWeek,
  mdiFileDocumentCheckOutline,
  mdiFlagOutline,
  mdiKeyboardBackspace,
  mdiTagOutline,
  mdiTextBoxOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, Link, useNavigate, useParams } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'
import misc from '@Styles/Misc.module.css'

export interface GameEditTabProps extends React.PropsWithChildren {
  head?: React.ReactNode
  headProps?: GroupProps
  contentPos?: React.CSSProperties['justifyContent']
  isLoading?: boolean
  backUrl?: string
}

export const WithGameEditTab: FC<GameEditTabProps> = ({
  children,
  isLoading,
  contentPos,
  head,
  backUrl,
  ...others
}) => {
  const navigate = useNavigate()
  const location = useLocation()
  const { id } = useParams()
  const { t } = useTranslation()

  const { user } = useUser()
  // 系统管理员可配置赛事全部内容；出题人只负责出题，
  // 仅开放「题目」与「周次设置」，其余（赛事信息、公告、赛区、审核、Writeup）不展示。
  const isAdmin = user?.role === Role.Admin

  const allPages = [
    { icon: mdiTextBoxOutline, title: t('admin.tab.games.info'), path: 'info' },
    { icon: mdiBullhornOutline, title: t('admin.tab.games.notices'), path: 'notices' },
    { icon: mdiFlagOutline, title: t('admin.tab.games.challenges'), path: 'challenges' },
    { icon: mdiCalendarWeek, title: '周次设置', path: 'weeks' },
    { icon: mdiTagOutline, title: t('admin.tab.games.divisions'), path: 'divisions' },
    { icon: mdiAccountGroupOutline, title: t('admin.tab.games.review'), path: 'review' },
    { icon: mdiFileDocumentCheckOutline, title: t('admin.tab.games.writeups'), path: 'writeups' },
  ]

  // 出题人仅负责出题；学校负责人需要「队伍审核」来调整组别
  const isSchoolManager = user?.isSchoolAdmin === true
  const NON_ADMIN_PATHS = isSchoolManager ? ['challenges', 'review'] : ['challenges']
  const pages = isAdmin ? allPages : allPages.filter((p) => NON_ADMIN_PATHS.includes(p.path))

  const getTab = (path: string) => pages.find((page) => path.includes(page.path))

  const [activeTab, setActiveTab] = useState(getTab(location.pathname)?.path ?? pages[0]?.path ?? '')

  useEffect(() => {
    if (pages.length === 0) return

    const tab = getTab(location.pathname)

    // 当前路径不在可访问列表内（出题人直接访问赛事配置页）：跳回第一个可用页。
    // 必须用绝对路径，相对路径在多层路由下会解析成错误的地址。
    if (tab) {
      setActiveTab(tab.path ?? '')
    } else if (id) {
      navigate(`/admin/games/${id}/${pages[0].path}`, { replace: true })
    }
  }, [location.pathname, pages.length, id])

  return (
    <AdminPage
      {...others}
      head={
        <>
          <Button
            w="10rem"
            component={Link}
            classNames={{ inner: misc.justifyBetween }}
            leftSection={<Icon path={mdiKeyboardBackspace} size={1} />}
            to={backUrl ?? '/admin/games'}
          >
            {t('admin.button.back')}
          </Button>
          <Group wrap="nowrap" justify={contentPos ?? 'space-between'} w="calc(100% - 11rem)">
            {head}
          </Group>
        </>
      }
    >
      <Group wrap="nowrap" justify="space-between" align="flex-start" w="100%" pb="xl">
        <Tabs
          orientation="vertical"
          value={activeTab}
          onChange={(value) => value && navigate(`/admin/games/${id}/${value}`)}
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
        <Stack w="calc(100% - 11rem)" pos="relative">
          <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
          {children}
        </Stack>
      </Group>
    </AdminPage>
  )
}
