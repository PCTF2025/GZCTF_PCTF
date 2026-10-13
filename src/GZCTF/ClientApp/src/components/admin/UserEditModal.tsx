import {
  Avatar,
  Badge,
  Box,
  Button,
  Center,
  Checkbox,
  Chip,
  Grid,
  Group,
  Modal,
  ModalProps,
  MultiSelect,
  Paper,
  Radio,
  Select,
  SimpleGrid,
  Stack,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { showErrorMsg } from '@Utils/Shared'
import { useSyncOnChange } from '@Hooks/useSyncOnChange'
import { useUser } from '@Hooks/useUser'
import api, {
  AdminUserInfoModel,
  ChallengeCategory,
  PERMISSION_META,
  Role,
  UserInfoModel,
  UserPermission,
  hasPermission,
} from '@Api'

export const RoleColorMap = new Map<Role, string>([
  [Role.Admin, 'blue'],
  [Role.User, 'green'],
  [Role.Monitor, 'yellow'],
  [Role.Banned, 'red'],
])

interface UserEditModalProps extends ModalProps {
  user: UserInfoModel
  mutateUser: (user: UserInfoModel) => void
}

/// 「社会」学校短名，与后端 AccountController.SocialSchoolSlug 保持一致
const SOCIAL_SCHOOL_SLUG = 'social'

export const UserEditModal: FC<UserEditModalProps> = (props) => {
  const { user, mutateUser, ...modalProps } = props
  const { user: self } = useUser()

  const [disabled, setDisabled] = useState(false)
  const [profile, setProfile] = useState<AdminUserInfoModel>(() => ({ ...user }))

  const { t } = useTranslation()
  const isSelf = self?.userId === user.id

  /// 已勾选的权限位（用位运算叠加，支持同时赋予多个权限）
  const permissions = profile.permissions ?? UserPermission.None

  const togglePermission = (bit: UserPermission) => {
    const next = (permissions & bit) === bit ? permissions & ~bit : permissions | bit
    setProfile({ ...profile, permissions: next })
  }

  /// 出题管理员可管理的方向（逗号分隔字符串，与后端一致）
  const managedCategories = (profile.managedCategories ?? '')
    .split(',')
    .map((v) => v.trim())
    .filter(Boolean)

  const toggleCategory = (cat: ChallengeCategory) => {
    const next = managedCategories.includes(cat)
      ? managedCategories.filter((c) => c !== cat)
      : [...managedCategories, cat]
    setProfile({ ...profile, managedCategories: next.join(',') })
  }

  /// 学校清单（用于「所属学校」下拉，含已停用的「社会」组）
  const { data: schoolSettings } = api.school.useSchoolSettings()

  const schoolOptions = useMemo(
    () =>
      (schoolSettings?.schools ?? [])
        .filter((s) => s.slug)
        .map((s) => ({ value: s.slug, label: s.name })),
    [schoolSettings]
  )

  /// 学校短名 → 全称，用于展示历史数据里已下架学校的名称
  const schoolNameMap = useMemo(
    () => new Map(schoolOptions.map((o) => [o.value, o.label])),
    [schoolOptions]
  )

  /// 学校负责人负责的学校（逗号分隔短名字符串，与后端存储格式一致）
  const [schoolDraft, setSchoolDraft] = useState('')

  /// 已负责的学校（短名数组，用于多选下拉）
  const managedSchoolList = useMemo(
    () =>
      schoolDraft
        .split(',')
        .map((v) => v.trim())
        .filter(Boolean),
    [schoolDraft]
  )

  /// 下拉数据：学校全称展示；若历史数据中的短名已不在学校列表里，
  /// 补一条「全称（短名）」项，避免选择框显示为空导致保存时被清掉。
  const managedSchoolData = useMemo(() => {
    const known = new Set(schoolOptions.map((o) => o.value))
    const missing = managedSchoolList
      .filter((slug) => !known.has(slug))
      .map((slug) => ({ value: slug, label: `${schoolNameMap.get(slug) ?? slug}（已下架）` }))
    return [...schoolOptions, ...missing]
  }, [schoolOptions, schoolNameMap, managedSchoolList])

  /// 展示用摘要（把短名换成全称，便于管理员确认）
  const managedSchoolSummary = useMemo(
    () => managedSchoolList.map((slug) => schoolNameMap.get(slug) ?? slug).join('、'),
    [managedSchoolList, schoolNameMap]
  )

  useSyncOnChange([user], () => setSchoolDraft(user.managedSchools ?? ''))

  useEffect(() => {
    setProfile((prev) => ({ ...prev, managedSchools: schoolDraft }))
  }, [schoolDraft])

  useSyncOnChange([user], () => setProfile({ ...user }))

  const onChangeProfile = async () => {
    if (!user.id) return

    setDisabled(true)

    try {
      await api.admin.adminUpdateUserInfo(user.id, profile)
      showNotification({
        color: 'teal',
        message: t('admin.notification.users.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutateUser({ ...user, ...profile })
      modalProps.onClose()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <Modal {...modalProps}>
      {/* User Info */}
      <Stack gap="md" m="auto" mt={15}>
        <Grid grow>
          <Grid.Col span={8}>
            <TextInput
              label={t('account.label.username')}
              type="text"
              w="100%"
              value={profile.userName ?? 'ctfer'}
              disabled={disabled}
              onChange={(event) => setProfile({ ...profile, userName: event.target.value })}
            />
          </Grid.Col>
          <Grid.Col span={4}>
            <Center>
              <Avatar alt="avatar" radius="xl" size={70} src={user.avatar}>
                {user.userName?.slice(0, 1) ?? 'U'}
              </Avatar>
            </Center>
          </Grid.Col>
        </Grid>
        <Radio.Group
          label={t('admin.label.users.role')}
          value={profile.role as Role | undefined}
          onChange={(value) => {
            setProfile({ ...profile, role: value as Role })
          }}
        >
          <Group grow mt="xs">
            {Object.keys(Role).map((role) => (
              <Radio
                key={role}
                value={role}
                label={
                  <Text size="sm" fw="bold">
                    {role}
                  </Text>
                }
                color={RoleColorMap.get(role as Role)}
                disabled={disabled || isSelf}
              />
            ))}
          </Group>
        </Radio.Group>
        {/* 学校归属：平台管理员可直接调整 */}
        <Paper p="sm" withBorder>
          <Stack gap="xs">
            <Group gap="xs">
              <Text size="sm" fw={500}>
                所属学校
              </Text>
              <Badge size="xs" variant="light" color="gray">
                仅平台管理员可调整
              </Badge>
            </Group>
            <Select
              placeholder="未归属"
              description="调整后学生将出现在该校的审核列表中；清空表示取消学校归属"
              data={schoolOptions}
              value={profile.school || null}
              disabled={disabled || isSelf}
              clearable
              searchable
              w="100%"
              onChange={(v) => setProfile({ ...profile, school: v ?? '' })}
            />
            <Text size="xs" c="dimmed">
              {!profile.school
                ? '当前未设置学校归属，该账号不会进入任何学校的审核列表。'
                : profile.school === SOCIAL_SCHOOL_SLUG
                  ? '该账号归入「社会」组，仅作归属标识，不参与学校学籍审核。'
                  : `当前归属：${schoolOptions.find((o) => o.value === profile.school)?.label ?? profile.school}`}
            </Text>
          </Stack>
        </Paper>

        {/* 附加职能权限（与角色正交，可同时赋予多个） */}
        <Paper p="sm" withBorder>
          <Stack gap="xs">
            <Group gap="xs">
              <Text size="sm" fw={500}>
                附加职能权限
              </Text>
              <Badge size="xs" variant="light" color="gray">
                与角色相互独立，可同时勾选
              </Badge>
            </Group>

            <Stack gap="xs">
              {PERMISSION_META.map((meta) => (
                <Checkbox
                  key={meta.value}
                  checked={hasPermission(permissions, meta.value)}
                  disabled={disabled || isSelf}
                  onChange={() => togglePermission(meta.value)}
                  label={
                    <Box>
                      <Text size="sm" fw={500}>
                        {meta.label}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {meta.description}
                      </Text>
                    </Box>
                  }
                />
              ))}
            </Stack>

            {/* 出题人或出题管理员：指定方向范围 */}
            {(hasPermission(permissions, UserPermission.ChallengeAuthor) ||
              hasPermission(permissions, UserPermission.ChallengeManager)) && (
              <Box>
                <Text size="sm" fw={500} mb={4}>
                  指定出题方向
                </Text>
                <Text size="xs" c="dimmed" mb="xs">
                  {hasPermission(permissions, UserPermission.ChallengeManager)
                    ? '出题管理员：可修改、删除这些方向下的全部题目'
                    : '出题人：只能在这些方向内新增题目'}
                  ；不勾选任何方向表示不限制
                </Text>
                <Group gap="xs">
                  {Object.values(ChallengeCategory).map((cat) => (
                    <Chip
                      key={cat}
                      checked={managedCategories.includes(cat)}
                      disabled={disabled || isSelf}
                      onChange={() => toggleCategory(cat)}
                    >
                      {cat}
                    </Chip>
                  ))}
                </Group>
              </Box>
            )}

            {/* 学校负责人：负责审核的学校（多选，展示学校全称，底层仍存短名） */}
            {hasPermission(permissions, UserPermission.SchoolManager) && (
              <>
                <MultiSelect
                  label="负责审核的学校"
                  description="可多选；留空则该权限不生效"
                  placeholder={schoolOptions.length === 0 ? '暂无可选学校' : '选择该负责人审核的学校'}
                  nothingFoundMessage="没有匹配的学校"
                  data={managedSchoolData}
                  value={managedSchoolList}
                  disabled={disabled || isSelf}
                  clearable
                  searchable
                  w="100%"
                  onChange={(values) => setSchoolDraft(values.join(','))}
                />
                {managedSchoolList.length > 0 && (
                  <Text size="xs" c="dimmed">
                    将负责：{managedSchoolSummary}
                  </Text>
                )}
              </>
            )}
          </Stack>
        </Paper>

        <SimpleGrid cols={2}>
          <TextInput
            label={t('account.label.email')}
            type="email"
            w="100%"
            value={profile.email ?? 'ctfer@gzti.me'}
            disabled={disabled}
            onChange={(event) => setProfile({ ...profile, email: event.target.value })}
          />
          <TextInput
            label={t('account.label.phone')}
            type="tel"
            w="100%"
            value={profile.phone ?? ''}
            disabled={disabled}
            onChange={(event) => setProfile({ ...profile, phone: event.target.value })}
          />
          <TextInput
            label={t('account.label.student_id')}
            type="text"
            w="100%"
            value={profile.stdNumber ?? ''}
            disabled={disabled}
            onChange={(event) => setProfile({ ...profile, stdNumber: event.target.value })}
          />
          <TextInput
            label={t('account.label.real_name')}
            type="text"
            w="100%"
            value={profile.realName ?? ''}
            disabled={disabled}
            onChange={(event) => setProfile({ ...profile, realName: event.target.value })}
          />
        </SimpleGrid>
        <Textarea
          label={t('account.label.bio')}
          value={profile.bio ?? t('account.placeholder.bio')}
          w="100%"
          disabled={disabled}
          autosize
          minRows={2}
          maxRows={4}
          onChange={(event) => setProfile({ ...profile, bio: event.target.value })}
        />

        <Stack gap={2}>
          <Group justify="space-between">
            <Text size="sm" fw={500}>
              {t('common.label.ip')}
            </Text>
            <Text size="sm" span fw={500} ff="monospace">
              {user.ip}
            </Text>
          </Group>
          <Group justify="space-between">
            <Text size="sm" fw={500}>
              {t('admin.label.users.last_visit')}
            </Text>
            <Text size="sm" span fw={500} ff="monospace">
              {dayjs(user.lastVisitedUtc).format('YYYY-MM-DD HH:mm:ss')}
            </Text>
          </Group>
        </Stack>

        <Group grow m="auto" w="100%">
          <Button fullWidth disabled={disabled} onClick={onChangeProfile}>
            {t('admin.button.save')}
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
