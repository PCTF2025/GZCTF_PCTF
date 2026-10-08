import {
  ActionIcon,
  Box,
  Button,
  Card,
  Center,
  Group,
  Image,
  NumberInput,
  Paper,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  mdiArrowDown,
  mdiArrowUp,
  mdiCheck,
  mdiContentSaveOutline,
  mdiDeleteOutline,
  mdiImagePlusOutline,
  mdiPlus,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { usePageTitle } from '@Hooks/usePageTitle'
import api, { HomeBannerConfigModel, HomeBannerModel } from '@Api'
import { getInputNumber, showErrorMsg } from '@Utils/Shared'

const DEFAULT_INTERVAL = 5000

const SiteSettings: FC = () => {
  usePageTitle('站点设置')
  const { t } = useTranslation()

  const [config, setConfig] = useState<HomeBannerConfigModel>()
  const [banners, setBanners] = useState<HomeBannerModel[]>([])
  const [intervalMs, setIntervalMs] = useState(DEFAULT_INTERVAL)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const res = await api.banner.bannerGetAdmin()
      setConfig(res.data)
      // 按排序权重展示，管理端所见即前台顺序
      setBanners([...(res.data.banners ?? [])].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0)))
      setIntervalMs(res.data.intervalMs ?? DEFAULT_INTERVAL)
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
  }, [])

  const addBanner = () => {
    setBanners((current) => [
      ...current,
      {
        id: crypto.randomUUID(),
        title: '',
        imageUrl: '',
        linkUrl: '',
        enabled: true,
        sortOrder: current.length,
      },
    ])
  }

  const updateBanner = (id: string, patch: Partial<HomeBannerModel>) => {
    setBanners((current) => current.map((b) => (b.id === id ? { ...b, ...patch } : b)))
  }

  const removeBanner = (id: string) => {
    setBanners((current) => current.filter((b) => b.id !== id).map((b, index) => ({ ...b, sortOrder: index })))
  }

  const move = (index: number, delta: number) => {
    setBanners((current) => {
      const next = [...current]
      const target = index + delta
      if (target < 0 || target >= next.length) return current
      ;[next[index], next[target]] = [next[target], next[index]]
      // 重排后同步排序权重，保持与展示顺序一致
      return next.map((b, i) => ({ ...b, sortOrder: i }))
    })
  }

  const save = async () => {
    setSaving(true)
    try {
      await api.banner.bannerSaveAdmin({
        intervalMs,
        banners: banners.map((b, index) => ({ ...b, sortOrder: index })),
      })
      await load()
      notifications.show({
        color: 'teal',
        message: '站点设置已保存',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setSaving(false)
    }
  }

  return (
    <AdminPage
      isLoading={loading}
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
        <Title order={2}>站点设置</Title>

        <Paper withBorder p="md">
          <Stack>
            <Group justify="space-between" wrap="nowrap">
              <Box>
                <Title order={4}>首页轮播图</Title>
                <Text size="sm" c="dimmed">
                  展示在首页顶部的图片轮播，可配置图片地址与点击跳转链接。已关闭的条目前台不显示。
                </Text>
              </Box>
              <Button
                variant="light"
                leftSection={<Icon path={mdiPlus} size={1} />}
                onClick={addBanner}
              >
                添加轮播图
              </Button>
            </Group>

            <NumberInput
              label="自动播放间隔"
              description="单位：毫秒，范围 1000 ~ 60000。设为 0 由前台回落为默认值。"
              min={1000}
              max={60000}
              step={500}
              maw={260}
              value={intervalMs}
              onChange={(value) => {
                const num = getInputNumber(value)
                if (!Number.isNaN(num)) setIntervalMs(num)
              }}
            />

            {banners.length === 0 ? (
              <Center py="xl">
                <Text c="dimmed">暂无轮播图，点击右上角「添加轮播图」开始配置</Text>
              </Center>
            ) : (
              <Stack gap="md">
                {banners.map((banner, index) => (
                  <Card key={banner.id} withBorder p="md">
                    <Group align="flex-start" wrap="nowrap" gap="md">
                      {/* 预览区 */}
                      <Stack w={200} gap="xs">
                        {banner.imageUrl ? (
                          <Image
                            src={banner.imageUrl}
                            alt={banner.title ?? ''}
                            h={90}
                            radius="sm"
                            fit="cover"
                            fallbackSrc="https://placehold.co/200x90?text=Invalid+Image"
                          />
                        ) : (
                          <Center h={90} bg="var(--mantine-color-default-hover)" style={{ borderRadius: 4 }}>
                            <Icon path={mdiImagePlusOutline} size={1.6} />
                          </Center>
                        )}
                        <Group gap={4} justify="center">
                          <Tooltip label="上移">
                            <ActionIcon variant="subtle" disabled={index === 0} onClick={() => move(index, -1)}>
                              <Icon path={mdiArrowUp} size={0.9} />
                            </ActionIcon>
                          </Tooltip>
                          <Tooltip label="下移">
                            <ActionIcon
                              variant="subtle"
                              disabled={index === banners.length - 1}
                              onClick={() => move(index, 1)}
                            >
                              <Icon path={mdiArrowDown} size={0.9} />
                            </ActionIcon>
                          </Tooltip>
                          <Tooltip label="删除">
                            <ActionIcon color="red" variant="subtle" onClick={() => removeBanner(banner.id!)}>
                              <Icon path={mdiDeleteOutline} size={0.9} />
                            </ActionIcon>
                          </Tooltip>
                        </Group>
                      </Stack>

                      {/* 编辑区 */}
                      <Stack flex={1} gap="xs">
                        <SimpleGrid cols={{ base: 1, sm: 2 }}>
                          <TextInput
                            label="标题"
                            description="仅用于后台识别，前台不显示"
                            placeholder="例如：2026 校赛宣传图"
                            value={banner.title ?? ''}
                            onChange={(e) => updateBanner(banner.id!, { title: e.currentTarget.value })}
                          />
                          <TextInput
                            label="点击跳转链接"
                            description="留空则不可点击"
                            placeholder="https://example.com"
                            value={banner.linkUrl ?? ''}
                            onChange={(e) => updateBanner(banner.id!, { linkUrl: e.currentTarget.value })}
                          />
                        </SimpleGrid>
                        <TextInput
                          label="图片地址"
                          description="支持站内相对路径或完整 URL"
                          placeholder="/assets/xxx/banner.png 或 https://example.com/banner.png"
                          value={banner.imageUrl ?? ''}
                          onChange={(e) => updateBanner(banner.id!, { imageUrl: e.currentTarget.value })}
                        />
                        <Switch
                          checked={banner.enabled ?? true}
                          onChange={(e) => updateBanner(banner.id!, { enabled: e.currentTarget.checked })}
                          label={SwitchLabel('启用', '关闭后该轮播图不在前台展示')}
                        />
                      </Stack>
                    </Group>
                  </Card>
                ))}
              </Stack>
            )}
          </Stack>
        </Paper>

        <Paper withBorder p="md">
          <Stack gap="xs">
            <Title order={4}>外部链接</Title>
            <Text size="sm" c="dimmed">
              PCTF Wiki 地址固定在平台代码中，如需调整请修改前端常量。
            </Text>
            <TextInput label="Wiki 地址" value="https://wiki.pctf.top/" readOnly w={420} />
          </Stack>
        </Paper>
      </Stack>
    </AdminPage>
  )
}

export default SiteSettings
