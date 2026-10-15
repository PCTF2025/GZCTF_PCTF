import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Card,
  Center,
  Divider,
  Group,
  Loader,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  TagsInput,
  Text,
  TextInput,
  Title,
  Tooltip,
  Code,
  CopyButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  mdiAccountMultipleOutline,
  mdiAlertCircleOutline,
  mdiCheckCircleOutline,
  mdiDeleteOutline,
  mdiDomain,
  mdiInformationOutline,
  mdiLockOutline,
  mdiPencilOutline,
  mdiContentCopy,
  mdiPlus,
  mdiRefresh,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useSyncOnChange } from '@Hooks/useSyncOnChange'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api, { ConfigEditModel, SchoolDetailModel, SchoolEditModel } from '@Api'
import { showErrorMsg } from '@Utils/Shared'

/// 空表单
const emptyForm = (): SchoolEditModel => ({
  name: '',
  authType: 'email',
  casLoginUrl: '',
  casValidateUrl: '',
  emailDomains: '',
  iconUrl: '',
  inviteCode: '',
  enabled: true,
  order: 0,
  freshmanGrades: '',
  seniorGrades: '',
  freshmanDivisionId: null,
  seniorDivisionId: null,
})

/// 逗号分隔字符串 ↔ 数组
const toList = (raw?: string | null) =>
  (raw ?? '')
    .split(/[,，;；\s]+/)
    .map((x) => x.trim())
    .filter(Boolean)

const toRaw = (list: string[]) => list.join(',')

const SchoolSettings: FC = () => {
  usePageTitle('学校设置')

  const { t } = useTranslation()
  const { data, isLoading, mutate } = api.school.useSchoolSettings()
  const schools = data?.schools ?? []
  const divisions = data?.divisions ?? []
  const grades = data?.grades ?? []

  const [editing, setEditing] = useState<SchoolDetailModel | null>(null)
  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState<SchoolEditModel>(emptyForm())
  const [saving, setSaving] = useState(false)
  const [inviteDraft, setInviteDraft] = useState<Record<string, string>>({})
  const [inviteSaving, setInviteSaving] = useState(false)

  // ===== 全局登录设置（原「登录设置」页功能）=====
  const { data: configs, mutate: mutateConfigs } = api.admin.useAdminGetConfigs(OnceSWRConfig)
  const [authEnabled, setAuthEnabled] = useState(true)
  const [localLoginEnabled, setLocalLoginEnabled] = useState(true)
  const [authTitle, setAuthTitle] = useState('')
  const [authDescription, setAuthDescription] = useState('')
  const [redirectPath, setRedirectPath] = useState('/')
  const [allowAutoRegister, setAllowAutoRegister] = useState(true)
  const [authSaving, setAuthSaving] = useState(false)

  useSyncOnChange([configs], () => {
    const sso = configs?.ssoConfig
    setAuthEnabled(sso?.enabled ?? true)
    setLocalLoginEnabled(sso?.localLoginEnabled ?? true)
    setAuthTitle(sso?.title ?? '')
    setAuthDescription(sso?.description ?? '')
    setRedirectPath(sso?.redirectPath ?? '/')
    setAllowAutoRegister(sso?.allowAutoRegister ?? true)
  })

  const saveAuthSettings = async () => {
    if (!configs) return
    setAuthSaving(true)
    try {
      const conf: ConfigEditModel = {
        ...configs,
        ssoConfig: {
          ...configs.ssoConfig,
          enabled: authEnabled,
          localLoginEnabled,
          title: authTitle.trim(),
          description: authDescription.trim() || null,
          redirectPath: redirectPath.trim() || '/',
          allowAutoRegister,
        },
      }
      await api.admin.adminUpdateConfigs(conf)
      await mutateConfigs()
      notifications.show({
        color: 'teal',
        title: '保存成功',
        message: '登录设置已保存',
        icon: <Icon path={mdiCheckCircleOutline} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setAuthSaving(false)
    }
  }

  const callbackUrl =
    typeof window === 'undefined' ? '' : `${window.location.origin}/api/account/sso/callback`

  const divisionOptions = useMemo(
    () =>
      divisions.map((d) => ({
        value: String(d.id),
        label: `${d.gameTitle ? `[${d.gameTitle}] ` : ''}${d.name}`,
      })),
    [divisions]
  )

  const openCreate = () => {
    setEditing(null)
    setForm(emptyForm())
    setCreating(true)
  }

  const openEdit = (s: SchoolDetailModel) => {
    setEditing(s)
    setForm({
      name: s.name,
      authType: s.authType,
      casLoginUrl: s.casLoginUrl ?? '',
      casValidateUrl: s.casValidateUrl ?? '',
      emailDomains: s.emailDomains ?? '',
      iconUrl: s.iconUrl ?? '',
      inviteCode: s.inviteCode ?? '',
      enabled: s.enabled,
      order: s.order,
      freshmanGrades: s.freshmanGrades ?? '',
      seniorGrades: s.seniorGrades ?? '',
      freshmanDivisionId: s.freshmanDivisionId ?? null,
      seniorDivisionId: s.seniorDivisionId ?? null,
    })
    setCreating(true)
  }

  const closeModal = () => {
    setCreating(false)
    setEditing(null)
    setForm(emptyForm())
  }

  const save = async () => {
    if (!form.name?.trim()) {
      notifications.show({ color: 'red', title: '保存失败', message: '请填写学校名称', icon: <Icon path={mdiAlertCircleOutline} /> })
      return
    }

    setSaving(true)
    try {
      const payload: SchoolEditModel = {
        ...form,
        casLoginUrl: form.casLoginUrl?.trim() || null,
        casValidateUrl: form.casValidateUrl?.trim() || null,
        emailDomains: form.emailDomains?.trim() || null,
        iconUrl: form.iconUrl?.trim() || null,
        inviteCode: form.inviteCode?.trim() || null,
        freshmanGrades: form.freshmanGrades?.trim() || '',
        seniorGrades: form.seniorGrades?.trim() || '',
      }

      const res = editing
        ? await api.school.schoolUpdate(editing.id, payload)
        : await api.school.schoolCreate(payload)

      if (res.status === 200) {
        notifications.show({
          color: 'teal',
          title: '保存成功',
          message: editing ? '学校已更新' : '学校已创建',
          icon: <Icon path={mdiCheckCircleOutline} />,
        })
        closeModal()
        await mutate()
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const remove = async (s: SchoolDetailModel) => {
    try {
      const res = await api.school.schoolDelete(s.id)
      if (res.status === 200) {
        notifications.show({ color: 'teal', title: '已删除', message: `已删除 ${s.name}`, icon: <Icon path={mdiCheckCircleOutline} /> })
        await mutate()
      }
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const saveInviteCodes = async () => {
    const entries = Object.entries(inviteDraft)
    if (entries.length === 0) {
      notifications.show({ color: 'gray', title: '无需保存', message: '没有需要保存的邀请码' })
      return
    }

    setInviteSaving(true)
    try {
      const res = await api.school.schoolInviteCodes(
        entries.map(([slug, inviteCode]) => ({ slug, inviteCode: inviteCode.trim() || null }))
      )
      if (res.status === 200) {
        notifications.show({
          color: 'teal',
          title: '保存成功',
          message: `已保存 ${entries.length} 所学校的邀请码`,
          icon: <Icon path={mdiCheckCircleOutline} />,
        })
        setInviteDraft({})
        await mutate()
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setInviteSaving(false)
    }
  }

  const groupSummary = (s: SchoolDetailModel) => {
    const parts: string[] = []
    const fresh = toList(s.freshmanGrades)
    const senior = toList(s.seniorGrades)

    if (fresh.length > 0)
      parts.push(`新生组：${fresh.join('/')}${s.freshmanDivisionId ? ` → ${divisionLabel(s.freshmanDivisionId)}` : '（未绑赛区）'}`)
    if (senior.length > 0)
      parts.push(`老生组：${senior.join('/')}${s.seniorDivisionId ? ` → ${divisionLabel(s.seniorDivisionId)}` : '（未绑赛区）'}`)

    return parts
  }

  const divisionLabel = (id?: number | null) =>
    id ? divisionOptions.find((d) => d.value === String(id))?.label ?? `赛区 #${id}` : '—'

  return (
    <AdminPage
      isLoading={isLoading}
      head={
        <Group justify="space-between" w="100%">
          <Group gap="xs">
            <Icon path={mdiDomain} size={0.9} />
            <Title order={4}>学校设置</Title>
          </Group>
          <Group gap="xs">
            <ActionIcon variant="subtle" onClick={() => void mutate()} title="刷新">
              <Icon path={mdiRefresh} size={0.9} />
            </ActionIcon>
            <Button leftSection={<Icon path={mdiPlus} size={0.9} />} onClick={openCreate}>
              新增学校
            </Button>
          </Group>
        </Group>
      }
    >
      <Stack gap="md">
        {/* ===== 登录方式 ===== */}
        <Paper withBorder p="md">
          <Stack gap="sm">
            <Group justify="space-between">
              <Title order={5}>登录方式</Title>
              <Button size="xs" loading={authSaving} onClick={() => void saveAuthSettings()}>
                保存登录设置
              </Button>
            </Group>
            <Switch
              checked={localLoginEnabled}
              onChange={(e) => setLocalLoginEnabled(e.currentTarget.checked)}
              label={SwitchLabel(
                '允许用户名 / 邮箱 + 密码登录',
                '关闭后登录页不再提供本地账号登录表单，参赛者只能通过学校统一身份认证登录。'
              )}
            />
            <Switch
              checked={authEnabled}
              onChange={(e) => setAuthEnabled(e.currentTarget.checked)}
              label={SwitchLabel(
                '启用学校统一身份认证',
                '启用后在登录页右侧展示学校列表；关闭后右侧区域整体隐藏。'
              )}
            />
          </Stack>
        </Paper>

        {/* ===== 登录页文案 ===== */}
        <Paper withBorder p="md">
          <Stack gap="sm">
            <Title order={5}>登录页文案</Title>
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <TextInput
                label="区域标题"
                placeholder="学校统一身份认证"
                value={authTitle}
                disabled={!authEnabled}
                onChange={(e) => setAuthTitle(e.currentTarget.value)}
              />
              <TextInput
                label="登录成功后跳转路径"
                placeholder="/"
                value={redirectPath}
                disabled={!authEnabled}
                onChange={(e) => setRedirectPath(e.currentTarget.value)}
              />
            </SimpleGrid>
            <TextInput
              label="区域说明"
              placeholder="选择所在学校，使用学校账号登录"
              value={authDescription}
              disabled={!authEnabled}
              onChange={(e) => setAuthDescription(e.currentTarget.value)}
            />
            <Switch
              checked={allowAutoRegister}
              onChange={(e) => setAllowAutoRegister(e.currentTarget.checked)}
              disabled={!authEnabled}
              label={SwitchLabel(
                '允许首次登录自动创建账号',
                '关闭后需要用户先注册并绑定学号，SSO 回调才能匹配到账号。'
              )}
            />
            <Divider label="CAS 对接信息" labelPosition="left" />
            <Text size="sm" c="dimmed">
              配置学校 CAS 侧「服务地址 / 回调地址」时填写以下地址。平台已在回调中附带学校名称。
            </Text>
            <Group gap="xs">
              <Code style={{ flex: 1, wordBreak: 'break-all' }}>{`${callbackUrl}?slug=<学校名称>`}</Code>
              <CopyButton value={callbackUrl}>
                {({ copied, copy }) => (
                  <Tooltip label={copied ? '已复制' : '复制回调地址'}>
                    <ActionIcon variant="subtle" onClick={copy}>
                      <Icon path={copied ? mdiCheckCircleOutline : mdiContentCopy} size={0.9} />
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

        <Paper withBorder p="md">
          <Stack gap="xs">
            <Title order={5}>学校与报名分组</Title>
            <Text size="sm" c="dimmed">
              学校是参赛单位：决定学生的归属、可用登录方式与报名邀请码；同时把年级划分为「新生组」「老生组」，
              每组绑定一个赛区 —— 报名主办赛道时系统会按学生的学校与年级自动归属对应赛区，选手无需自行选择。
            </Text>
          </Stack>
        </Paper>

        {schools.length === 0 ? (
          <Center mih={200}>
            <Stack align="center" gap="xs">
              <Text c="dimmed">还没有任何学校</Text>
              <Button variant="light" leftSection={<Icon path={mdiPlus} size={0.9} />} onClick={openCreate}>
                新增第一所学校
              </Button>
            </Stack>
          </Center>
        ) : (
          <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
            {schools.map((s) => (
              <Card key={s.id} withBorder p="md" opacity={s.enabled ? 1 : 0.6}>
                <Stack gap="xs">
                  <Group justify="space-between" wrap="nowrap">
                    <Group gap="xs" wrap="nowrap">
                      <Icon path={mdiDomain} size={0.8} />
                      <Text fw={600}>{s.name}</Text>
                      <Badge
                        size="sm"
                        variant="light"
                        color={s.authType === 'cas' ? 'blue' : s.authType === 'invite' ? 'cyan' : 'grape'}
                      >
                        {s.authType === 'cas' ? '统一认证' : s.authType === 'invite' ? '仅邀请码' : '邮箱验证码'}
                      </Badge>
                      {!s.enabled && (
                        <Badge size="sm" variant="light" color="gray">
                          已停用
                        </Badge>
                      )}
                    </Group>
                    <Group gap={4} wrap="nowrap">
                      <Tooltip label="编辑">
                        <ActionIcon variant="subtle" onClick={() => openEdit(s)}>
                          <Icon path={mdiPencilOutline} size={0.85} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label={s.userCount > 0 ? `已有 ${s.userCount} 名学生绑定，无法删除` : '删除'}>
                        <ActionIcon
                          variant="subtle"
                          color="red"
                          disabled={s.userCount > 0}
                          onClick={() => void remove(s)}
                        >
                          <Icon path={s.userCount > 0 ? mdiLockOutline : mdiDeleteOutline} size={0.85} />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Group>

                  <Group gap="xs">
                    <Badge size="xs" variant="outline">
                      {s.userCount} 名学生
                    </Badge>
                  </Group>

                  {s.emailDomains && (
                    <Text size="xs" c="dimmed">
                      邮箱后缀：{toList(s.emailDomains).join('、')}
                    </Text>
                  )}

                  <Divider />

                  <Stack gap={4}>
                    {groupSummary(s).length === 0 ? (
                      <Text size="xs" c="dimmed">
                        未配置报名分组（该校学生报名时需自行选择赛区）
                      </Text>
                    ) : (
                      groupSummary(s).map((line) => (
                        <Text key={line} size="xs">
                          {line}
                        </Text>
                      ))
                    )}
                  </Stack>

                  <Divider />

                  <Group gap="xs" wrap="nowrap">
                    <TextInput
                      size="xs"
                      label="报名邀请码"
                      placeholder="留空 = 免邀请码"
                      style={{ flex: 1 }}
                      value={inviteDraft[s.slug] ?? s.inviteCode ?? ''}
                      onChange={(e) =>
                        setInviteDraft((prev) => ({ ...prev, [s.slug]: e.currentTarget.value }))
                      }
                    />
                    {s.hasInviteCode && (
                      <Badge size="xs" mt={20} variant="light" color="teal">
                        已设置
                      </Badge>
                    )}
                  </Group>
                </Stack>
              </Card>
            ))}
          </SimpleGrid>
        )}

        {Object.keys(inviteDraft).length > 0 && (
          <Paper withBorder p="sm" style={{ position: 'sticky', bottom: 12 }}>
            <Group justify="space-between">
              <Text size="sm">
                已修改 {Object.keys(inviteDraft).length} 所学校的报名邀请码
              </Text>
              <Group gap="xs">
                <Button variant="subtle" onClick={() => setInviteDraft({})}>
                  放弃
                </Button>
                <Button loading={inviteSaving} onClick={() => void saveInviteCodes()}>
                  保存邀请码
                </Button>
              </Group>
            </Group>
          </Paper>
        )}
      </Stack>

      <Modal
        opened={creating}
        onClose={closeModal}
        title={editing ? `编辑学校 · ${editing.name}` : '新增学校'}
        size="xl"
      >
        <ScrollArea.Autosize mah="70vh">
          <Stack gap="sm">
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <TextInput
                label="学校名称"
                required
                placeholder="如：中国人民警察大学"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.currentTarget.value })}
              />
            </SimpleGrid>

            <Divider label="登录方式" labelPosition="left" />

            <Select
              label="认证模式"
              description="决定登录页展示方式：邮箱验证码需填写学校邮箱后缀；CAS 需填写对接地址；仅邀请码绑定则在登录页不出现该校，学生凭邀请码加入"
              data={[
                { value: 'email', label: '邮箱验证码（按学校邮箱后缀）' },
                { value: 'cas', label: 'CAS 统一身份认证' },
                { value: 'invite', label: '仅邀请码绑定（无专属登录入口）' },
              ]}
              value={form.authType ?? 'email'}
              onChange={(v) => setForm({ ...form, authType: v ?? 'email' })}
              allowDeselect={false}
            />

            {form.authType === 'invite' && (
              <Alert color="cyan" variant="light" icon={<Icon path={mdiInformationOutline} size={0.9} />}>
                该校不显示在登录页的学校列表中，学生使用站内账号注册 / 登录后，凭下方「报名邀请码」绑定学校归属。
                请务必在「报名设置」中设置邀请码。
              </Alert>
            )}

            {form.authType === 'email' && (
              <TagsInput
                label="允许的邮箱后缀"
                description="回车添加，如 cppu.edu.cn"
                value={toList(form.emailDomains)}
                onChange={(list) => setForm({ ...form, emailDomains: toRaw(list) })}
              />
            )}

            {form.authType === 'cas' && (
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput
                  label="CAS 登录地址"
                  placeholder="https://sso.example.edu.cn/cas/login"
                  value={form.casLoginUrl ?? ''}
                  onChange={(e) => setForm({ ...form, casLoginUrl: e.currentTarget.value })}
                />
                <TextInput
                  label="CAS 校验地址"
                  placeholder="https://sso.example.edu.cn/cas/serviceValidate"
                  value={form.casValidateUrl ?? ''}
                  onChange={(e) => setForm({ ...form, casValidateUrl: e.currentTarget.value })}
                />
              </SimpleGrid>
            )}

            <TextInput
              label="图标地址"
              description="可选，登录页学校卡片图标"
              value={form.iconUrl ?? ''}
              onChange={(e) => setForm({ ...form, iconUrl: e.currentTarget.value })}
            />

            <Divider label="报名分组" labelPosition="left" />

            <Text size="xs" c="dimmed">
              把年级分配到两个组，并分别绑定赛区。学生报名主办赛道时，系统按「学校 + 年级」自动进入对应赛区。
            </Text>

            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <Stack gap="xs">
                <TagsInput
                  label="新生组年级"
                  description="回车添加，如 大一"
                  value={toList(form.freshmanGrades)}
                  onChange={(list) => setForm({ ...form, freshmanGrades: toRaw(list) })}
                  data={grades}
                />
                <Select
                  label="新生组赛区"
                  placeholder="选择赛区"
                  clearable
                  data={divisionOptions}
                  value={form.freshmanDivisionId ? String(form.freshmanDivisionId) : null}
                  onChange={(v) => setForm({ ...form, freshmanDivisionId: v ? Number(v) : null })}
                />
              </Stack>

              <Stack gap="xs">
                <TagsInput
                  label="老生组年级"
                  description="回车添加，如 大二 / 大三 / 大四"
                  value={toList(form.seniorGrades)}
                  onChange={(list) => setForm({ ...form, seniorGrades: toRaw(list) })}
                  data={grades}
                />
                <Select
                  label="老生组赛区"
                  placeholder="选择赛区"
                  clearable
                  data={divisionOptions}
                  value={form.seniorDivisionId ? String(form.seniorDivisionId) : null}
                  onChange={(v) => setForm({ ...form, seniorDivisionId: v ? Number(v) : null })}
                />
              </Stack>
            </SimpleGrid>

            <Divider label="报名设置" labelPosition="left" />

            <TextInput
              label="报名邀请码"
              description="学生凭邀请码绑定本校；留空表示无需邀请码"
              value={form.inviteCode ?? ''}
              onChange={(e) => setForm({ ...form, inviteCode: e.currentTarget.value })}
            />

            <Group gap="lg">
              <Box>
                {SwitchLabel('启用该校', '停用后学生无法选择该校登录与绑定')}
                <Switch
                  mt={4}
                  checked={form.enabled ?? true}
                  onChange={(e) => setForm({ ...form, enabled: e.currentTarget.checked })}
                />
              </Box>
              <Box w={120}>
                <NumberInput
                  label="排序"
                  description="数值小的排前面"
                  value={form.order ?? 0}
                  onChange={(v) => setForm({ ...form, order: typeof v === 'number' ? v : 0 })}
                />
              </Box>
            </Group>
          </Stack>
        </ScrollArea.Autosize>

        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={closeModal}>
            取消
          </Button>
          <Button loading={saving} onClick={() => void save()}>
            {editing ? '保存修改' : '创建学校'}
          </Button>
        </Group>
      </Modal>
    </AdminPage>
  )
}

export default SchoolSettings
