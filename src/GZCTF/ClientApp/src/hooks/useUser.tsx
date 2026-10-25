import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { useSWRConfig } from 'swr'
import api, { Role, UserPermission, hasPermission } from '@Api'

export const useUser = () => {
  const navigate = useNavigate()
  const { t } = useTranslation()

  const {
    data: user,
    error,
    mutate,
  } = api.account.useAccountProfile({
    refreshInterval: 0,
    shouldRetryOnError: false,
    revalidateOnFocus: false,
    onErrorRetry: async (err, _key, _config, revalidate, { retryCount }) => {
      if (err?.status === 403) {
        await api.account.accountLogOut()
        navigate('/')
        showNotification({
          color: 'red',
          message: t('account.notification.login.banned'),
          icon: <Icon path={mdiClose} size={1} />,
        })
        return
      }

      if (err?.status === 401 || retryCount >= 5) {
        mutate(undefined, false)
        return
      }

      setTimeout(() => revalidate({ retryCount: retryCount }), 10000)
    },
  })

  return { user, error, mutate }
}

export const useUserRole = () => {
  const { user, error } = useUser()
  return { role: user?.role, error }
}

export const useTeams = () => {
  const {
    data: teams,
    error,
    mutate,
  } = api.team.useTeamGetTeamsInfo({
    refreshInterval: 120000,
    shouldRetryOnError: false,
    revalidateOnFocus: false,
  })

  return { teams, error, mutate }
}

export const useLogOut = () => {
  const navigate = useNavigate()
  const { mutate } = useSWRConfig()
  const { mutate: mutateProfile } = useUser()
  const { t } = useTranslation()

  return async () => {
    try {
      await api.account.accountLogOut()
      navigate('/')
      mutate((key) => typeof key === 'string' && key.includes('game/'), undefined, {
        revalidate: false,
      })
      mutateProfile(undefined, { revalidate: false })
      showNotification({
        color: 'teal',
        message: t('account.notification.logout'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch {
      navigate('/')
      mutateProfile(undefined, { revalidate: false })
    }
  }
}

/// 当前用户可访问的后台标签页 key 集合。
/// 系统管理员拥有全部权限，其余用户按附加职能权限开放对应页面。
export const useAdminTabs = () => {
  const { user } = useUser()

  return useMemo(() => {
    if (!user) return { keys: [] as string[], canAccessAdmin: false }

    const isAdmin = user.role === Role.Admin
    const isAuthor = isAdmin || user.isChallengeAuthor === true || user.isChallengeManager === true
    const isSchoolManager = isAdmin || user.isSchoolAdmin === true
    const canUpload =
      isAdmin ||
      isAuthor ||
      isSchoolManager ||
      hasPermission(user.permissions, UserPermission.ChallengeAuthor) ||
      hasPermission(user.permissions, UserPermission.ChallengeManager)

    const keys: string[] = []
    if (isAuthor) keys.push('games')
    if (isSchoolManager) keys.push('verify', 'games')
    if (canUpload) keys.push('assets')
    // 报名大屏：出题人 / 出题管理员 / 学籍审核人 / 系统管理员均可查看
    if (isAuthor || isSchoolManager) keys.push('screen')
    // 系统管理员保留全部后台功能（screen 已在上面加入）
    if (isAdmin)
      keys.push('teams', 'users', 'instances', 'logs', 'settings', 'schools', 'tracks')

    return { keys, canAccessAdmin: keys.length > 0 }
  }, [user])
}
