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
  const [grade, setGrade] = useState<string | null>(null)

  const navigate = useNavigate()
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

  const boundSchoolName = useMemo(() => {
    if (!schoolSlug) return ''
    return (config.sso?.schools ?? []).find((s) => s.slug === schoolSlug)?.name ?? ''
  }, [schoolSlug, config.sso])

  // 可选学校列表（来自后台 SSO 配置）
  const schoolOptions = useMemo(
    () =>
      (config.sso?.schools ?? [])
        .filter((s) => s.name && s.slug)
        .map((s) => ({ value: s.slug!, label: s.name! })),
    [config.sso]
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

    // 学籍信息为报名主办赛道的前提，注册时一并校验
    if (!effectiveSchool || !realName.trim() || !stdNumber.trim() || !grade) {
      showNotification({
        color: 'orange',
        title: '请完善学籍信息',
        message: '所属学校、真实姓名、学号、年级均为必填',
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

        if (res.data.data === RegisterStatus.LoggedIn) navigate('/')
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
        required
        label={t('account.label.username')}
        type="text"
        placeholder="ctfer"
        w="100%"
        value={uname}
        disabled={disabled}
        onChange={(event) => setUname(event.currentTarget.value)}
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
      <Select
        required
        label="所属学校"
        description="请选择你的学校，学籍信息将由该校管理员审核"
        placeholder="请选择学校"
        data={schoolOptions}
        value={pickedSchool}
        disabled={disabled || Boolean(schoolSlug)}
        searchable
        w="100%"
        onChange={setPickedSchool}
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
