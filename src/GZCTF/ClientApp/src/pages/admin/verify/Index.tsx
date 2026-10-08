import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Center,
  Checkbox,
  Code,
  Group,
  Loader,
  Pagination,
  Paper,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { modals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiCheckAll, mdiClose, mdiMagnify, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import useSWR from 'swr'
import { AdminPage } from '@Components/admin/AdminPage'
import api, { VerifyItemModel, VerifyStatus } from '@Api'

const PAGE_SIZE = 50

/// 审核状态展示配置
const statusMeta: Record<VerifyStatus, { label: string; color: string }> = {
  [VerifyStatus.None]: { label: '未提交', color: 'gray' },
  [VerifyStatus.Pending]: { label: '待审核', color: 'orange' },
  [VerifyStatus.Approved]: { label: '已通过', color: 'teal' },
  [VerifyStatus.Rejected]: { label: '已驳回', color: 'red' },
}

const formatTime = (value?: string | null) => {
  if (!value) return '-'
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return '-'
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ` +
    `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

const VerifyPage: FC = () => {
  // 筛选条件
  const [school, setSchool] = useState<string | null>(null)
  const [status, setStatus] = useState<VerifyStatus>(VerifyStatus.Pending)
  const [hint, setHint] = useState('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [acting, setActing] = useState(false)

  const query = `school=${school ?? ''}&status=${status}&skip=${(page - 1) * PAGE_SIZE}&count=${PAGE_SIZE}&hint=${encodeURIComponent(hint)}`

  const { data, isLoading, mutate } = useSWR(
    `/api/verify/list?${query}`,
    async () => (await api.verify.verifyList({
      school: school ?? undefined,
      status,
      skip: (page - 1) * PAGE_SIZE,
      count: PAGE_SIZE,
      hint: hint || undefined,
    })).data,
    { revalidateOnFocus: false, keepPreviousData: true }
  )

  const items: VerifyItemModel[] = data?.items ?? []
  const total = data?.total ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  /// 学校下拉：系统管理员可筛选全部，学校管理员只能看到自己负责的学校
  const schoolOptions = useMemo(
    () => (data?.availableSchools ?? []).map((s) => ({ value: s.slug, label: `${s.name}（${s.slug}）` })),
    [data]
  )

  const statusOptions = [
    { value: VerifyStatus.Pending, label: '待审核' },
    { value: VerifyStatus.Approved, label: '已通过' },
    { value: VerifyStatus.Rejected, label: '已驳回' },
    { value: VerifyStatus.None, label: '未提交' },
  ]

  const allSelected = items.length > 0 && items.every((i) => i.userId && selected.has(i.userId))

  const toggleAll = () => {
    if (allSelected) {
      setSelected(new Set())
    } else {
      setSelected(new Set(items.map((i) => i.userId!).filter(Boolean)))
    }
  }

  const toggleOne = (id?: string) => {
    if (!id) return
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const refresh = async () => {
    setSelected(new Set())
    await mutate()
  }

  const approve = async (item: VerifyItemModel) => {
    if (!item.userId) return
    setActing(true)
    try {
      const res = await api.verify.verifyApprove({ userId: item.userId })
      showNotification({
        color: 'teal',
        message: res.data.title ?? '已通过',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      await refresh()
    } catch (err: any) {
      showNotification({
        color: 'red',
        message: err?.response?.data?.title ?? '操作失败',
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setActing(false)
    }
  }

  const reject = (item: VerifyItemModel) => {
    if (!item.userId) return

    let reason = ''
    modals.openConfirmModal({
      title: `驳回 ${item.realName ?? item.userName} 的学籍信息`,
      children: (
        <Stack gap="xs">
          <Text size="sm" c="dimmed">
            请填写驳回原因，学生将看到该说明。
          </Text>
          <TextInput
            label="驳回原因"
            placeholder="如：学号与姓名不匹配"
            onChange={(e) => {
              reason = e.currentTarget.value
            }}
          />
        </Stack>
      ),
      labels: { confirm: '确认驳回', cancel: '取消' },
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        setActing(true)
        try {
          const res = await api.verify.verifyReject({ userId: item.userId!, note: reason || undefined })
          showNotification({
            color: 'teal',
            message: res.data.title ?? '已驳回',
            icon: <Icon path={mdiCheck} size={1} />,
          })
          await refresh()
        } catch (err: any) {
          showNotification({
            color: 'red',
            message: err?.response?.data?.title ?? '操作失败',
            icon: <Icon path={mdiClose} size={1} />,
          })
        } finally {
          setActing(false)
        }
      },
    })
  }

  const batchApprove = async () => {
    const ids = Array.from(selected)
    if (ids.length === 0) return

    setActing(true)
    try {
      const res = await api.verify.verifyBatchApprove({ userIds: ids })
      showNotification({
        color: 'teal',
        message: res.data.title ?? `已批量通过 ${ids.length} 人`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
      await refresh()
    } catch (err: any) {
      showNotification({
        color: 'red',
        message: err?.response?.data?.title ?? '批量操作失败',
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setActing(false)
    }
  }

  return (
    <AdminPage isLoading={isLoading && items.length === 0}>
      <Group justify="space-between" wrap="wrap">
        <Box>
          <Title order={2}>学籍审核</Title>
          <Text size="sm" c="dimmed">
            {data?.manageAll
              ? '你可以审核全部学校的学生。主办赛道报名要求学生学籍信息审核通过。'
              : '你只能审核你负责学校的学生。主办赛道报名要求学生学籍信息审核通过。'}
          </Text>
        </Box>
        <Group gap="xs">
          <Button
            variant="light"
            leftSection={<Icon path={mdiCheckAll} size={1} />}
            disabled={selected.size === 0 || acting}
            loading={acting}
            onClick={batchApprove}
          >
            批量通过{selected.size > 0 ? `（${selected.size}）` : ''}
          </Button>
          <ActionIcon variant="light" size="lg" onClick={refresh} loading={isLoading}>
            <Icon path={mdiRefresh} size={1} />
          </ActionIcon>
        </Group>
      </Group>

      {/* 筛选区 */}
      <Paper withBorder p="md">
        <Group align="flex-end" wrap="wrap">
          <Select
            label="学校"
            placeholder="全部学校"
            data={schoolOptions}
            value={school}
            clearable
            searchable
            w={260}
            disabled={!data?.manageAll && schoolOptions.length <= 1}
            onChange={(v) => {
              setSchool(v)
              setPage(1)
              setSelected(new Set())
            }}
          />
          <Select
            label="审核状态"
            data={statusOptions}
            value={status}
            w={160}
            allowDeselect={false}
            onChange={(v) => {
              setStatus((v as VerifyStatus) ?? VerifyStatus.Pending)
              setPage(1)
              setSelected(new Set())
            }}
          />
          <TextInput
            label="搜索"
            placeholder="用户名 / 姓名 / 学号"
            leftSection={<Icon path={mdiMagnify} size={0.9} />}
            value={hint}
            w={220}
            onChange={(e) => {
              setHint(e.currentTarget.value)
              setPage(1)
            }}
          />
          <Badge size="lg" variant="light">
            共 {total} 条
          </Badge>
        </Group>
      </Paper>

      {/* 列表 */}
      <Paper withBorder p="md">
        {isLoading && items.length === 0 ? (
          <Center py="xl">
            <Loader />
          </Center>
        ) : items.length === 0 ? (
          <Center py="xl">
            <Stack align="center" gap={4}>
              <Icon path={mdiCheck} size={1.8} />
              <Text c="dimmed">没有符合条件的记录</Text>
            </Stack>
          </Center>
        ) : (
          <Table.ScrollContainer minWidth={900}>
            <Table verticalSpacing="sm" highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w={40}>
                    <Checkbox
                      checked={allSelected}
                      indeterminate={selected.size > 0 && !allSelected}
                      onChange={toggleAll}
                      aria-label="全选"
                    />
                  </Table.Th>
                  <Table.Th>学生</Table.Th>
                  <Table.Th>学校</Table.Th>
                  <Table.Th>年级</Table.Th>
                  <Table.Th>学号</Table.Th>
                  <Table.Th>状态</Table.Th>
                  <Table.Th>注册时间</Table.Th>
                  <Table.Th w={180}>操作</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.map((item) => {
                  const meta = statusMeta[item.status ?? VerifyStatus.None]
                  return (
                    <Table.Tr key={item.userId}>
                      <Table.Td>
                        <Checkbox
                          checked={Boolean(item.userId && selected.has(item.userId))}
                          onChange={() => toggleOne(item.userId)}
                          disabled={item.status !== VerifyStatus.Pending}
                          aria-label="选择"
                        />
                      </Table.Td>
                      <Table.Td>
                        <Stack gap={0}>
                          <Text fw={500} size="sm">
                            {item.realName || '(未填姓名)'}
                          </Text>
                          <Text size="xs" c="dimmed">
                            {item.userName}
                          </Text>
                          <Text size="xs" c="dimmed">
                            {item.email}
                          </Text>
                        </Stack>
                      </Table.Td>
                      <Table.Td>
                        <Badge variant="light">{item.schoolName ?? item.school ?? '-'}</Badge>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm">{item.grade ?? '-'}</Text>
                      </Table.Td>
                      <Table.Td>
                        <Code>{item.stdNumber || '-'}</Code>
                      </Table.Td>
                      <Table.Td>
                        <Stack gap={2}>
                          <Badge color={meta.color} variant="light">
                            {meta.label}
                          </Badge>
                          {item.note && (
                            <Tooltip label={item.note} multiline w={260}>
                              <Text size="xs" c="dimmed" lineClamp={1} style={{ maxWidth: 120 }}>
                                {item.note}
                              </Text>
                            </Tooltip>
                          )}
                        </Stack>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs" c="dimmed">
                          {formatTime(item.registerTimeUtc)}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Group gap="xs">
                          <Button
                            size="compact-sm"
                            variant="light"
                            color="teal"
                            disabled={acting || item.status !== VerifyStatus.Pending}
                            onClick={() => approve(item)}
                          >
                            通过
                          </Button>
                          <Button
                            size="compact-sm"
                            variant="light"
                            color="red"
                            disabled={acting || item.status !== VerifyStatus.Pending}
                            onClick={() => reject(item)}
                          >
                            驳回
                          </Button>
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}

        {pageCount > 1 && (
          <Group justify="center" mt="md">
            <Pagination
              value={page}
              total={pageCount}
              onChange={(p) => {
                setPage(p)
                setSelected(new Set())
              }}
            />
          </Group>
        )}
      </Paper>
    </AdminPage>
  )
}

export default VerifyPage
