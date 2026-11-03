import {
  Badge,
  Button,
  Group,
  Paper,
  PasswordInput,
  Stack,
  Switch,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiConnection, mdiContentSave } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { showErrorMsg } from '@Utils/Shared'
import api from '@Api'

/// 对象存储（OSS / S3 兼容）配置
const StorageSettings: FC = () => {
  const { t } = useTranslation()
  const { data, mutate } = api.storageSettings.useStorageSettings()

  const [endpoint, setEndpoint] = useState('')
  const [bucket, setBucket] = useState('')
  const [accessKeyId, setAccessKeyId] = useState('')
  const [accessKeySecret, setAccessKeySecret] = useState('')
  const [region, setRegion] = useState('')
  const [forcePathStyle, setForcePathStyle] = useState(false)
  const [hasSecret, setHasSecret] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!data) return
    setEndpoint(data.endpoint ?? '')
    setBucket(data.bucket ?? '')
    setAccessKeyId(data.accessKeyId ?? '')
    setRegion(data.region ?? '')
    setForcePathStyle(data.forcePathStyle ?? false)
    setHasSecret(data.hasSecret ?? false)
    setAccessKeySecret('')
  }, [data])

  /// 允许只填域名，自动补协议
  const normalizeEndpoint = (value: string) => {
    const v = value.trim()
    if (!v) return ''
    const withScheme = /^https?:\/\//i.test(v) ? v : `https://${v}`
    return withScheme.replace(/\/+$/, '')
  }

  const onSave = async () => {
    setBusy(true)
    try {
      await api.storageSettings.storageSettingsUpdate({
        endpoint: normalizeEndpoint(endpoint),
        bucket: bucket.trim(),
        accessKeyId: accessKeyId.trim(),
        region: region.trim(),
        forcePathStyle,
        // 留空表示不修改已有密钥
        accessKeySecret: accessKeySecret.trim(),
      })
      setAccessKeySecret('')
      await mutate()
      showNotification({
        color: 'teal',
        message: '对象存储配置已保存',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setBusy(false)
    }
  }

  const onTest = async () => {
    setBusy(true)
    try {
      const res = await api.storageSettings.storageSettingsTest()
      const ok = (res.data as { ok?: boolean; message?: string } | undefined)?.ok
      const message = (res.data as { ok?: boolean; message?: string } | undefined)?.message ?? ''
      showNotification({
        color: ok ? 'teal' : 'orange',
        message,
        icon: <Icon path={ok ? mdiCheck : mdiConnection} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setBusy(false)
    }
  }

  return (
    <AdminPage
      isLoading={!data}
      head={
        <Group gap="sm">
          <Title order={4}>对象存储</Title>
          <Badge variant="light" color={hasSecret ? 'teal' : 'gray'}>
            {hasSecret ? '密钥已配置' : '未配置密钥'}
          </Badge>
        </Group>
      }
    >
      <Stack w="100%" maw="42rem" gap="md">
        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Text size="sm" c="dimmed">
              用于存放靶机流量包等大文件。AccessKeySecret 保存后不会再回显，留空表示不修改。
            </Text>

            <TextInput
              label="Endpoint（OSS 站点）"
              description="只填域名也可以，会自动补 https://"
              placeholder="touful-waf-oss.oss-cn-hangzhou.aliyuncs.com"
              value={endpoint}
              onChange={(e) => setEndpoint(e.currentTarget.value)}
              onBlur={() => setEndpoint((v) => normalizeEndpoint(v))}
            />
            <TextInput
              label="Bucket"
              placeholder="pctf-traffic"
              value={bucket}
              onChange={(e) => setBucket(e.currentTarget.value)}
            />
            <TextInput
              label="AccessKeyId"
              placeholder="LTAI..."
              value={accessKeyId}
              onChange={(e) => setAccessKeyId(e.currentTarget.value)}
            />
            <PasswordInput
              label="AccessKeySecret"
              placeholder={hasSecret ? '已配置，留空则不修改' : '请输入 AccessKeySecret'}
              value={accessKeySecret}
              onChange={(e) => setAccessKeySecret(e.currentTarget.value)}
              autoComplete="new-password"
            />
            <TextInput
              label="Region（可选）"
              placeholder="cn-hangzhou"
              value={region}
              onChange={(e) => setRegion(e.currentTarget.value)}
            />
            <Switch
              checked={forcePathStyle}
              onChange={(e) => setForcePathStyle(e.currentTarget.checked)}
              label="使用路径风格（阿里云 OSS 请关闭；MinIO 等第三方通常需要开启）"
            />

            <Group justify="flex-end" mt="sm">
              <Button
                variant="light"
                leftSection={<Icon path={mdiConnection} size={0.9} />}
                loading={busy}
                onClick={onTest}
              >
                测试连接
              </Button>
              <Button
                leftSection={<Icon path={mdiContentSave} size={0.9} />}
                loading={busy}
                onClick={onSave}
              >
                保存
              </Button>
            </Group>
          </Stack>
        </Paper>
      </Stack>
    </AdminPage>
  )
}

export default StorageSettings
