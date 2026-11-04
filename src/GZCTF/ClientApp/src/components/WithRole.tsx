import { Center, Loader } from '@mantine/core'
import React, { FC, useEffect } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useUser, useUserRole } from '@Hooks/useUser'
import { Role } from '@Api'

interface WithRoleProps {
  requiredRole: Role
  children?: React.ReactNode
}

export const RoleMap = new Map<Role, number>([
  [Role.Admin, 3],
  [Role.Monitor, 1],
  [Role.User, 0],
  [Role.Banned, -1],
])

export const RequireRole = (requiredRole: Role, role?: Role | null) =>
  RoleMap.get(role ?? Role.User)! >= RoleMap.get(requiredRole)!

/// 可查看监考数据：监考角色及以上，或出题人 / 出题管理员 / 学校负责人
export const useMonitorAccess = () => {
  const { user } = useUser()
  const isAdmin = user?.role === Role.Admin
  return (
    isAdmin ||
    RequireRole(Role.Monitor, user?.role) ||
    user?.isChallengeAuthor === true ||
    user?.isChallengeManager === true ||
    user?.isSchoolAdmin === true
  )
}

/// 完整监考权限（系统管理员或监考角色），可看事件/提交等全部面板
export const useMonitorFullAccess = () => {
  const { user } = useUser()
  return user?.role === Role.Admin || RequireRole(Role.Monitor, user?.role)
}

export const WithRole: FC<WithRoleProps> = ({ requiredRole, children }) => {
  const { role, error } = useUserRole()
  const navigate = useNavigate()
  const location = useLocation()

  const required = RoleMap.get(requiredRole)!

  useEffect(() => {
    if (error && error.status === 401) {
      navigate(`/account/login?from=${location.pathname}`, { replace: true })
    }

    if (!role) return

    const current = RoleMap.get(role)!

    if (current < required) navigate('/404')
  }, [role, error, required, navigate])

  if (role && RoleMap.get(role)! < required /* show loader before redirect */) {
    return (
      <Center h="calc(100vh - 32px)">
        <Loader />
      </Center>
    )
  }

  return <>{children}</>
}
