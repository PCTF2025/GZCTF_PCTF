import {
  Alert,
  Anchor,
  Box,
  Button,
  Center,
  Divider,
  Flex,
  Grid,
  Group,
  Image,
  PasswordInput,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useInputState, useViewportSize } from '@mantine/hooks'
import { showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose, mdiLoginVariant, mdiOpenInNew, mdiSchoolOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'
import logoImage from '@Resources/pctf-logo.png'
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

  // 来自学校邮箱认证的待绑定学校：优先取 URL 参数，其次取本地暂存
  const pendingSchool = useMemo(() => {
    const fromQuery = params.get('school')
    if (fromQuery) return fromQuery
    try {
      return window.localStorage.getItem('pctf.sso.pendingSchool') ?? ''
    } catch {
      return ''
    }
  }, [params])

  // 携带邮箱进入登录页时自动预填账号，减少一次输入
  useEffect(() => {
    const email = params.get('email')
    const stored = (() => {
      try {
        return window.localStorage.getItem('pctf.sso.pendingEmail') ?? ''
      } catch {
        return ''
      }
    })()

    const target = email || stored
    if (target && !uname) setUname(target)
  }, [params])

  const pendingSchoolName = useMemo(() => {
    if (!pendingSchool) return ''
    return ssoSchools.find((s) => s.slug === pendingSchool)?.name ?? ''
  }, [pendingSchool, ssoSchools])

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
        // 学校邮箱认证（快速登录）进入时携带学校短名，登录成功后由后端自动绑定学校
        schoolSlug: pendingSchool || undefined,
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

      // 登录成功，清理待绑定学校暂存，避免影响下次登录
      try {
        window.localStorage.removeItem('pctf.sso.pendingSchool')
        window.localStorage.removeItem('pctf.sso.pendingEmail')
      } catch {
        /* 忽略 */
      }
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
      {pendingSchool && (
        <Alert color="teal" variant="light" icon={<Icon path={mdiSchoolOutline} size={1} />}>
          <Text size="sm">
            登录成功后将自动绑定 <b>{pendingSchoolName || pendingSchool}</b>。
          </Text>
        </Alert>
      )}
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

  // 邮箱模式：先把学号拼成学校邮箱并校验归属，通过后跳登录 / 注册页（带邮箱与学校预填）。
  // 该学校会在登录成功（或注册完成）后自动绑定到账号。
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

    // 记录待绑定的学校，登录 / 注册成功后由后端自动绑定
    const slug = emailSchool.slug ?? ''
    try {
      window.localStorage.setItem('pctf.sso.pendingSchool', slug)
      window.localStorage.setItem('pctf.sso.pendingEmail', email)
    } catch {
      /* 隐私模式下 localStorage 不可用时忽略，登录后仍可手动绑定 */
    }

    navigate(`/account/login?school=${encodeURIComponent(slug)}&email=${encodeURIComponent(email)}`)
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

  // 未配置学校：单栏布局，同样使用图片 Logo
  if (ssoSchools.length === 0) {
    return (
      <Center mih="100vh" p="md">
        <Stack align="center" gap="xl" w="100%" maw={380}>
          <Image src={logoImage} alt="PCTF" maw={280} w="100%" h="auto" fit="contain" />
          {localEnabled ? (
            <form className={misc.accountForm} onSubmit={onLogin} style={{ width: '100%' }}>
              <Stack gap="xs" align="center" justify="center">
                {localForm}
              </Stack>
            </form>
          ) : (
            <Text c="dimmed">未开放任何登录方式，请联系管理员</Text>
          )}
        </Stack>
      </Center>
    )
  }

  // 仅学校认证：单栏居中
  if (!localEnabled) {
    return (
      <Center mih="100vh" p="md">
        <Stack align="center" gap="xl" w="100%" maw={520}>
          {/* 登录页 Logo：与首页同一张品牌图 */}
          <Image src={logoImage} alt="PCTF" maw={280} w="100%" h="auto" fit="contain" />
          {renderSsoPanel(true)}
        </Stack>
      </Center>
    )
  }

  return (
    <Center mih="100vh" p="md">
      <Stack align="center" gap="xl" w="100%" maw={920}>
        {/* 登录页顶部 Logo：与首页同一张品牌图 */}
        <Image src={logoImage} alt="PCTF" maw={280} w="100%" h="auto" fit="contain" />

        <Flex
          direction={stacked ? 'column' : 'row'}
          gap={stacked ? 'lg' : 'xl'}
          align="center"
          justify="center"
          w="100%"
        >
          {/* 左栏：账号密码登录（窄屏时整体居中） */}
          <Box flex={stacked ? undefined : 1} w={stacked ? '100%' : undefined} maw={stacked ? 380 : undefined}>
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
          <Box flex={stacked ? undefined : 1} w={stacked ? '100%' : undefined} maw={stacked ? 380 : undefined}>
            {renderSsoPanel()}
          </Box>
        </Flex>
      </Stack>
    </Center>
  )
}

export default Login
