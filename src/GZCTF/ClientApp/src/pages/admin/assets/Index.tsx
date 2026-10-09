import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Center,
  Code,
  CopyButton,
  Group,
  Image,
  Loader,
  Pagination,
  Paper,
  SimpleGrid,
  Stack,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { Dropzone } from '@mantine/dropzone'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiClose,
  mdiCloudUploadOutline,
  mdiContentCopy,
  mdiDeleteOutline,
  mdiFileImageOutline,
  mdiOpenInNew,
  mdiRefresh,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import useSWR from 'swr'
import { AdminPage } from '@Components/admin/AdminPage'
import api, { FileItemModel } from '@Api'

const PAGE_SIZE = 24

/// 图片扩展名，用于在列表中直接预览
const IMAGE_EXT = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp', 'svg', 'ico']

const isImage = (name: string) => {
  const ext = name.split('.').pop()?.toLowerCase() ?? ''
  return IMAGE_EXT.includes(ext)
}

const formatSize = (size: number) => {
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(2)} MB`
}

const formatTime = (value?: number | null) => {
  if (!value) return '-'
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return '-'
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ` +
    `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

const AssetsPage: FC = () => {
  const [page, setPage] = useState(1)
  const [uploading, setUploading] = useState(false)
  const [acting, setActing] = useState<string | null>(null)

  const { data, isLoading, mutate } = useSWR(
    `/api/assets/list?count=${PAGE_SIZE}&skip=${(page - 1) * PAGE_SIZE}`,
    async () => (await api.assets.assetsList({ count: PAGE_SIZE, skip: (page - 1) * PAGE_SIZE })).data,
    { revalidateOnFocus: false }
  )

  const files = data?.files ?? []
  const total = data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  /// 上传后自动跳回第一页，新文件按时间倒序展示
  const onDrop = async (accepted: File[]) => {
    if (accepted.length === 0) return

    setUploading(true)
    try {
      await api.assets.assetsUpload({ files: accepted })
      showNotification({
        color: 'teal',
        title: '上传成功',
        message: `已上传 ${accepted.length} 个文件，可复制内链使用`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setPage(1)
      await mutate()
    } catch {
      showNotification({
        color: 'red',
        title: '上传失败',
        message: '请检查文件大小与格式后重试',
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setUploading(false)
    }
  }

  const onDelete = async (file: FileItemModel) => {
    setActing(file.hash)
    try {
      await api.assets.assetsDelete(file.hash)
      showNotification({
        color: 'teal',
        message: `已删除 ${file.name}`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
      await mutate()
    } catch {
      showNotification({
        color: 'red',
        message: '删除失败：该文件可能仍被题目或公告引用',
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setActing(null)
    }
  }

  const markdown = useMemo(() => (file: FileItemModel) => `![${file.name}](${file.url})`, [])

  return (
    <AdminPage
      isLoading={isLoading}
      head={
        <Group justify="space-between" w="100%">
          <Group gap="xs">
            <Title order={2}>素材库</Title>
            <Badge variant="light">{total} 个文件</Badge>
          </Group>
          <Button
            variant="light"
            leftSection={<Icon path={mdiRefresh} size={0.9} />}
            onClick={() => void mutate()}
          >
            刷新
          </Button>
        </Group>
      }
    >
      <Stack gap="md">
        <Paper p="md" withBorder>
          <Dropzone
            onDrop={onDrop}
            loading={uploading}
            multiple
            maxSize={200 * 1024 ** 2}
            accept={undefined}
          >
            <Group justify="center" gap="xl" mih={140} style={{ pointerEvents: 'none' }}>
              <Dropzone.Accept>
                <Icon path={mdiCloudUploadOutline} size={3} />
              </Dropzone.Accept>
              <Dropzone.Reject>
                <Icon path={mdiClose} size={3} />
              </Dropzone.Reject>
              <Dropzone.Idle>
                <Icon path={mdiFileImageOutline} size={3} />
              </Dropzone.Idle>
              <Box>
                <Text size="lg" inline>
                  拖拽文件到此处，或点击选择
                </Text>
                <Text size="sm" c="dimmed" inline mt={7}>
                  支持图片与任意附件，单个文件不超过 200 MB；上传后可复制内链用于题目正文
                </Text>
              </Box>
            </Group>
          </Dropzone>
        </Paper>

        {files.length === 0 ? (
          <Center mih={200}>
            <Text c="dimmed">暂无文件，先上传一些图片或附件吧</Text>
          </Center>
        ) : (
          <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
            {files.map((file) => (
              <Paper key={file.hash} p="sm" withBorder>
                <Stack gap="xs">
                  {isImage(file.name) ? (
                    <Image
                      src={file.url}
                      alt={file.name}
                      h={140}
                      fit="contain"
                      radius="sm"
                      fallbackSrc="data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxIiBoZWlnaHQ9IjEiLz4="
                    />
                  ) : (
                    <Center h={140}>
                      <Icon path={mdiFileImageOutline} size={3} />
                    </Center>
                  )}

                  <Text size="sm" fw={500} lineClamp={1} title={file.name}>
                    {file.name}
                  </Text>

                  <Group gap="xs">
                    <Badge size="xs" variant="light" color="gray">
                      {formatSize(file.size)}
                    </Badge>
                    <Badge size="xs" variant="light" color={file.referenceCount > 0 ? 'teal' : 'gray'}>
                      引用 {file.referenceCount}
                    </Badge>
                  </Group>

                  <Text size="xs" c="dimmed">
                    {formatTime(file.uploadTimeUtc)}
                  </Text>

                  <Code block style={{ fontSize: '0.7rem', wordBreak: 'break-all' }}>
                    {file.url}
                  </Code>

                  <Group gap="xs" grow>
                    <CopyButton value={file.url} timeout={2000}>
                      {({ copied, copy }) => (
                        <Button
                          size="xs"
                          variant="light"
                          color={copied ? 'teal' : 'blue'}
                          leftSection={<Icon path={copied ? mdiCheck : mdiContentCopy} size={0.8} />}
                          onClick={copy}
                        >
                          {copied ? '已复制' : '内链'}
                        </Button>
                      )}
                    </CopyButton>

                    <CopyButton value={markdown(file)} timeout={2000}>
                      {({ copied, copy }) => (
                        <Tooltip label="复制 Markdown 图片语法">
                          <Button
                            size="xs"
                            variant="light"
                            color={copied ? 'teal' : 'grape'}
                            leftSection={<Icon path={copied ? mdiCheck : mdiContentCopy} size={0.8} />}
                            onClick={copy}
                          >
                            {copied ? '已复制' : 'MD'}
                          </Button>
                        </Tooltip>
                      )}
                    </CopyButton>

                    <Tooltip label="在新窗口打开">
                      <ActionIcon component="a" href={file.url} target="_blank" variant="light" size="input-xs">
                        <Icon path={mdiOpenInNew} size={0.8} />
                      </ActionIcon>
                    </Tooltip>

                    <Tooltip label="删除文件">
                      <ActionIcon
                        color="red"
                        variant="light"
                        size="input-xs"
                        loading={acting === file.hash}
                        onClick={() => void onDelete(file)}
                      >
                        <Icon path={mdiDeleteOutline} size={0.8} />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                </Stack>
              </Paper>
            ))}
          </SimpleGrid>
        )}

        {pages > 1 && (
          <Center>
            <Pagination value={page} onChange={setPage} total={pages} />
          </Center>
        )}

        {isLoading && (
          <Center>
            <Loader />
          </Center>
        )}
      </Stack>
    </AdminPage>
  )
}

export default AssetsPage
