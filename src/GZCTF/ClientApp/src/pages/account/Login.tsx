import {
  Anchor,
  Box,
  Button,
  Center,
  Divider,
  Flex,
  Grid,
  Group,
  Image,
  Paper,
  PasswordInput,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useInputState, useViewportSize } from '@mantine/hooks'
import { showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose, mdiLoginVariant, mdiOpenInNew } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { AccountView } from '@Components/AccountView'
import { Captcha, useCaptchaRef } from '@Components/Captcha'
import { encryptApiData } from '@Utils/Crypto'
import { tryGetClientError } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import api, { ClientSsoSchool, SsoAuthMode } from '@Api'
import misc from '@Styles/Misc.module.css'

const Login: FC = () => {
  const params = useSearchParams()[0]
  const navigate = useNavigate()

  const [pwd, setPwd] = useInputState('')
  const [uname, setUname] = useInputState('')
  const [disabled, setDisabled] = useState(false)
  const [needRedirect, setNeedRedirect] = useState(false)
  // guards against redirecting twice without triggering another render
  const redirecting = useRef(false)

  const { captchaRef, getToken, cleanUp } = useCaptchaRef()
  const { user, mutate } = useUser()
  const { config } = useConfig()

  const { t } = useTranslation()
  const { width } = useViewportSize()

  usePageTitle(t('account.title.login'))

  // 窄屏下分隔线改为横向，两栏上下堆叠
  const stacked = width < 900

  // 后台配置的学校列表；未配置时不显示右栏
  const sso = config.sso
  const ssoSchools = (sso?.schools ?? []).filter((s) => s.name && s.slug)

  // 右栏状态：学校列表 / 邮箱登录表单
  const [ssoMode, setSsoMode] = useState<'schools' | 'email'>('schools')
  const [emailSchool, setEmailSchool] = useState<ClientSsoSchool | null>(null)
  const [emailAccount, setEmailAccount] = useState('')
  const [pickedSuffix, setPickedSuffix] = useState('')

  useEffect(() => {
    if (needRedirect && user && !redirecting.current) {
      redirecting.current = true
      setTimeout(() => {
        navigate(params.get('from') ?? '/')
      }, 200)
    }
  }, [user, needRedirect])

  const onLogin = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    if (uname.length === 0 || pwd.length < 6) {
      showNotification({
        color: 'red',
        title: t('account.notification.login.invalid'),
        message: t('common.error.check_input'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      setDisabled(false)
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
      id: 'login-status',
      title: t('account.notification.captcha.request_sent.title'),
      message: t('account.notification.captcha.request_sent.message'),
      loading: true,
      autoClose: false,
    })

    try {
      await api.account.accountLogIn({
        userName: uname,
        password: await encryptApiData(t, pwd, config.apiPublicKey),
        challenge: token,
      })

      updateNotification({
        id: 'login-status',
        color: 'teal',
        title: t('account.notification.login.success.title'),
        message: t('account.notification.login.success.message'),
        icon: <Icon path={mdiCheck} size={1} />,
        autoClose: true,
        loading: false,
      })
      cleanUp(true)
      setNeedRedirect(true)
      mutate()
    } catch (err: any) {
      const { title, message } = tryGetClientError(err, t)
      updateNotification({
        id: 'login-status',
        color: 'red',
        title,
        message,
        icon: <Icon path={mdiClose} size={1} />,
        autoClose: true,
        loading: false,
      })
      cleanUp(false)
    } finally {
      setDisabled(false)
    }
  }

  const localForm = (
    <>
      <TextInput
        required
        label={t('account.label.username_or_email')}
        placeholder="ctfer"
        type="text"
        w="100%"
        value={uname}
        disabled={disabled}
        onChange={(event) => setUname(event.currentTarget.value)}
      />
      <PasswordInput
        required
        label={t('account.label.password')}
        id="your-password"
        placeholder="P4ssW@rd"
        w="100%"
        value={pwd}
        disabled={disabled}
        onChange={(event) => setPwd(event.currentTarget.value)}
      />
      <Captcha action="login" ref={captchaRef} />
      <Anchor fz="xs" className={misc.alignSelfEnd} component={Link} to="/account/recovery">
        {t('account.anchor.recovery')}
      </Anchor>
      <Grid grow w="100%">
        <Grid.Col span={2}>
          <Button fullWidth variant="outline" component={Link} to="/account/register">
            {t('account.button.register')}
          </Button>
        </Grid.Col>
        <Grid.Col span={2}>
          <Button fullWidth disabled={disabled} onClick={onLogin}>
            {t('account.button.login')}
          </Button>
        </Grid.Col>
      </Grid>
    </>
  )

  // 邮箱模式：先把学号拼成学校邮箱并校验归属，通过后跳注册页带邮箱预填
  const onEmailLogin = () => {
    if (!emailSchool) return

    const account = emailAccount.trim()

    if (!account) {
      showNotification({
        color: 'red',
        title: '请输入学号或邮箱',
        message: '学号将自动拼接为学校邮箱',
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    const email = account.includes('@') ? account : `${account}@${pickedSuffix}`

    // 归属校验：邮箱域必须属于该学校配置的后缀
    const suffixes = emailSchool.emailSuffixes ?? []
    const domain = email.slice(email.indexOf('@') + 1)
    const belongs =
      suffixes.length === 0 ||
      suffixes.some((s) => domain.toLowerCase() === s.toLowerCase() || domain.toLowerCase().endsWith(`.${s.toLowerCase()}`))

    if (!belongs) {
      showNotification({
        color: 'red',
        title: '邮箱不属于该校',
        message: `请使用 ${suffixes.map((s) => `@${s}`).join(' / ')} 邮箱`,
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    navigate(`/account/recovery?email=${encodeURIComponent(email)}`)
  }

  // 点击学校：CAS 模式跳转统一认证；邮箱模式就地切换到邮箱登录
  const onSchoolClick = (school: ClientSsoSchool) => {
    const slug = school.slug ?? ''

    if (school.mode === SsoAuthMode.Email) {
      setEmailSchool(school)
      setPickedSuffix(school.emailSuffixes?.[0] ?? '')
      setSsoMode('email')
      return
    }

    window.location.href = `/api/account/sso/login/${encodeURIComponent(slug)}`
  }

  /// 学校统一身份认证区域：列表模式与邮箱模式
  /// standalone = 登录页仅展示该区域时使用更大的标题
  const renderSsoPanel = (standalone = false) => (
    <Stack align="center" justify="center" gap="md">
      {ssoMode === 'schools' ? (
        <>
          <Title order={standalone ? 3 : 4} ta="center">
            {sso?.title || '学校统一身份认证'}
          </Title>
          {sso?.description && (
            <Text size="sm" c="dimmed" ta="center">
              {sso.description}
            </Text>
          )}
          <Stack w="100%" gap="sm" mt="xs">
            {ssoSchools.map((school) => (
              <Button
                key={school.slug ?? school.name}
                fullWidth
                variant="light"
                disabled={disabled}
                leftSection={
                  school.icon ? (
                    <Image src={school.icon} alt={school.name} w={18} h={18} radius="xl" />
                  ) : (
                    <Icon path={mdiLoginVariant} size={0.85} />
                  )
                }
                onClick={() => onSchoolClick(school)}
              >
                {school.name}
              </Button>
            ))}
          </Stack>
        </>
      ) : (
        <>
          <Title order={standalone ? 3 : 4} ta="center">
            {emailSchool?.name} 邮箱登录
          </Title>
          <Text size="sm" c="dimmed" ta="center">
            请使用学校邮箱登录
            {(emailSchool?.emailSuffixes?.length ?? 0) > 0 &&
              `（${emailSchool!.emailSuffixes!.map((s) => `@${s}`).join(' / ')}）`}
          </Text>
          <TextInput
            required
            label="学号 / 邮箱"
            placeholder="请输入学号或邮箱"
            w="100%"
            value={emailAccount}
            disabled={disabled}
            onChange={(event) => setEmailAccount(event.currentTarget.value)}
          />
          {pickedSuffix && (
            <Text size="xs" c="dimmed" ta="center" w="100%">
              登录邮箱：{emailAccount.includes('@') ? emailAccount : `${emailAccount || '学号'}@${pickedSuffix}`}
            </Text>
          )}
          <Group w="100%" grow>
            <Button
              variant="outline"
              onClick={() => {
                setSsoMode('schools')
                setEmailSchool(null)
                setEmailAccount('')
              }}
            >
              返回
            </Button>
            <Button disabled={disabled} onClick={onEmailLogin}>
              下一步
            </Button>
          </Group>
        </>
      )}
    </Stack>
  )

  // 后台未配置学校时，保持原有单栏布局
  // 三种布局：左右双栏 / 仅本地登录 / 仅学校认证
  const localEnabled = sso?.localLoginEnabled ?? true

  if (ssoSchools.length === 0) {
    return <AccountView onSubmit={onLogin}>{localEnabled ? localForm : <Text c="dimmed">未开放任何登录方式，请联系管理员</Text>}</AccountView>
  }

  if (!localEnabled) {
    return (
      <Center mih="100vh" p="md">
        <Paper w="100%" maw={520} p="xl" withBorder radius="md">
          {renderSsoPanel(true)}
        </Paper>
      </Center>
    )
  }

  return (
    <Center mih="100vh" p="md">
      <Paper w="100%" maw={920} p="xl" withBorder radius="md">
        <Flex
          direction={stacked ? 'column' : 'row'}
          gap="xl"
          align={stacked ? 'stretch' : 'center'}
          justify="center"
        >
          {/* 左栏：账号密码登录 */}
          <Box flex={stacked ? undefined : 1} maw={stacked ? undefined : 380}>
            <form className={misc.accountForm} onSubmit={onLogin} style={{ width: '100%' }}>
              <Stack gap="xs" align="center" justify="center">
                {localForm}
              </Stack>
            </form>
          </Box>

          {/* 中间分隔线：宽屏竖线，窄屏横线 */}
          <Divider
            orientation={stacked ? 'horizontal' : 'vertical'}
            size="sm"
            style={stacked ? { width: '100%' } : { alignSelf: 'stretch', height: 'auto' }}
          />

          {/* 右栏：学校统一身份认证（列表由后台维护，支持 CAS / 邮箱两种模式） */}
          <Box flex={stacked ? undefined : 1} maw={stacked ? undefined : 380}>
            {renderSsoPanel()}
          </Box>
        </Flex>
      </Paper>
    </Center>
  )
}

export default Login
