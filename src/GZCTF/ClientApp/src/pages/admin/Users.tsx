import {
  ActionIcon,
  Avatar,
  Badge,
  Button,
  Code,
  Group,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { useClipboard, useInputState } from '@mantine/hooks'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiAccountOutline,
  mdiArrowLeftBold,
  mdiArrowRightBold,
  mdiCheck,
  mdiDeleteOutline,
  mdiLockReset,
  mdiMagnify,
  mdiPencilOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { ActionIconWithConfirm } from '@Components/ActionIconWithConfirm'
import { AdminPage } from '@Components/admin/AdminPage'
import { UserEditModal, RoleColorMap } from '@Components/admin/UserEditModal'
import { showErrorMsg } from '@Utils/Shared'
import { useArrayResponse } from '@Hooks/useArrayResponse'
import { useUser } from '@Hooks/useUser'
import api, { hasPermission, Role, UserInfoModel, UserPermission, VerifyStatus } from '@Api'
import tableClasses from '@Styles/Table.module.css'

const ITEM_COUNT_PER_PAGE = 30

/// 学籍审核状态筛选项
const VERIFY_STATUS_OPTIONS = [
  { value: VerifyStatus.Pending, label: '待审核' },
  { value: VerifyStatus.Approved, label: '已通过' },
  { value: VerifyStatus.Rejected, label: '已驳回' },
  { value: VerifyStatus.None, label: '未提交' },
]


/// 学籍审核状态 → 展示样式
const VERIFY_BADGE: Record<string, { label: string; color: string }> = {
  [VerifyStatus.Approved]: { label: '已通过', color: 'teal' },
  [VerifyStatus.Pending]: { label: '待审核', color: 'orange' },
  [VerifyStatus.Rejected]: { label: '已驳回', color: 'red' },
  [VerifyStatus.None]: { label: '未提交', color: 'gray' },
}

/// 学籍审核状态徽章
const VerifyBadge: FC<{ user: UserInfoModel }> = ({ user }) => {
  const meta = VERIFY_BADGE[user.verifyStatus ?? VerifyStatus.None] ?? VERIFY_BADGE[VerifyStatus.None]
  return (
    <Badge size="sm" variant="light" color={meta.color}>
      {meta.label}
    </Badge>
  )
}

/// 职能权限位 → 展示元信息
const DUTY_META: { bit: UserPermission; label: string; color: string }[] = [
  { bit: UserPermission.ChallengeAuthor, label: '出题人', color: 'grape' },
  { bit: UserPermission.ChallengeManager, label: '出题管理员', color: 'indigo' },
  { bit: UserPermission.SchoolManager, label: '学校负责人', color: 'cyan' },
]

/// 负责人类型（及授权方向）徽章组
const DutyBadges: FC<{ user: UserInfoModel; schoolNames?: Map<string, string> }> = ({
  user,
  schoolNames,
}) => {
  const bits = user.permissions ?? UserPermission.None
  const categories = (user.managedCategories ?? '')
    .split(',')
    .map((c) => c.trim())
    .filter(Boolean)
  const schools = (user.managedSchools ?? '')
    .split(',')
    .map((c) => c.trim())
    .filter(Boolean)
  /// 短名转全称，未收录的短名原样展示
  const schoolLabels = schools.map((slug) => schoolNames?.get(slug) ?? slug)

  const duties = DUTY_META.filter((m) => hasPermission(bits, m.bit))

  // 兼容历史数据：仅有负责学校但未打权限位的账号，仍视为学校负责人
  if (duties.length === 0 && schools.length > 0)
    duties.push({ bit: UserPermission.SchoolManager, label: '学校负责人', color: 'cyan' })

  // 平台管理员单独标注（Role 体系，与职能权限正交）
  const isAdmin = user.role === Role.Admin

  if (!isAdmin && duties.length === 0)
    return (
      <Text size="xs" c="dimmed">
        —
      </Text>
    )

  return (
    <Group gap={4} wrap="wrap">
      {isAdmin && (
        <Badge size="sm" variant="light" color={RoleColorMap.get(Role.Admin)}>
          平台管理员
        </Badge>
      )}
      {duties.map((d) => (
        <Tooltip
          key={d.label}
          withArrow
          label={
            d.bit === UserPermission.SchoolManager
              ? schools.length > 0
                ? `负责审核：${schoolLabels.join('、')}`
                : '负责审核学校'
              : categories.length > 0
                ? `授权方向：${categories.join('、')}`
                : '全部方向'
          }
        >
          <Badge size="sm" variant="light" color={d.color} style={{ cursor: 'help' }}>
            {d.label}
            {d.bit === UserPermission.SchoolManager
              ? schools.length > 0 && ` · ${schools.length} 校`
              : categories.length > 0 && ` · ${categories.length} 方向`}
          </Badge>
        </Tooltip>
      ))}
    </Group>
  )
}

const Users: FC = () => {
  const [page, setPage] = useState(1)
  const [update, setUpdate] = useState(new Date())
  const [editModalOpened, setEditModalOpened] = useState(false)
  const [activeUser, setActiveUser] = useState<UserInfoModel>({})
  const { data: users, total, setData: setUsers, updateData: updateUsers } = useArrayResponse<UserInfoModel>()
  const [hint, setHint] = useInputState('')
  const [school, setSchool] = useState<string | null>(null)
  const [verifyStatus, setVerifyStatus] = useState<string | null>(null)
  const [searching, setSearching] = useState(false)
  const [disabled, setDisabled] = useState(false)
  const [current, setCurrent] = useState(0)

  const modals = useModals()
  const { user: currentUser } = useUser()

  /// 学校列表（数据库为准，含已停用的「社会」组）
  const { data: schoolSettings } = api.school.useSchoolSettings()

  /// 学校短名 → 名称
  const schoolNameMap = useMemo(
    () => new Map((schoolSettings?.schools ?? []).map((s) => [s.slug, s.name])),
    [schoolSettings]
  )

  /// 学校短名 → 展示名（未知短名原样回显）
  const schoolLabel = useCallback(
    (slug?: string | null) => {
      if (!slug) return '—'
      return schoolNameMap.get(slug) ?? slug
    },
    [schoolNameMap]
  )

  /// 学校下拉选项
  const schoolOptions = useMemo(
    () =>
      (schoolSettings?.schools ?? [])
        .filter((s) => s.slug)
        .map((s) => ({ value: s.slug, label: s.name })),
    [schoolSettings]
  )
  const clipboard = useClipboard()
  const { t } = useTranslation()
  const viewport = useRef<HTMLDivElement>(null)

  useEffect(() => {
    viewport.current?.scrollTo({ top: 0, behavior: 'smooth' })
  }, [page, viewport])

  useEffect(() => {
    const fetchData = async () => {
      try {
        const res = await api.admin.adminUsers({
          count: ITEM_COUNT_PER_PAGE,
          skip: (page - 1) * ITEM_COUNT_PER_PAGE,
          school: school ?? undefined,
          verifyStatus: (verifyStatus as VerifyStatus | null) ?? undefined,
        })
        setUsers(res.data)
        setCurrent((page - 1) * ITEM_COUNT_PER_PAGE + res.data.length)
      } catch (err) {
        showErrorMsg(err, t)
      }
    }

    fetchData()
  }, [page, update, school, verifyStatus])

  const onSearch = async () => {
    try {
      if (!hint) {
        const res = await api.admin.adminUsers({
          count: ITEM_COUNT_PER_PAGE,
          skip: (page - 1) * ITEM_COUNT_PER_PAGE,
          school: school ?? undefined,
          verifyStatus: (verifyStatus as VerifyStatus | null) ?? undefined,
        })
        setUsers(res.data)
        setCurrent((page - 1) * ITEM_COUNT_PER_PAGE + res.data.length)
      } else {
        const res = await api.admin.adminSearchUsers({ hint })
        setUsers(res.data)
        setCurrent(res.data.length)
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSearching(false)
    }
  }

  const onToggleActive = async (user: UserInfoModel) => {
    setDisabled(true)

    try {
      await api.admin.adminUpdateUserInfo(user.id!, {
        emailConfirmed: !user.emailConfirmed,
      })
      if (users) {
        updateUsers(
          users.map((u) =>
            u.id === user.id
              ? {
                  ...u,
                  emailConfirmed: !u.emailConfirmed,
                }
              : u
          )
        )
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onResetPassword = async (user: UserInfoModel) => {
    setDisabled(true)
    try {
      const res = await api.admin.adminResetPassword(user.id!)

      modals.openModal({
        title: t('admin.content.users.reset.title', {
          name: user.userName,
        }),

        children: (
          <Stack>
            <Text>
              <Trans i18nKey="admin.content.users.reset.content" />
            </Text>
            <Text fw="bold" ff="monospace">
              {res.data}
            </Text>
            <Button
              onClick={() => {
                clipboard.copy(res.data)
                showNotification({
                  message: t('admin.notification.users.password_copied'),
                  color: 'teal',
                  icon: <Icon path={mdiCheck} size={1} />,
                })
              }}
            >
              {t('common.button.copy')}
            </Button>
          </Stack>
        ),
      })
    } catch (err: any) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const onDelete = async (user: UserInfoModel) => {
    try {
      setDisabled(true)
      if (!user.id) return

      await api.admin.adminDeleteUser(user.id)
      showNotification({
        message: t('admin.notification.users.deleted', {
          name: user.userName,
        }),
        color: 'teal',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      if (users) {
        updateUsers(users.filter((x) => x.id !== user.id))
      }
      setCurrent(current - 1)
      setUpdate(new Date())
    } catch (e: any) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <AdminPage
      isLoading={searching || !users}
      head={
        <>
          <TextInput
            w="36%"
            leftSection={<Icon path={mdiMagnify} size={1} />}
            placeholder={t('admin.placeholder.users.search')}
            value={hint}
            onChange={setHint}
            onKeyDown={(e) => {
              if (!searching && e.key === 'Enter') onSearch()
            }}
            rightSection={<Icon path={mdiAccountOutline} size={1} />}
          />
          <Select
            w="16%"
            placeholder="全部学校"
            data={schoolOptions}
            value={school}
            clearable
            searchable
            onChange={(v) => {
              setSchool(v)
              setPage(1)
            }}
          />
          <Select
            w="14%"
            placeholder="全部状态"
            data={VERIFY_STATUS_OPTIONS}
            value={verifyStatus}
            clearable
            onChange={(v) => {
              setVerifyStatus(v)
              setPage(1)
            }}
          />
          <Group justify="right">
            <Text fw="bold" size="sm">
              <Trans
                i18nKey="admin.content.users.stats"
                values={{
                  current,
                  total,
                }}
              >
                _<Code>_</Code>_
              </Trans>
            </Text>
            <ActionIcon size="lg" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              <Icon path={mdiArrowLeftBold} size={1} />
            </ActionIcon>
            <Text fw="bold" size="sm">
              {page}
            </Text>
            <ActionIcon size="lg" disabled={page * ITEM_COUNT_PER_PAGE >= total} onClick={() => setPage(page + 1)}>
              <Icon path={mdiArrowRightBold} size={1} />
            </ActionIcon>
          </Group>
        </>
      }
    >
      <Paper shadow="md" p="xs" w="100%">
        <ScrollArea viewportRef={viewport} offsetScrollbars scrollbarSize={4} h="calc(100vh - 190px)">
          <Table className={tableClasses.table}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th miw="1.8rem">{t('admin.label.users.active')}</Table.Th>
                <Table.Th>{t('common.label.user')}</Table.Th>
                <Table.Th>{t('account.label.email')}</Table.Th>
                <Table.Th miw="9rem">学校</Table.Th>
                <Table.Th miw="14rem">负责人类型 / 方向</Table.Th>
                <Table.Th miw="6rem">学籍审核</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {users &&
                users.map((user) => (
                  <Table.Tr key={user.id}>
                    <Table.Td>
                      <Switch
                        disabled={disabled}
                        checked={user.emailConfirmed ?? false}
                        onChange={() => onToggleActive(user)}
                      />
                    </Table.Td>
                    <Table.Td>
                      <Group wrap="nowrap" justify="space-between" gap="xs">
                        <Group wrap="nowrap" justify="left">
                          <Avatar alt="avatar" src={user.avatar} radius="xl">
                            {user.userName?.slice(0, 1) ?? 'U'}
                          </Avatar>
                          <Text ff="monospace" size="sm" fw="bold" lineClamp={1}>
                            {user.userName}
                          </Text>
                        </Group>
                        <Badge size="sm" color={RoleColorMap.get(user.role ?? Role.User)}>
                          {user.role}
                        </Badge>
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" lineClamp={1}>
                        {user.email}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" lineClamp={1}>
                        {schoolLabel(user.school)}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <DutyBadges user={user} schoolNames={schoolNameMap} />
                    </Table.Td>
                    <Table.Td>
                      <VerifyBadge user={user} />
                    </Table.Td>
                    <Table.Td align="right">
                      <Group wrap="nowrap" gap="sm" justify="right">
                        <ActionIcon
                          color="blue"
                          onClick={() => {
                            setActiveUser(user)
                            setEditModalOpened(true)
                          }}
                        >
                          <Icon path={mdiPencilOutline} size={1} />
                        </ActionIcon>
                        <ActionIconWithConfirm
                          iconPath={mdiLockReset}
                          color="orange"
                          message={t('admin.content.users.reset.message', {
                            name: user.userName,
                          })}
                          disabled={disabled}
                          onClick={() => onResetPassword(user)}
                        />
                        <ActionIconWithConfirm
                          iconPath={mdiDeleteOutline}
                          color="alert"
                          message={t('admin.content.users.delete', {
                            name: user.userName,
                          })}
                          disabled={disabled || user.id === currentUser?.userId}
                          onClick={() => onDelete(user)}
                        />
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
        <UserEditModal
          size="35%"
          title={t('admin.button.users.edit')}
          user={activeUser}
          opened={editModalOpened}
          onClose={() => setEditModalOpened(false)}
          mutateUser={(user: UserInfoModel) => {
            updateUsers(
              [user, ...(users?.filter((n) => n.id !== user.id) ?? [])].sort((a, b) => (a.id! < b.id! ? -1 : 1))
            )
          }}
        />
      </Paper>
    </AdminPage>
  )
}

export default Users
