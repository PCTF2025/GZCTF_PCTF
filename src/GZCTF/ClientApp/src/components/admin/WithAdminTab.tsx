import { Group, GroupProps, LoadingOverlay, Stack } from '@mantine/core'
import {
  mdiAccountCogOutline,
  mdiAccountGroupOutline,
  mdiAccountSearchOutline,
  mdiImageMultipleOutline,
  mdiFileDocumentOutline,
  mdiFlagOutline,
  mdiPackageVariantClosed,
  mdiSitemapOutline,
  mdiTrophyVariantOutline,
  mdiShieldAccountOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { IconTabs } from '@Components/IconTabs'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useAdminTabs } from '@Hooks/useUser'

export interface AdminTabProps extends React.PropsWithChildren {
  head?: React.ReactNode
  isLoading?: boolean
  headProps?: GroupProps
}

export const WithAdminTab: FC<AdminTabProps> = ({ head, headProps, isLoading, children }) => {
  const navigate = useNavigate()
  const location = useLocation()

  const { t } = useTranslation()

  // 按当前用户的职能权限过滤标签页：
  // 出题人/出题管理员看到「比赛」，学校负责人看到「学籍审核」，能上传的用户看到「素材库」
  const { keys: allowedKeys } = useAdminTabs()

  const allPages = [
    { icon: mdiFlagOutline, title: t('admin.tab.games.index'), path: 'games' },
    { icon: mdiAccountGroupOutline, title: t('admin.tab.teams'), path: 'teams' },
    { icon: mdiAccountCogOutline, title: t('admin.tab.users'), path: 'users' },
    {
      icon: mdiPackageVariantClosed,
      title: t('admin.tab.instances'),
      path: 'instances',
    },
    { icon: mdiFileDocumentOutline, title: t('admin.tab.logs'), path: 'logs' },
    { icon: mdiSitemapOutline, title: t('admin.tab.settings'), path: 'settings' },
    { icon: mdiShieldAccountOutline, title: '登录设置', path: 'login' },
    { icon: mdiTrophyVariantOutline, title: '赛道设置', path: 'tracks' },
    { icon: mdiAccountSearchOutline, title: '学籍审核', path: 'verify' },
    { icon: mdiImageMultipleOutline, title: '素材库', path: 'assets' },
  ]

  const pages = allPages.filter((p) => allowedKeys.includes(p.path))
  const getTab = (path: string) => pages.findIndex((page) => path.startsWith(`/admin/${page.path}`))
  const tabIndex = getTab(location.pathname)
  const [activeTab, setActiveTab] = useState(tabIndex < 0 ? 0 : tabIndex)

  const onChange = (active: number, tabKey: string) => {
    setActiveTab(active)
    navigate(`/admin/${tabKey}`)
  }

  useEffect(() => {
    // 过滤后可能为空（理论上不会，AdminPage 已拦截）
    if (pages.length === 0) return

    const tab = getTab(location.pathname)
    if (tab >= 0) {
      setActiveTab(tab)
    } else {
      navigate(`/admin/${pages[0].path}`)
    }
  }, [location, pages.length])

  // 越界兜底：过滤后 tabIndex 可能超出范围
  usePageTitle(pages[tabIndex]?.title ?? pages[0]?.title ?? '')

  return (
    <Stack gap="xs" align="center" pt="md">
      <IconTabs
        withIcon
        active={activeTab}
        onTabChange={onChange}
        tabs={pages.map((p) => ({
          tabKey: p.path,
          label: p.title,
          icon: <Icon path={p.icon} size={1} />,
        }))}
      />
      {head && (
        <Group wrap="nowrap" justify="space-between" h="40px" w="100%" {...headProps}>
          {head}
        </Group>
      )}
      {children}
      <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
    </Stack>
  )
}
