import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Card,
  Center,
  Code,
  CopyButton,
  Divider,
  Group,
  Paper,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Switch,
  TagsInput,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  mdiAlertCircleOutline,
  mdiCheck,
  mdiContentCopy,
  mdiContentSaveOutline,
  mdiDeleteOutline,
  mdiPlus,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useSyncOnChange } from '@Hooks/useSyncOnChange'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api, { ConfigEditModel, SsoAuthMode } from '@Api'
import { showErrorMsg } from '@Utils/Shared'

/// 编辑态的学校行：字段与后端 CSV 一一对应
interface SchoolRow {
  key: string
  name: string
  slug: string
  mode: SsoAuthMode
  loginUrl: string
  validateUrl: string
  icon: string
  emailSuffixes: string[]
  enabled: boolean
}

const emptySchool = (index: number): SchoolRow => ({
  key: `new-${index}-${Date.now()}`,
  name: '',
  slug: '',
  mode: SsoAuthMode.Cas,
  loginUrl: '',
  validateUrl: '',
  icon: '',
  emailSuffixes: [],
  enabled: true,
})

/// 把后端 CSV 行解析为编辑态（字段顺序：名称|短名|模式|登录地址|校验地址|图标|邮箱后缀|启用）
const parseSchools = (raw?: string | null): SchoolRow[] => {
  if (!raw) return []

  return raw
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith('#'))
    .map((line, index) => {
      const parts = line.split('|')
      const at = (i: number) => (parts[i] ?? '').trim()
      return {
        key: `row-${index}-${at(1) || at(0)}`,
        name: at(0),
        slug: at(1),
        mode: at(2).toLowerCase() === 'email' ? SsoAuthMode.Email : SsoAuthMode.Cas,
        loginUrl: at(3),
        validateUrl: at(4),
        icon: at(5),
        emailSuffixes: at(6)
          ? at(6)
              .split(',')
              .map((s) => s.trim().replace(/^@/, ''))
              .filter(Boolean)
          : [],
        // 启用列留空视为启用
        enabled: at(7) === '' || at(7) === '1' || at(7).toLowerCase() === 'true',
      }
    })
}

/// 编辑态回写为后端 CSV
const serializeSchools = (rows: SchoolRow[]): string =>
  rows
    .filter((r) => r.name.trim().length > 0)
    .map((r) =>
      [
        r.name.trim(),
        r.slug.trim(),
        r.mode === SsoAuthMode.Email ? 'email' : 'cas',
        r.loginUrl.trim(),
        r.validateUrl.trim(),
        r.icon.trim(),
        r.emailSuffixes.map((s) => s.trim().replace(/^@/, '')).filter(Boolean).join(','),
        r.enabled ? '1' : '0',
      ].join('|')
    )
    .join('\n')

const LoginSettings: FC = () => {
  usePageTitle('登录设置')
  const { t } = useTranslation()

  const { data: configs, mutate } = api.admin.useAdminGetConfigs(OnceSWRConfig)

  const [enabled, setEnabled] = useState(true)
  const [localLoginEnabled, setLocalLoginEnabled] = useState(true)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [redirectPath, setRedirectPath] = useState('/')
  const [allowAutoRegister, setAllowAutoRegister] = useState(true)
  const [schools, setSchools] = useState<SchoolRow[]>([])
  const [schoolInviteCodes, setSchoolInviteCodes] = useState('')
  const [saving, setSaving] = useState(false)

  useSyncOnChange([configs], () => {
    const sso = configs?.ssoConfig
    if (!sso) return
    setEnabled(sso.enabled ?? true)
    setLocalLoginEnabled(sso.localLoginEnabled ?? true)
    setTitle(sso.title ?? '')
    setDescription(sso.description ?? '')
    setRedirectPath(sso.redirectPath ?? '/')
    setAllowAutoRegister(sso.allowAutoRegister ?? true)
    setSchools(parseSchools(sso.schools))
    setSchoolInviteCodes(sso.schoolInviteCodes ?? '')
  })

  useEffect(() => {
    const sso = configs?.ssoConfig
    if (!sso) return
    setEnabled(sso.enabled ?? true)
    setLocalLoginEnabled(sso.localLoginEnabled ?? true)
    setTitle(sso.title ?? '')
    setDescription(sso.description ?? '')
    setRedirectPath(sso.redirectPath ?? '/')
    setAllowAutoRegister(sso.allowAutoRegister ?? true)
    setSchools(parseSchools(sso.schools))
    setSchoolInviteCodes(sso.schoolInviteCodes ?? '')
  }, [configs])

  const update = (key: string, patch: Partial<SchoolRow>) =>
    setSchools((current) => current.map((row) => (row.key === key ? { ...row, ...patch } : row)))

  const remove = (key: string) => setSchools((current) => current.filter((row) => row.key !== key))

  const add = () => setSchools((current) => [...current, emptySchool(current.length)])

  const save = async () => {
    setSaving(true)
    try {
      const conf: ConfigEditModel = {
        ...configs,
        ssoConfig: {
          enabled,
          localLoginEnabled,
          title: title.trim(),
          description: description.trim() || null,
          redirectPath: redirectPath.trim() || '/',
          allowAutoRegister,
          schools: serializeSchools(schools),
          schoolInviteCodes: schoolInviteCodes.trim(),
        },
      }
      await api.admin.adminUpdateConfigs(conf)
      await mutate()
      notifications.show({
        color: 'teal',
        message: '登录设置已保存',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setSaving(false)
    }
  }

  const origin = typeof window === 'undefined' ? '' : window.location.origin
  const callbackUrl = `${origin}/api/account/sso/callback`

  const validSchools = schools.filter((s) => s.name.trim().length > 0)

  return (
    <AdminPage
      isLoading={!configs}
      head={
        <Button
          leftSection={<Icon path={mdiContentSaveOutline} size={1} />}
          loading={saving}
          onClick={save}
        >
          {t('admin.button.save')}
        </Button>
      }
    >
      <Stack>
        <Title order={2}>登录设置</Title>

        {/* 登录方式开关 */}
        <Paper withBorder p="md">
          <Stack>
            <Title order={4}>登录方式</Title>
            <Switch
              checked={localLoginEnabled}
              onChange={(e) => setLocalLoginEnabled(e.currentTarget.checked)}
              label={SwitchLabel(
                '允许用户名 / 邮箱 + 密码登录',
                '关闭后登录页不再提供本地账号登录表单，参赛者只能通过学校统一身份认证登录。'
              )}
            />
            <Switch
              checked={enabled}
              onChange={(e) => setEnabled(e.currentTarget.checked)}
              label={SwitchLabel(
                '启用学校统一身份认证',
                '启用后在登录页右侧展示学校列表；关闭后右侧区域整体隐藏。'
              )}
            />
          </Stack>
        </Paper>

        {/* 登录页文案 */}
        <Paper withBorder p="md">
          <Stack>
            <Title order={4}>登录页文案</Title>
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <TextInput
                label="区域标题"
                placeholder="学校统一身份认证"
                value={title}
                disabled={!enabled}
                onChange={(e) => setTitle(e.currentTarget.value)}
              />
              <TextInput
                label="登录成功后跳转路径"
                placeholder="/"
                value={redirectPath}
                disabled={!enabled}
                onChange={(e) => setRedirectPath(e.currentTarget.value)}
              />
            </SimpleGrid>
            <TextInput
              label="区域说明"
              placeholder="选择所在学校，使用学校账号登录"
              value={description}
              disabled={!enabled}
              onChange={(e) => setDescription(e.currentTarget.value)}
            />
            <Switch
              checked={allowAutoRegister}
              onChange={(e) => setAllowAutoRegister(e.currentTarget.checked)}
              disabled={!enabled}
              label={SwitchLabel(
                '允许首次登录自动创建账号',
                '关闭后需要用户先注册并绑定学号，SSO 回调才能匹配到账号。'
              )}
            />
          </Stack>
        </Paper>

        {/* 学校列表 */}
        <Paper withBorder p="md">
          <Stack>
            <Group justify="space-between" wrap="nowrap">
              <Box>
                <Title order={4}>学校列表</Title>
                <Text size="sm" c="dimmed">
                  每所学校一行。CAS 模式需填写登录地址；邮箱模式按邮箱后缀校验归属，纯学号也可通过。
                </Text>
              </Box>
              <Button variant="light" leftSection={<Icon path={mdiPlus} size={1} />} onClick={add} disabled={!enabled}>
                添加学校
              </Button>
            </Group>

            {validSchools.length === 0 ? (
              <Center py="xl">
                <Stack align="center" gap={4}>
                  <Icon path={mdiAlertCircleOutline} size={1.6} />
                  <Text c="dimmed">暂无学校，点击右上角「添加学校」开始配置</Text>
                </Stack>
              </Center>
            ) : (
              <Stack gap="md">
                {schools.map((school, index) => (
                  <Card key={school.key} withBorder p="md">
                    <Stack gap="sm">
                      <Group justify="space-between" wrap="nowrap">
                        <Group gap="xs">
                          <Badge variant="light">{`#${index + 1}`}</Badge>
                          <Text fw="bold">{school.name || '未命名学校'}</Text>
                        </Group>
                        <Tooltip label="删除该校">
                          <ActionIcon color="red" variant="subtle" onClick={() => remove(school.key)}>
                            <Icon path={mdiDeleteOutline} size={0.9} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>

                      <SimpleGrid cols={{ base: 1, sm: 3 }}>
                        <TextInput
                          label="学校名称"
                          placeholder="中国人民警察大学"
                          value={school.name}
                          required
                          onChange={(e) => update(school.key, { name: e.currentTarget.value })}
                        />
                        <TextInput
                          label="路由短名"
                          description="留空自动生成"
                          placeholder="cppu"
                          value={school.slug}
                          onChange={(e) => update(school.key, { slug: e.currentTarget.value })}
                        />
                        <Stack gap={4}>
                          <Text size="sm" fw={500}>
                            认证模式
                          </Text>
                          <SegmentedControl
                            size="xs"
                            value={school.mode}
                            onChange={(value) => update(school.key, { mode: value as SsoAuthMode })}
                            data={[
                              { label: 'CAS 统一认证', value: SsoAuthMode.Cas },
                              { label: '邮箱后缀', value: SsoAuthMode.Email },
                            ]}
                          />
                        </Stack>
                      </SimpleGrid>

                      {school.mode === SsoAuthMode.Cas ? (
                        <SimpleGrid cols={{ base: 1, sm: 2 }}>
                          <TextInput
                            label="CAS 登录地址"
                            placeholder="https://sso.example.edu.cn/tpass/login"
                            value={school.loginUrl}
                            onChange={(e) => update(school.key, { loginUrl: e.currentTarget.value })}
                          />
                          <TextInput
                            label="CAS 校验地址"
                            description="留空由登录地址推导"
                            placeholder="https://sso.example.edu.cn/tpass/serviceValidate"
                            value={school.validateUrl}
                            onChange={(e) => update(school.key, { validateUrl: e.currentTarget.value })}
                          />
                          <TextInput
                            label="图标 URL"
                            placeholder="https://example.edu.cn/logo.png"
                            value={school.icon}
                            onClick={(e) => e.stopPropagation()}
                            onChange={(e) => update(school.key, { icon: e.currentTarget.value })}
                          />
                          <TagsInput
                            label="邮箱后缀（可选）"
                            description="用于匹配 学号@后缀，回车确认"
                            placeholder="cppu.edu.cn"
                            value={school.emailSuffixes}
                            onChange={(value) => update(school.key, { emailSuffixes: value })}
                          />
                        </SimpleGrid>
                      ) : (
                        <SimpleGrid cols={{ base: 1, sm: 2 }}>
                          <TagsInput
                            label="邮箱后缀"
                            description="回车确认，可多个，如 demo.edu.cn、stu.demo.edu.cn"
                            placeholder="demo.edu.cn"
                            value={school.emailSuffixes}
                            onChange={(value) => update(school.key, { emailSuffixes: value })}
                          />
                          <TextInput
                            label="图标 URL"
                            placeholder="https://example.edu.cn/logo.png"
                            value={school.icon}
                            onChange={(e) => update(school.key, { icon: e.currentTarget.value })}
                          />
                        </SimpleGrid>
                      )}

                      <Switch
                        checked={school.enabled}
                        onChange={(e) => update(school.key, { enabled: e.currentTarget.checked })}
                        label={SwitchLabel('启用该校', '关闭后该校不出现在登录页')}
                      />

                      <Divider />
                      <Text size="xs" c="dimmed">
                        登录入口：
                        <Code>{`${origin}/api/account/sso/login/${school.slug || '(自动生成)'}`}</Code>
                      </Text>
                    </Stack>
                  </Card>
                ))}
              </Stack>
            )}
          </Stack>
        </Paper>

        {/* 学校报名邀请码 */}
        <Paper withBorder p="md">
          <Stack>
            <Box>
              <Title order={4}>学校报名邀请码</Title>
              <Text size="sm" c="dimmed">
                未使用快速登录（学校统一身份认证）的参赛者，需凭邀请码绑定学校后方可报名主办赛道。
                每所学校一行，格式为「学校短名:邀请码」；未列出的学校不校验邀请码。
              </Text>
            </Box>

            <Textarea
              label="邀请码列表"
              description="每行一条，例如：cppu:PCTF2026CPPU"
              placeholder={'cppu:PCTF2026CPPU\ndemo:PCTF2026DEMO'}
              value={schoolInviteCodes}
              disabled={!enabled}
              autosize
              minRows={3}
              maxRows={10}
              onChange={(e) => setSchoolInviteCodes(e.currentTarget.value)}
            />

            {validSchools.length > 0 && (
              <Stack gap={4}>
                <Text size="xs" c="dimmed">
                  已配置学校对应邀请码状态：
                </Text>
                {validSchools.map((s) => {
                  const matched = schoolInviteCodes
                    .split(/[,\n;]/)
                    .map((item) => item.trim())
                    .filter(Boolean)
                    .find((item) => item.split(':')[0]?.trim().toLowerCase() === s.slug.trim().toLowerCase())

                  return (
                    <Group key={s.key} gap="xs">
                      <Badge size="sm" variant="light">
                        {s.slug || '(无短名)'}
                      </Badge>
                      <Text size="xs" c={matched ? 'teal' : 'dimmed'}>
                        {matched ? `已设邀请码：${matched.split(':')[1] ?? ''}` : '未设邀请码（该校可免邀请码绑定）'}
                      </Text>
                    </Group>
                  )
                })}
              </Stack>
            )}
          </Stack>
        </Paper>

        {/* 对接信息 */}
        <Paper withBorder p="md">
          <Stack gap="xs">
            <Title order={4}>对接信息</Title>
            <Text size="sm" c="dimmed">
              配置学校 CAS 侧「服务地址 / 回调地址」时填写以下地址。平台已在回调中附带学校短名。
            </Text>
            <Group gap="xs">
              <Code style={{ flex: 1, wordBreak: 'break-all' }}>{`${callbackUrl}?slug=<学校短名>`}</Code>
              <CopyButton value={callbackUrl}>
                {({ copied, copy }) => (
                  <Tooltip label={copied ? '已复制' : '复制回调地址'}>
                    <ActionIcon variant="subtle" onClick={copy}>
                      <Icon path={copied ? mdiCheck : mdiContentCopy} size={0.9} />
                    </ActionIcon>
                  </Tooltip>
                )}
              </CopyButton>
            </Group>
            <Text size="xs" c="dimmed">
              校验方式：CAS 2.0。平台请求 <Code>{`<校验地址>?service=<回调地址>&ticket=<票据>`}</Code>
              ，解析返回的 cas:user 作为学号/账号。
            </Text>
          </Stack>
        </Paper>
      </Stack>
    </AdminPage>
  )
}

export default LoginSettings
