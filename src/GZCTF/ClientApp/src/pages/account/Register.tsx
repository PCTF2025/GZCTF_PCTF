import { Alert, Anchor, Button, Divider, PasswordInput, Select, Text, TextInput } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose, mdiSchoolOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { AccountView } from '@Components/AccountView'
import { Captcha, useCaptchaRef } from '@Components/Captcha'
import { StrengthPasswordInput } from '@Components/StrengthPasswordInput'
import { encryptApiData } from '@Utils/Crypto'
import { tryGetClientError } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import api, { RegisterStatus } from '@Api'
import misc from '@Styles/Misc.module.css'

/// 年级选项（与后端 Grade 字段对应）
export const GRADE_OPTIONS = [
  { value: '大一', label: '大一' },
  { value: '大二', label: '大二' },
  { value: '大三', label: '大三' },
  { value: '大四', label: '大四' },
  { value: '大五', label: '大五' },
  { value: '研一', label: '研一' },
  { value: '研二', label: '研二' },
  { value: '研三', label: '研三' },
  { value: '博士', label: '博士' },
  { value: '教职工', label: '教职工' },
  { value: '其他', label: '其他' },
]

const Register: FC = () => {
  const [pwd, setPwd] = useInputState('')
  const [retypedPwd, setRetypedPwd] = useInputState('')
  const [uname, setUname] = useInputState('')
  const [email, setEmail] = useInputState('')
  const [disabled, setDisabled] = useState(false)
  const { config } = useConfig()

  // 学籍信息：注册时一并提交，由对应学校的管理员审核
  const [pickedSchool, setPickedSchool] = useState<string | null>(null)
  const [realName, setRealName] = useInputState('')
  const [stdNumber, setStdNumber] = useInputState('')
  const [inviteCode, setInviteCode] = useInputState('')
  const [grade, setGrade] = useState<string | null>(null)

  const navigate = useNavigate()
  const { mutate: mutateProfile } = useUser()
  const { captchaRef, getToken, cleanUp } = useCaptchaRef()
  const params = useSearchParams()[0]

  const { t } = useTranslation()

  // 来自学校邮箱认证的待绑定学校与预填邮箱
  const schoolSlug = useMemo(() => {
    const fromQuery = params.get('school')
    if (fromQuery) return fromQuery
    try {
      return window.localStorage.getItem('pctf.sso.pendingSchool') ?? ''
    } catch {
      return ''
    }
  }, [params])

  // 学校列表以数据库 Schools 表为准（/api/account/sso/schools），
  // 不再读 config.sso.schools —— 那是旧 CSV 配置，容易与后台维护的学校脱节。
  const { data: ssoSchools } = api.account.useSsoSchools()

  const boundSchoolName = useMemo(() => {
    if (!schoolSlug) return ''
    return (ssoSchools ?? []).find((s) => s.slug === schoolSlug)?.name ?? ''
  }, [schoolSlug, ssoSchools])

  // 可选学校列表
  const schoolOptions = useMemo(
    () =>
      (ssoSchools ?? [])
        .filter((s) => s.name && s.slug)
        .map((s) => ({ value: s.slug, label: s.name })),
    [ssoSchools]
  )

  // 邮箱认证进入时，学校已确定，直接回填
  useEffect(() => {
    if (schoolSlug) setPickedSchool(schoolSlug)
  }, [schoolSlug])

  useEffect(() => {
    const preset = params.get('email')
    if (preset && !email) setEmail(preset)
  }, [params])

  const effectiveSchool = pickedSchool ?? schoolSlug ?? ''

  const RegisterStatusMap = new Map([
    [
      RegisterStatus.LoggedIn,
      {
        message: t('account.notification.register.logged_in'),
      },
    ],
    [
      RegisterStatus.AdminConfirmationRequired,
      {
        title: t('account.notification.register.request_sent.title'),
        message: t('account.notification.register.request_sent.message'),
      },
    ],
    [
      RegisterStatus.EmailConfirmationRequired,
      {
        title: t('common.email.sent.title'),
        message: t('common.email.sent.message'),
      },
    ],
    [undefined, undefined],
  ])

  usePageTitle(t('account.title.register'))

  const onRegister = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    if (pwd !== retypedPwd) {
      showNotification({
        color: 'red',
        title: t('common.error.check_input'),
        message: t('account.password.not_match'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    // 学校邀请码为注册前提（社会组注册已暂时关闭）。
    // 经学校邮箱认证入口进入时学校已确定，无需邀请码。
    if (!schoolSlug && !inviteCode.trim()) {
      showNotification({
        color: 'red',
        title: '需要学校邀请码',
        message: '当前仅开放持邀请码的学校注册，请向所在学校获取邀请码',
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    // 学籍信息为报名主办赛道的前提，注册时一并校验。
    // 学校不再单独必填：由邀请码自动匹配。
    if (!realName.trim() || !stdNumber.trim() || !grade) {
      showNotification({
        color: 'orange',
        title: '请完善学籍信息',
        message: '真实姓名、学号、年级均为必填',
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    const { valid, token } = await getToken()

    if (!valid) {
      showNotification({
        color: 'orange',
        title: t('account.notification.captcha.not_valid'),
        message: t('common.error.try_later'),
        loading: true,
      })
      return
    }

    setDisabled(true)

    showNotification({
      color: 'orange',
      id: 'register-status',
      title: t('account.notification.captcha.request_sent.title'),
      message: t('account.notification.captcha.request_sent.message'),
      loading: true,
      autoClose: false,
    })

    try {
      const res = await api.account.accountRegister({
        userName: uname,
        password: await encryptApiData(t, pwd, config.apiPublicKey),
        email: email,
        challenge: token,
        // 学校邮箱认证入口进入时携带学校，注册即完成学校绑定
        schoolSlug: effectiveSchool || undefined,
        inviteCode: inviteCode.trim() || undefined,
        // 学籍信息：提交后由对应学校管理员审核
        realName: realName.trim() || undefined,
        stdNumber: stdNumber.trim() || undefined,
        grade: grade || undefined,
      })
      const data = RegisterStatusMap.get(res.data.data)
      if (data) {
        updateNotification({
          id: 'register-status',
          color: 'teal',
          title: data.title,
          message: data.message,
          icon: <Icon path={mdiCheck} size={1} />,
          loading: false,
          autoClose: true,
        })
        cleanUp(true)

        try {
          window.localStorage.removeItem('pctf.sso.pendingSchool')
          window.localStorage.removeItem('pctf.sso.pendingEmail')
        } catch {
          /* 忽略 */
        }

        if (res.data.data === RegisterStatus.LoggedIn) {
          // 先刷新用户态再跳转，避免首页读到旧的未登录缓存而需要手动刷新
          await mutateProfile()
          navigate('/')
        }
        else if (res.data.data === RegisterStatus.EmailConfirmationRequired)
          navigate('/account/pending', { state: { email } })
        else navigate('/account/login')
      }
    } catch (err: any) {
      const { title, message } = tryGetClientError(err, t)

      updateNotification({
        id: 'register-status',
        color: 'red',
        title,
        message,
        icon: <Icon path={mdiClose} size={1} />,
        loading: false,
        autoClose: true,
      })
      cleanUp(false)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <AccountView onSubmit={onRegister}>
      {schoolSlug && (
        <Alert color="teal" variant="light" icon={<Icon path={mdiSchoolOutline} size={1} />}>
          <Text size="sm">
            正在通过 <b>{boundSchoolName || schoolSlug}</b> 邮箱认证注册，注册后将自动绑定该学校。
          </Text>
        </Alert>
      )}
      <TextInput
        required
        label={t('account.label.email')}
        type="email"
        placeholder="ctf@example.com"
        w="100%"
        value={email}
        disabled={disabled}
        onChange={(event) => setEmail(event.currentTarget.value)}
      />
      <TextInput
        label={t('account.label.username')}
        type="text"
        placeholder={stdNumber.trim() ? `留空则使用学号 ${stdNumber.trim()}` : '留空则使用学号'}
        description="选填，3-15 位字母、数字或下划线"
        w="100%"
        value={uname}
        disabled={disabled}
        error={uname.length > 0 && uname.length < 3 ? '用户名至少 3 位' : undefined}
        onChange={(event) => setUname(event.currentTarget.value.replace(/[^A-Za-z0-9_]/g, ''))}
      />
      <StrengthPasswordInput value={pwd} onChange={(event) => setPwd(event.currentTarget.value)} disabled={disabled} />
      <PasswordInput
        required
        label={t('account.label.password_retype')}
        value={retypedPwd}
        onChange={(event) => setRetypedPwd(event.currentTarget.value)}
        disabled={disabled}
        w="100%"
        error={pwd !== retypedPwd}
      />
      <Divider label="学籍信息（报名主办赛道需经学校审核）" labelPosition="center" my="xs" />
      <TextInput
        required={!schoolSlug}
        label="学校邀请码"
        description={
          schoolSlug
            ? '已通过学校邮箱认证入口进入，无需填写邀请码'
            : '必填：请填写学校下发的邀请码，将自动绑定该学校。未持邀请码暂无法注册'
        }
        placeholder="如 PCTF2026-XXXX"
        w="100%"
        disabled={disabled || Boolean(schoolSlug)}
        value={inviteCode}
        onChange={(event) => setInviteCode(event.currentTarget.value)}
      />
      <TextInput
        required
        label="真实姓名"
        description="请填写与学籍一致的姓名"
        placeholder="张三"
        w="100%"
        value={realName}
        disabled={disabled}
        onChange={(event) => setRealName(event.currentTarget.value)}
      />
      <TextInput
        required
        label="学号"
        placeholder="2021001"
        w="100%"
        value={stdNumber}
        disabled={disabled}
        onChange={(event) => setStdNumber(event.currentTarget.value)}
      />
      <Select
        required
        label="年级"
        placeholder="请选择年级"
        data={GRADE_OPTIONS}
        value={grade}
        disabled={disabled}
        w="100%"
        onChange={setGrade}
      />
      <Captcha action="register" ref={captchaRef} />
      <Anchor fz="xs" className={misc.alignSelfEnd} component={Link} to="/account/login">
        {t('account.anchor.login')}
      </Anchor>
      <Button type="submit" fullWidth onClick={onRegister} disabled={disabled}>
        {t('account.button.register')}
      </Button>
    </AccountView>
  )
}

export default Register
