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
import api, { ClientSsoProvider } from '@Api'
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

  // 后台配置的外部登录入口；未配置时不显示右栏
  const sso = config.sso
  const ssoProviders = (sso?.providers ?? []).filter((p) => p.title && p.link)

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

  const onSsoClick = (provider: ClientSsoProvider) => {
    const link = provider.link ?? ''
    if (!link) return

    if (provider.newWindow) window.open(link, '_blank', 'noopener,noreferrer')
    else window.location.href = link
  }

  // 后台未配置外部登录入口时，保持原有单栏布局
  if (ssoProviders.length === 0) {
    return <AccountView onSubmit={onLogin}>{localForm}</AccountView>
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

          {/* 右栏：外部登录（OA / 学校邮箱），内容由后台配置 */}
          <Box flex={stacked ? undefined : 1} maw={stacked ? undefined : 380}>
            <Stack align="center" justify="center" gap="md">
              <Title order={4} ta="center">
                {sso?.title}
              </Title>
              {sso?.description && (
                <Text size="sm" c="dimmed" ta="center">
                  {sso.description}
                </Text>
              )}
              <Stack w="100%" gap="sm" mt="xs">
                {ssoProviders.map((p) => (
                  <Button
                    key={p.provider ?? p.title}
                    fullWidth
                    variant="light"
                    disabled={disabled}
                    leftSection={
                      p.icon ? (
                        <Image src={p.icon} alt={p.title} w={18} h={18} radius="xl" />
                      ) : (
                        <Icon path={p.newWindow ? mdiOpenInNew : mdiLoginVariant} size={0.85} />
                      )
                    }
                    onClick={() => onSsoClick(p)}
                  >
                    {p.title}
                  </Button>
                ))}
              </Stack>
            </Stack>
          </Box>
        </Flex>
      </Paper>
    </Center>
  )
}

export default Login
