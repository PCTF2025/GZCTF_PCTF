import { Alert, Anchor, Badge, Button, Group, PinInput, Select, Stack, Text, TextInput, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiAlertCircleOutline, mdiArrowLeft, mdiEmailFastOutline, mdiLoginVariant } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useUser } from '@Hooks/useUser'
import api, { ClientSsoSchool, RequestResponse } from '@Api'

/// 年级选项
const GRADE_OPTIONS = [
  '大一',
  '大二',
  '大三',
  '大四',
  '大五',
  '研一',
  '研二',
  '研三',
  '博士',
  '教职工',
  '其他',
].map((v) => ({ value: v, label: v }))

/// 步骤：填邮箱 → 填验证码与学籍信息
type Step = 'email' | 'verify'

interface Props {
  /// 目标学校
  school: ClientSsoSchool
  /// 返回学校列表
  onBack: () => void
  /// 是否作为独立面板展示（仅学校认证模式）
  standalone?: boolean
}

export const SsoCodeLogin: FC<Props> = ({ school, onBack, standalone }) => {
  const navigate = useNavigate()
  const { mutate: mutateProfile } = useUser()

  const [step, setStep] = useState<Step>('email')
  const [emailPrefix, setEmailPrefix] = useState('')
  const [code, setCode] = useState('')
  const [userName, setUserName] = useState('')
  const [realName, setRealName] = useState('')
  const [stdNumber, setStdNumber] = useState('')
  const [grade, setGrade] = useState<string | null>(null)
  const [registered, setRegistered] = useState<boolean | null>(null)
  const [needProfile, setNeedProfile] = useState(true)
  const [disabled, setDisabled] = useState(false)
  const [countdown, setCountdown] = useState(0)

  const suffixes = school.emailSuffixes ?? []
  const defaultSuffix = suffixes[0] ?? ''
  const [suffix, setSuffix] = useState(defaultSuffix)

  useEffect(() => {
    setSuffix(defaultSuffix)
  }, [defaultSuffix])

  /// 完整邮箱：允许直接输入带 @ 的邮箱
  const email = useMemo(() => {
    const value = emailPrefix.trim()
    if (!value) return ''
    if (value.includes('@')) return value
    return `${value}@${suffix}`
  }, [emailPrefix, suffix])

  /// 倒计时
  useEffect(() => {
    if (countdown <= 0) return
    const timer = window.setInterval(() => setCountdown((c) => (c <= 1 ? 0 : c - 1)), 1000)
    return () => window.clearInterval(timer)
  }, [countdown])

  const notify = (color: string, title: string, message: string) =>
    showNotification({
      color,
      title,
      message,
      icon: <Icon path={color === 'red' ? mdiAlertCircleOutline : mdiLoginVariant} size={1} />,
    })

  /// 第一步：发送验证码
  const onSendCode = async () => {
    if (!email || !email.includes('@')) {
      notify('red', '请填写邮箱', '请输入学号或完整邮箱地址')
      return
    }

    setDisabled(true)
    try {
      const res = await api.account.ssoSendCode({ schoolSlug: school.slug!, email })
      notify('teal', '验证码已发送', res.data.title ?? `请查收 ${email} 的邮件`)
      setStep('verify')
      setCountdown(60)
    } catch (err) {
      const e = err as { response?: { data?: RequestResponse } }
      notify('red', '发送失败', e.response?.data?.title ?? '请稍后重试')
    } finally {
      setDisabled(false)
    }
  }

  /// 第二步：校验验证码，判断是注册还是登录
  /// 传入 value 是因为 PinInput 的 onComplete 携带完整值，
  /// 此时受控状态可能尚未同步
  const onCheckCode = async (value?: string) => {
    const input = (value ?? code).trim()

    if (input.length !== 6) {
      notify('red', '验证码不完整', '请输入 6 位数字验证码')
      return
    }

    if (value && value !== code) setCode(value)

    setDisabled(true)
    try {
      const res = await api.account.ssoCheckCode({ schoolSlug: school.slug!, email, code: input })
      const data = res.data

      setRegistered(Boolean(data.registered))
      setNeedProfile(Boolean(data.needProfile))

      // 已注册用户回填已有资料，减少重复输入
      if (data.realName) setRealName(data.realName)
      if (data.stdNumber) setStdNumber(data.stdNumber)
      if (data.grade) setGrade(data.grade)
    } catch (err) {
      const e = err as { response?: { data?: RequestResponse } }
      notify('red', '验证失败', e.response?.data?.title ?? '验证码不正确或已过期')
    } finally {
      setDisabled(false)
    }
  }

  /// 第三步：一键注册 / 登录
  const onLoginWithCode = async (value?: string) => {
    const input = (value ?? code).trim()

    if (input.length !== 6) {
      notify('red', '验证码不完整', '请输入 6 位数字验证码')
      return
    }

    // 首次注册需补齐学籍信息
    if (needProfile && (!realName.trim() || !stdNumber.trim())) {
      notify('red', '请完善学籍信息', '真实姓名与学号为必填项')
      return
    }

    setDisabled(true)
    try {
      const res = await api.account.ssoLoginWithCode({
        schoolSlug: school.slug!,
        email,
        code: input,
        userName: userName.trim() || undefined,
        realName: realName.trim() || undefined,
        stdNumber: stdNumber.trim() || undefined,
        grade: grade || undefined,
      })

      const result = res.data

      // 清理注册页遗留的暂存
      try {
        window.localStorage.removeItem('pctf.sso.pendingSchool')
        window.localStorage.removeItem('pctf.sso.pendingEmail')
      } catch {
        /* ignore */
      }

      notify(
        'teal',
        result.created ? '注册成功' : '登录成功',
        `${result.schoolName ?? school.name} 已绑定，学籍信息待学校管理员审核`
      )

      // 先刷新用户态再跳转，避免首页读到旧的未登录缓存而需要手动刷新
      await mutateProfile()
      navigate('/')
    } catch (err) {
      const e = err as { response?: { data?: RequestResponse } }
      notify('red', '操作失败', e.response?.data?.title ?? '请稍后重试')
    } finally {
      setDisabled(false)
    }
  }

  if (step === 'email') {
    return (
      <Stack align="center" justify="center" gap="md" w="100%">
        <Title order={standalone ? 3 : 4} ta="center">
          {school.name} 邮箱验证码登录
        </Title>{' '}
        <Text size="sm" c="dimmed" ta="center">
          输入学校邮箱，我们会发送 6 位验证码。
          {suffixes.length > 0 && `（支持 ${suffixes.map((s) => `@${s}`).join(' / ')}）`}
        </Text>
        <TextInput
          required
          label="学号 / 邮箱"
          placeholder="请输入学号或完整邮箱"
          w="100%"
          value={emailPrefix}
          disabled={disabled}
          onChange={(event) => setEmailPrefix(event.currentTarget.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void onSendCode()
          }}
        />
        {!emailPrefix.includes('@') && suffixes.length > 1 && (
          <Select
            label="邮箱后缀"
            data={suffixes.map((s) => ({ value: s, label: `@${s}` }))}
            value={suffix}
            w="100%"
            allowDeselect={false}
            onChange={(v) => setSuffix(v ?? defaultSuffix)}
          />
        )}
        {emailPrefix && !emailPrefix.includes('@') && (
          <Text size="xs" c="dimmed" w="100%">
            登录邮箱：{email}
          </Text>
        )}
        <Group w="100%" grow>
          <Button variant="outline" disabled={disabled} onClick={onBack}>
            返回
          </Button>
          <Button disabled={disabled} loading={disabled} onClick={onSendCode}>
            发送验证码
          </Button>
        </Group>
      </Stack>
    )
  }

  return (
    <Stack align="center" justify="center" gap="md" w="100%">
      <Title order={standalone ? 3 : 4} ta="center">
        {school.name} 验证码登录
      </Title>

      <Alert
        w="100%"
        color="teal"
        variant="light"
        icon={<Icon path={mdiEmailFastOutline} size={1} />}
        title={`验证码已发送至 ${email}`}
      >
        <Group justify="space-between">
          <Text size="xs">10 分钟内有效，请勿转发给他人</Text>
          <Anchor
            size="xs"
            onClick={() => {
              if (countdown > 0 || disabled) return
              void onSendCode()
            }}
          >
            {countdown > 0 ? `${countdown}s 后可重发` : '重新发送'}
          </Anchor>
        </Group>
      </Alert>

      <Stack w="100%" gap={6} align="center">
        <Text size="sm" fw={500}>
          验证码
        </Text>
        <PinInput
          length={6}
          type="number"
          oneTimeCode
          size="md"
          value={code}
          disabled={disabled}
          onChange={setCode}
          onComplete={(value) => void onCheckCode(value)}
        />
        {registered !== null && (
          <Badge variant="light" color={registered ? 'blue' : 'grape'}>
            {registered ? '该邮箱已注册，将直接登录' : '该邮箱未注册，将自动创建账号'}
          </Badge>
        )}
      </Stack>

      {registered === false || (registered && needProfile) ? (
        <>
          <TextInput
            label="用户名（可选）"
            placeholder={`留空则使用学号 ${stdNumber || ''}`.trim()}
            description="登录时使用的账号名，3-15 位字母、数字或下划线"
            w="100%"
            value={userName}
            disabled={disabled}
            onChange={(event) => setUserName(event.currentTarget.value.replace(/[^A-Za-z0-9_]/g, ''))}
          />
          <TextInput
            required
            label="真实姓名"
            placeholder="请填写与学籍一致的姓名"
            w="100%"
            value={realName}
            disabled={disabled}
            onChange={(event) => setRealName(event.currentTarget.value)}
          />
          <TextInput
            required
            label="学号"
            placeholder="请输入学号"
            w="100%"
            value={stdNumber}
            disabled={disabled}
            onChange={(event) => setStdNumber(event.currentTarget.value)}
          />
          <Select
            label="年级"
            placeholder="请选择年级"
            data={GRADE_OPTIONS}
            w="100%"
            value={grade}
            disabled={disabled}
            onChange={setGrade}
          />
          <Text size="xs" c="dimmed" ta="center" w="100%">
            学籍信息将提交给 {school.name} 管理员审核，审核通过后可报名主办赛道。
          </Text>
        </>
      ) : null}

      <Group w="100%" grow>
        <Button variant="outline" disabled={disabled} onClick={() => setStep('email')}>
          <Icon path={mdiArrowLeft} size={0.9} />
        </Button>
        <Button disabled={disabled} loading={disabled} onClick={() => void onLoginWithCode()}>
          {registered === false ? '注册并登录' : '登录'}
        </Button>
      </Group>
    </Stack>
  )
}
