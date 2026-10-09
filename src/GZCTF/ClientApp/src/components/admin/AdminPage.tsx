import React, { FC, useEffect } from 'react'
import { Center, Loader } from '@mantine/core'
import { useLocation, useNavigate } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { AdminTabProps, WithAdminTab } from '@Components/admin/WithAdminTab'
import { useAdminTabs, useUser } from '@Hooks/useUser'

/// 管理后台入口。
/// 系统管理员可访问全部页面；持有「出题人 / 出题管理员 / 学校负责人」
/// 等附加职能权限的用户，也能进入后台对应的页面（出题、审核、素材库）。
export const AdminPage: FC<AdminTabProps> = (props) => {
  const { user, error } = useUser()
  const { canAccessAdmin } = useAdminTabs()
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => {
    // 未登录：跳转登录页并带回跳地址
    if (error && error.status === 401) {
      navigate(`/account/login?from=${location.pathname}`, { replace: true })
      return
    }

    // 已登录但没有任何后台职能权限：视为无权限
    if (user && !canAccessAdmin) navigate('/404')
  }, [user, error, canAccessAdmin, navigate, location.pathname])

  if (!user || !canAccessAdmin) {
    return (
      <WithNavBar width="90%" minWidth={1080}>
        <Center h="calc(100vh - 32px)">
          <Loader />
        </Center>
      </WithNavBar>
    )
  }

  return (
    <WithNavBar width="90%" minWidth={1080}>
      <WithAdminTab {...props} />
    </WithNavBar>
  )
}
