import { Anchor, Badge, Group, Select, Stack, Text, TextInput } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { ProfileUserInfoModel, SchoolBindSource, SchoolOptionModel } from '@Api'
import { tryGetClientError } from '@Utils/Shared'

interface SchoolBindingProps {
  /** 当前用户资料，用于读取已绑定的学校 */
  profile?: ProfileUserInfoModel | null
  /** 只读模式（仅展示绑定状态，不提供操作） */
  readOnly?: boolean
  /** 绑定成功后回调，便于父组件刷新资料 */
  onBound?: () => void
}

const sourceLabel = (source?: SchoolBindSource) => {
  switch (source) {
    case SchoolBindSource.Sso:
      return '学校统一认证绑定'
    case SchoolBindSource.Invite:
      return '邀请码绑定'
    default:
      return '未绑定'
  }
}

/**
 * 学校绑定组件。
 *
 * 两种绑定途径：
 *   1. 学校统一身份认证（快速登录）—— 登录时由后端自动绑定，此处仅展示；
 *   2. 邀请码绑定 —— 未使用快速登录的用户，选择学校 + 输入邀请码完成绑定。
 */
export const SchoolBinding: FC<SchoolBindingProps> = ({ profile, readOnly = false, onBound }) => {
  const { t } = useTranslation()

  const { data: schools, mutate: mutateSchools } = useSWR(
    '/api/account/sso/schools',
    async () => (await api.account.ssoSchools()).data,
    { revalidateOnFocus: false }
  )

  const [slug, setSlug] = useState<string | null>(null)
  const [inviteCode, setInviteCode] = useState('')
  const [stdNumber, setStdNumber] = useState(profile?.stdNumber ?? '')
  const [disabled, setDisabled] = useState(false)

  const bound = Boolean(profile?.school)

  const schoolOptions = useMemo(
    () => (schools ?? []).map((s: SchoolOptionModel) => ({ value: s.slug, label: `${s.name}（${s.slug}）` })),
    [schools]
  )

  const selectedSchool = useMemo(() => (schools ?? []).find((s: SchoolOptionModel) => s.slug === slug), [schools, slug])

  const boundSchoolName = useMemo(() => {
    if (!profile?.school) return null
    const found = (schools ?? []).find((s: SchoolOptionModel) => s.slug === profile.school)
    return found?.name ?? profile.school
  }, [schools, profile?.school])

  const onBind = async () => {
    if (!slug) {
      showNotification({
        color: 'orange',
        message: '请选择所属学校',
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    setDisabled(true)
    try {
      await api.account.ssoBind({
        schoolSlug: slug,
        inviteCode: inviteCode || undefined,
        stdNumber: stdNumber || undefined,
      })

      showNotification({
        color: 'teal',
        title: t('common.success'),
        message: `已绑定 ${selectedSchool?.name ?? slug}`,
        icon: <Icon path={mdiCheck} size={1} />,
      })

      setInviteCode('')
      setSlug(null)
      await mutateSchools()
      onBound?.()
    } catch (err: unknown) {
      const { title, message } = tryGetClientError(err as never, t)
      showNotification({
        color: 'red',
        title,
        message,
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setDisabled(false)
    }
  }

  // 已绑定：展示状态（快速登录绑定的不允许自行更换）
  if (bound) {
    return (
      <Stack gap="xs">
        <Group gap="xs">
          <Text size="sm" fw={500}>
            所属学校
          </Text>
          <Badge color={profile?.schoolSource === SchoolBindSource.Sso ? 'blue' : 'teal'} variant="light">
            {boundSchoolName}
          </Badge>
          <Text size="xs" c="dimmed">
            {sourceLabel(profile?.schoolSource)}
          </Text>
        </Group>
        {profile?.schoolSource === SchoolBindSource.Sso && (
          <Text size="xs" c="dimmed">
            通过学校统一身份认证自动绑定，如需更换请联系赛事管理员。
          </Text>
        )}
        {!readOnly && profile?.schoolSource !== SchoolBindSource.Sso && (
          <Text size="xs" c="dimmed">
            如需更换学校，请重新选择并绑定。
          </Text>
        )}
      </Stack>
    )
  }

  if (readOnly) {
    return (
      <Text size="sm" c="dimmed">
        未绑定学校
      </Text>
    )
  }

  return (
    <Stack gap="sm">
      <Select
        required
        label="所属学校"
        description="使用学校统一身份认证（快速登录）可自动绑定；否则请选择学校并输入邀请码"
        placeholder="请选择学校"
        data={schoolOptions}
        value={slug}
        disabled={disabled}
        searchable
        onChange={setSlug}
      />
      <TextInput
        label="邀请码"
        description="向赛事管理员获取；学校未启用邀请码时可留空"
        placeholder="由管理员下发"
        value={inviteCode}
        disabled={disabled}
        onChange={(e) => setInviteCode(e.currentTarget.value)}
      />
      <TextInput
        label="学号"
        description="用于主办赛道报名核对，请填写真实学号"
        placeholder="如 2021001"
        value={stdNumber}
        disabled={disabled}
        onChange={(e) => setStdNumber(e.currentTarget.value)}
      />
      <Anchor component="button" type="button" fz="sm" onClick={onBind} aria-disabled={disabled}>
        {disabled ? '绑定中…' : '绑定学校'}
      </Anchor>
    </Stack>
  )
}

export default SchoolBinding
