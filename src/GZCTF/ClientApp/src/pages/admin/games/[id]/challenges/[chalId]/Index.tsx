import {
  Alert,
  Badge,
  Button,
  Code,
  ComboboxItem,
  Grid,
  Group,
  Input,
  NumberInput,
  Paper,
  Select,
  Slider,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
  Tabs,
  Title,
} from '@mantine/core'
import { DateTimePicker } from '@mantine/dates'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiContentSaveOutline,
  mdiDatabaseEditOutline,
  mdiDeleteOutline,
  mdiEyeOutline,
  mdiPackageVariantClosed,
  mdiPlus,
  mdiServerNetwork,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { HintList } from '@Components/HintList'
import { InstanceEntry } from '@Components/InstanceEntry'
import { ChallengePreviewModal } from '@Components/admin/ChallengePreviewModal'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { WithChallengeEdit } from '@Components/admin/WithChallengeEdit'
import { ScoreFunc } from '@Components/charts/ScoreFunc'
import { getInputNumber, NetworkModeItem, NetworkModeList, showErrorMsg, useNetworkModeMap } from '@Utils/Shared'
import {
  ChallengeCategoryItem,
  useChallengeCategoryLabelMap,
  ChallengeTypeItem,
  useChallengeTypeLabelMap,
  ChallengeCategoryList,
} from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { useEditChallenge, useEditChallenges } from '@Hooks/useEdit'
import { useGame } from '@Hooks/useGame'
import { useSyncOnChange } from '@Hooks/useSyncOnChange'
import api, {
  ChallengeCategory,
  ChallengeType,
  ChallengeUpdateModel,
  ExtraContainerImage,
  NetworkMode,
  TopologyEdge,
  TopologyNode,
} from '@Api'
import { TopologyEditor } from '@Components/admin/TopologyEditor'
import misc from '@Styles/Misc.module.css'

const GameChallengeEdit: FC = () => {
  const navigate = useNavigate()
  const { id, chalId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1'), parseInt(chalId ?? '-1')]

  const { game } = useGame(numId)
  const { challenge, mutate } = useEditChallenge(numId, numCId)
  const { challenges, mutate: mutateChals } = useEditChallenges(numId)

  const [challengeInfo, setChallengeInfo] = useState<ChallengeUpdateModel>({ ...challenge })
  const [deadline, setDeadline] = useState<dayjs.Dayjs | null>(
    challenge?.deadlineUtc ? dayjs(challenge?.deadlineUtc) : null
  )

  const [disabled, setDisabled] = useState(false)

  // 同方向交叉查看：可查看与测题，但表单只读
  const readOnly = challenge?.canEdit === false
  const locked = disabled || readOnly

  const [minRate, setMinRate] = useState((challenge?.minScoreRate ?? 0.25) * 100)
  const [category, setCategory] = useState<string | null>(challenge?.category ?? ChallengeCategory.Misc)
  const [networkMode, setNetworkMode] = useState<string | null>(challenge?.networkMode ?? NetworkMode.Open)
  const [type, setType] = useState<string | null>(challenge?.type ?? ChallengeType.StaticAttachment)
  const [currentAcceptCount, setCurrentAcceptCount] = useState(challenge?.acceptedCount ?? 0)
  const [previewOpened, setPreviewOpened] = useState(false)

  const modals = useModals()
  const challengeTypeLabelMap = useChallengeTypeLabelMap()
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const networkModeLabelMap = useNetworkModeMap()

  const { t } = useTranslation()

  const { config } = useConfig()

  /// 与服务端 Challenge.MaxExtraContainerImages 保持一致
  const MAX_EXTRA_IMAGES = 5

  /// 镜像地址前缀：平台统一提供，出题人只需填写镜像名（后缀）。
  /// 未配置前缀时输入框回退为完整地址模式。
  const imagePrefix = config?.containerImagePrefix ?? ''

  /// 附加靶机（单题多靶机）：一道题可同时启动多个互相配合的容器
  const extraImages = challengeInfo.extraContainerImages ?? []

  const setExtraImages = (next: ExtraContainerImage[]) =>
    setChallengeInfo({ ...challengeInfo, extraContainerImages: next })

  const addExtraImage = () =>
    setExtraImages([...extraImages, { name: '', image: '', exposePort: 80, enableTrafficCapture: false }])

  const updateExtraImage = (index: number, patch: Partial<ExtraContainerImage>) =>
    setExtraImages(extraImages.map((it, i) => (i === index ? { ...it, ...patch } : it)))

  const removeExtraImage = (index: number) => setExtraImages(extraImages.filter((_, i) => i !== index))

  /// 与服务端 Challenge.MaxTopologyNodes 保持一致
  const MAX_TOPOLOGY_NODES = 8

  /// 靶机拓扑（单题多靶机可视化编排）。
  /// 拓扑非空时，容器创建改以拓扑为准，附加靶机列表仅作兼容保留。
  const topologyNodes = challengeInfo.topologyNodes ?? []
  const topologyEdges = challengeInfo.topologyEdges ?? []

  const setTopology = (next: { nodes: TopologyNode[]; edges: TopologyEdge[] }) =>
    setChallengeInfo({ ...challengeInfo, topologyNodes: next.nodes, topologyEdges: next.edges })

  /// 容器编排视图：「拓扑」为图形化编排，「列表」为兼容的简单列表
  const [topoView, setTopoView] = useState<'topology' | 'list'>('topology')

  /// 靶机编排入口默认隐藏（当前赛事用不上单题多靶机）。
  /// 已配置过拓扑或附加靶机的题目仍然显示，避免历史题目无法维护；
  /// 需要全面开放时把 TOPOLOGY_UI_ENABLED 改为 true 即可。
  const TOPOLOGY_UI_ENABLED = false
  const showTopologyEditor = TOPOLOGY_UI_ENABLED || topologyNodes.length > 0 || extraImages.length > 0

  /// 已有拓扑编排的题目直接落到拓扑视图；否则先看简单列表，降低上手成本
  useEffect(() => {
    if (topologyNodes.length > 0) setTopoView('topology')
  }, [])
  
  /// 附加靶机的镜像名同样只填后缀，统一走前缀拼接
  const extraSuffix = (image: string) => {
    if (!imagePrefix) return image
    return image.startsWith(imagePrefix) ? image.slice(imagePrefix.length) : image
  }

  /// 输入框展示值：有前缀时只显示后缀部分
  const imageSuffix = useMemo(() => {
    const full = challengeInfo.containerImage ?? ''
    if (!imagePrefix) return full
    return full.startsWith(imagePrefix) ? full.slice(imagePrefix.length) : full
  }, [challengeInfo.containerImage, imagePrefix])

  /// 后缀变化时拼回完整地址
  const onImageSuffixChange = (suffix: string) => {
    const trimmed = suffix.trim()
    setChallengeInfo({
      ...challengeInfo,
      containerImage: imagePrefix ? `${imagePrefix}${trimmed}` : trimmed,
    })
  }

  useSyncOnChange([challenge], () => {
    if (challenge) {
      setChallengeInfo({ ...challenge })
      setCategory(challenge.category)
      setType(challenge.type)
      setMinRate((challenge?.minScoreRate ?? 0.25) * 100)
      setCurrentAcceptCount(challenge.acceptedCount)
      setDeadline(challenge.deadlineUtc ? dayjs(challenge.deadlineUtc) : null)
      setNetworkMode(challenge.networkMode ?? NetworkMode.Open)
    }
  })

  const onUpdate = async (challenge: ChallengeUpdateModel, noFeedback?: boolean) => {
    if (!challenge) return
    setDisabled(true)

    try {
      const res = await api.edit.editUpdateGameChallenge(numId, numCId, {
        ...challenge,
        deadlineUtc: deadline ? deadline.valueOf() : 0,
        isEnabled: undefined,
      })
      if (!noFeedback) {
        showNotification({
          color: 'teal',
          message: t('admin.notification.games.challenges.updated'),
          icon: <Icon path={mdiCheck} size={1} />,
        })
      }
      mutate(res.data)
      mutateChals()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      if (!noFeedback) {
        setDisabled(false)
      }
    }
  }

  const onConfirmDelete = async () => {
    setDisabled(true)

    try {
      await api.edit.editRemoveGameChallenge(numId, numCId)
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.challenges.deleted'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutateChals(
        challenges?.filter((chal) => chal.id !== numCId),
        { revalidate: false }
      )
      navigate(`/admin/games/${id}/challenges`)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onCreateTestContainer = async () => {
    // disabled by Toggle function

    try {
      const res = await api.edit.editCreateTestContainer(numId, numCId)
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.instances.created'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      if (challenge) {
        mutate({ ...challenge, testContainer: res.data })
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onDestroyTestContainer = async () => {
    // disabled by Toggle function

    try {
      await api.edit.editDestroyTestContainer(numId, numCId)
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.instances.deleted'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      if (challenge) {
        mutate({ ...challenge, testContainer: undefined })
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onToggleTestContainer = async () => {
    if (!challenge) return
    setDisabled(true)

    await onUpdate(
      {
        ...challengeInfo,
        category: category as ChallengeCategory,
        minScoreRate: minRate / 100,
      },
      true
    )

    if (challenge?.testContainer) {
      await onDestroyTestContainer()
    } else {
      await onCreateTestContainer()
    }
  }

  const tryDefault: <T>(values: T[], defaultValue?: NonNullable<T>) => NonNullable<T> | undefined = (vs, d) => {
    return vs.find((v) => !!v) ?? d
  }

  return (
    <WithChallengeEdit
      isLoading={!challenge}
      contentPos="space-between"
      backUrl={`/admin/games/${id}/challenges`}
      head={
        <>
          <Title lineClamp={1} className={misc.wordBreakAll}>
            # {challengeInfo?.title}
          </Title>
          <Group wrap="nowrap" justify="right">
            <Button
              disabled={locked}
              color="red"
              leftSection={<Icon path={mdiDeleteOutline} size={1} />}
              variant="outline"
              onClick={() =>
                modals.openConfirmModal({
                  title: t('admin.button.challenges.delete'),
                  children: (
                    <Text size="sm">
                      {t('admin.content.games.challenges.delete', {
                        name: challengeInfo?.title,
                      })}
                    </Text>
                  ),
                  onConfirm: () => onConfirmDelete(),
                  confirmProps: { color: 'red' },
                })
              }
            >
              {t('admin.button.challenges.delete')}
            </Button>
            <Button
              leftSection={<Icon path={mdiEyeOutline} size={1} />}
              onClick={() => setPreviewOpened(true)}
            >
              {t('admin.button.challenges.preview')}
            </Button>
            <Button
              disabled={locked}
              component={Link}
              leftSection={<Icon path={mdiDatabaseEditOutline} size={1} />}
              to={`/admin/games/${numId}/challenges/${numCId}/flags`}
            >
              {t('admin.button.challenges.edit_more')}
            </Button>
            <Button
              disabled={locked}
              leftSection={<Icon path={mdiContentSaveOutline} size={1} />}
              onClick={() =>
                onUpdate({
                  ...challengeInfo,
                  category: category as ChallengeCategory,
                  minScoreRate: minRate / 100,
                })
              }
            >
              {t('admin.button.save')}
            </Button>
          </Group>
        </>
      }
    >
      <Stack>
        {readOnly && (
          <Alert color="blue" variant="light" title="只读视角">
            该题目由
            {challenge?.authorName ? ` ${challenge.authorName} ` : '其他出题人'}
            创建，与你同属一个方向。你可以查看题目内容并启动测试容器，但不能修改配置、附件或
            Flag，也无法删除该题目。
          </Alert>
        )}
        <Grid columns={3}>
          <Grid.Col span={1}>
            <TextInput
              label={t('admin.content.games.challenges.title')}
              disabled={locked}
              value={challengeInfo.title ?? ''}
              required
              onChange={(e) => setChallengeInfo({ ...challengeInfo, title: e.target.value })}
            />
          </Grid.Col>
          <Grid.Col span={1}>
            <Select
              label={
                <Group gap="sm" wrap="nowrap">
                  <Text size="sm">{t('admin.content.games.challenges.type.label')}</Text>
                  <Text size="xs" c="dimmed">
                    {t('admin.content.games.challenges.type.description')}
                  </Text>
                </Group>
              }
              placeholder="Type"
              value={type}
              disabled={locked}
              readOnly
              renderOption={ChallengeTypeItem}
              data={Object.entries(ChallengeType).map((type) => {
                const data = challengeTypeLabelMap.get(type[1])
                return { value: type[1], label: data?.name, ...data } as ComboboxItem
              })}
            />
          </Grid.Col>
          <Grid.Col span={1}>
            <Select
              required
              label={t('admin.content.games.challenges.category')}
              placeholder="Category"
              value={category}
              disabled={locked}
              onChange={(e) => {
                setCategory(e)
                setChallengeInfo({ ...challengeInfo, category: e as ChallengeCategory })
              }}
              renderOption={ChallengeCategoryItem}
              data={ChallengeCategoryList.map((category) => {
                const data = challengeCategoryLabelMap.get(category)
                return { value: category, label: data?.name, ...data } as ComboboxItem
              })}
            />
          </Grid.Col>
          {game?.weekModeEnabled && (
            <Grid.Col span={1}>
              <Select
                label="所属周次"
                description="1-5 周受时间限制，挑战题/其他题不受限"
                placeholder="未归类"
                clearable
                disabled={locked}
                value={challengeInfo.week ? String(challengeInfo.week) : null}
                data={[
                  ...[1, 2, 3, 4, 5].map((week) => ({
                    value: String(week),
                    label: game?.weekBuckets?.find((b) => b.key === week)?.name || `第 ${week} 周`,
                  })),
                  {
                    value: '6',
                    label: game?.weekBuckets?.find((b) => b.key === 6)?.name || '挑战题',
                  },
                  {
                    value: '7',
                    label: game?.weekBuckets?.find((b) => b.key === 7)?.name || '其他题',
                  },
                ]}
                onChange={(value) => setChallengeInfo({ ...challengeInfo, week: value ? Number(value) : 0 })}
              />
            </Grid.Col>
          )}
          <Grid.Col span={2}>
            <Textarea
              w="100%"
              label={
                <Group gap="sm">
                  <Text size="sm">{t('admin.content.games.challenges.description')}</Text>
                  <Text size="xs" c="dimmed">
                    {t('admin.content.markdown_support')}
                  </Text>
                </Group>
              }
              value={challengeInfo?.content ?? ''}
              autosize
              disabled={locked}
              minRows={5}
              maxRows={5}
              onChange={(e) => setChallengeInfo({ ...challengeInfo, content: e.target.value })}
            />
          </Grid.Col>
          <Grid.Col span={1}>
            <Stack gap="0.425625rem">
              <NumberInput
                label={t('admin.content.games.challenges.submission_limit.label')}
                description={t('admin.content.games.challenges.submission_limit.description')}
                placeholder={t('admin.content.games.challenges.submission_limit.placeholder')}
                min={0}
                max={10000}
                disabled={locked}
                stepHoldDelay={500}
                stepHoldInterval={(t) => Math.max(1000 / t ** 2, 25)}
                value={challengeInfo?.submissionLimit || undefined}
                onChange={(e) => {
                  const number = getInputNumber(e)
                  if (isNaN(number)) return
                  setChallengeInfo({ ...challengeInfo, submissionLimit: number })
                }}
              />
              <DateTimePicker
                label={t('admin.content.games.challenges.deadline.label')}
                placeholder={t('admin.content.games.challenges.deadline.placeholder')}
                size="sm"
                value={deadline?.toDate()}
                valueFormat="L LT"
                disabled={locked}
                clearable
                onChange={(e) => {
                  setDeadline(e ? dayjs(e) : null)
                }}
              />
            </Stack>
          </Grid.Col>
          <Grid.Col span={1}>
            <Stack gap="sm">
              <HintList
                label={
                  <Group gap="sm">
                    <Text size="sm">{t('admin.content.games.challenges.hints')}</Text>
                    <Text size="xs" c="dimmed">
                      {t('admin.content.markdown_inline_support')}
                    </Text>
                  </Group>
                }
                hints={challengeInfo?.hints ?? []}
                disabled={locked}
                height={180}
                onChangeHint={(hints) => setChallengeInfo({ ...challengeInfo, hints })}
              />
            </Stack>
          </Grid.Col>
          <Grid.Col span={1}>
            <Stack h="100%">
              <Group wrap="nowrap">
                <NumberInput
                  label={t('admin.content.games.challenges.score')}
                  min={0}
                  required
                  disabled={locked}
                  stepHoldDelay={500}
                  stepHoldInterval={(t) => Math.max(1000 / t ** 2, 25)}
                  value={challengeInfo?.originalScore ?? 500}
                  onChange={(e) => {
                    const number = getInputNumber(e)
                    if (isNaN(number)) return
                    setChallengeInfo({ ...challengeInfo, originalScore: number })
                  }}
                />
                <NumberInput
                  label={t('admin.content.games.challenges.difficulty')}
                  decimalScale={2}
                  fixedDecimalScale
                  step={0.2}
                  min={0.1}
                  required
                  disabled={locked}
                  value={challengeInfo?.difficulty ?? 100}
                  stepHoldDelay={500}
                  stepHoldInterval={(t) => Math.max(1000 / t ** 2, 25)}
                  onChange={(e) => {
                    const number = getInputNumber(e, true)
                    if (isNaN(number)) return
                    setChallengeInfo({ ...challengeInfo, difficulty: number })
                  }}
                />
              </Group>
              <Input.Wrapper label={t('admin.content.games.challenges.min_score_radio.label')} h="3.8rem" required>
                <Slider
                  label={(value) =>
                    t('admin.content.games.challenges.min_score_radio.description', {
                      min_score: ((value / 100) * (challengeInfo?.originalScore ?? 500)).toFixed(0),
                    })
                  }
                  disabled={locked}
                  value={minRate}
                  marks={[
                    { value: 20, label: '20%' },
                    { value: 50, label: '50%' },
                    { value: 80, label: '80%' },
                  ]}
                  onChange={setMinRate}
                  classNames={{ label: misc.challEditLabel }}
                />
              </Input.Wrapper>
              <Switch
                disabled={locked}
                checked={!challengeInfo?.disableBloodBonus}
                label={SwitchLabel(
                  t('admin.content.games.challenges.blood_bonus.label'),
                  t('admin.content.games.challenges.blood_bonus.description')
                )}
                onChange={(e) => setChallengeInfo({ ...challengeInfo, disableBloodBonus: !e.target.checked })}
              />
            </Stack>
          </Grid.Col>
          <Grid.Col span={1}>
            <ScoreFunc
              currentAcceptCount={currentAcceptCount}
              originalScore={challengeInfo.originalScore ?? 500}
              minScoreRate={minRate / 100}
              difficulty={challengeInfo.difficulty ?? 30}
            />
          </Grid.Col>
        </Grid>
        {type === ChallengeType.DynamicAttachment && (
          <TextInput
            label={t('admin.content.games.challenges.attachment_name.label')}
            description={t('admin.content.games.challenges.attachment_name.description')}
            disabled={locked}
            value={challengeInfo.fileName ?? 'attachment'}
            onChange={(e) => setChallengeInfo({ ...challengeInfo, fileName: e.target.value })}
          />
        )}
        {(type === ChallengeType.StaticContainer || type === ChallengeType.DynamicContainer) && (
          <Grid columns={12}>
            <Grid.Col span={8}>
              <Group justify="space-between" align="flex-end">
                {imagePrefix ? (
                  <TextInput
                    label={t('admin.content.games.challenges.container_image')}
                    description={
                      <>
                        仅需填写镜像名，前缀固定为 <Code>{imagePrefix}</Code>
                      </>
                    }
                    disabled={locked}
                    value={imageSuffix}
                    required
                    leftSection={<Icon path={mdiPackageVariantClosed} size={0.9} />}
                    onChange={(e) => onImageSuffixChange(e.currentTarget.value)}
                    classNames={{ root: misc.flexGrow }}
                  />
                ) : (
                  <TextInput
                    label={t('admin.content.games.challenges.container_image')}
                    description="平台未配置镜像仓库，请填写完整镜像地址"
                    disabled={locked}
                    value={challengeInfo.containerImage ?? ''}
                    required
                    onChange={(e) => setChallengeInfo({ ...challengeInfo, containerImage: e.target.value })}
                    classNames={{ root: misc.flexGrow }}
                  />
                )}
                <NumberInput
                  label={t('admin.content.games.challenges.service_port.label')}
                  min={1}
                  max={65535}
                  w="8rem"
                  required
                  disabled={locked}
                  stepHoldDelay={500}
                  stepHoldInterval={(t) => Math.max(1000 / t ** 2, 25)}
                  value={challengeInfo.exposePort ?? 80}
                  onChange={(e) => {
                    const number = getInputNumber(e)
                    if (isNaN(number)) return
                    setChallengeInfo({ ...challengeInfo, exposePort: number })
                  }}
                />
                <Button
                  miw="8rem"
                  color={challenge?.testContainer ? 'orange' : 'green'}
                  disabled={disabled}
                  onClick={onToggleTestContainer}
                >
                  {challenge?.testContainer
                    ? t('admin.button.challenges.test_container.destroy')
                    : t('admin.button.challenges.test_container.create')}
                </Button>
              </Group>
            </Grid.Col>
            <Grid.Col span={4}>
              <InstanceEntry
                test
                label={`${challenge?.title} @ ${game?.title} (test)`}
                disabled={disabled}
                context={{
                  closeTime: challenge?.testContainer?.expectStopAt,
                  instanceEntry: challenge?.testContainer?.entry,
                }}
              />
            </Grid.Col>

            {/* 单题多靶机：靶机编排。默认隐藏，见 showTopologyEditor */}
            <Grid.Col span={12} style={{ display: showTopologyEditor ? undefined : 'none' }}>
              <Paper withBorder p="md" radius="md">
                <Tabs value={topoView} onChange={(v) => setTopoView((v as 'topology' | 'list') ?? 'topology')}>
                  <Group justify="space-between" align="flex-start" wrap="nowrap">
                    <div>
                      <Group gap="xs">
                        <Icon path={mdiServerNetwork} size={0.9} />
                        <Text fw={600}>靶机编排</Text>
                        <Badge size="sm" variant="light" color={topologyNodes.length > 0 ? 'teal' : 'gray'}>
                          {topologyNodes.length > 0
                            ? `${topologyNodes.length} 个靶机 / ${topologyEdges.length} 条连线`
                            : `${extraImages.length} / ${MAX_EXTRA_IMAGES} 附加靶机`}
                        </Badge>
                      </Group>
                      <Text size="xs" c="dimmed" mt={4}>
                        一道题可同时启动多个互相配合的容器（如 Web + 数据库）。选手启动实例时全部创建，
                        每个靶机有独立入口。
                        {topologyNodes.length > 0 && ' 已启用拓扑编排，容器按拓扑创建，连线仅作题面说明。'}
                      </Text>
                    </div>
                    <Tabs.List>
                      <Tabs.Tab value="topology">拓扑视图</Tabs.Tab>
                      <Tabs.Tab value="list">列表视图</Tabs.Tab>
                    </Tabs.List>
                  </Group>

                  <Tabs.Panel value="topology" pt="md">
                    <TopologyEditor
                      value={{ nodes: topologyNodes, edges: topologyEdges }}
                      onChange={setTopology}
                      imagePrefix={imagePrefix}
                    />
                  </Tabs.Panel>

                  <Tabs.Panel value="list" pt="md">
                    <Group justify="flex-end" mb={extraImages.length > 0 ? 'sm' : 0}>
                      <Button
                        variant="light"
                        size="xs"
                        leftSection={<Icon path={mdiPlus} size={0.8} />}
                        disabled={disabled || extraImages.length >= MAX_EXTRA_IMAGES}
                        onClick={addExtraImage}
                      >
                        添加靶机
                      </Button>
                    </Group>

                <Stack gap="sm">
                  {extraImages.map((item, index) => (
                    <Paper key={index} withBorder p="sm" radius="sm">
                      <Grid columns={12} align="flex-end">
                        <Grid.Col span={3}>
                          <TextInput
                            label="名称"
                            placeholder={`靶机 ${index + 2}`}
                            size="xs"
                            disabled={locked}
                            value={item.name ?? ''}
                            onChange={(e) => updateExtraImage(index, { name: e.currentTarget.value })}
                          />
                        </Grid.Col>
                        <Grid.Col span={5}>
                          <TextInput
                            label="镜像"
                            description={imagePrefix ? '只填镜像名，前缀同上' : '填写完整镜像地址'}
                            size="xs"
                            required
                            disabled={locked}
                            value={extraSuffix(item.image)}
                            onChange={(e) =>
                              updateExtraImage(index, {
                                image: imagePrefix
                                  ? `${imagePrefix}${e.currentTarget.value.trim()}`
                                  : e.currentTarget.value.trim(),
                              })
                            }
                          />
                        </Grid.Col>
                        <Grid.Col span={2}>
                          <NumberInput
                            label="端口"
                            size="xs"
                            min={1}
                            max={65535}
                            required
                            disabled={locked}
                            value={item.exposePort}
                            onChange={(e) => {
                              const number = getInputNumber(e)
                              if (isNaN(number)) return
                              updateExtraImage(index, { exposePort: number })
                            }}
                          />
                        </Grid.Col>
                        <Grid.Col span={2}>
                          <Button
                            fullWidth
                            variant="light"
                            color="red"
                            size="xs"
                            leftSection={<Icon path={mdiDeleteOutline} size={0.8} />}
                            disabled={locked}
                            onClick={() => removeExtraImage(index)}
                          >
                            移除
                          </Button>
                        </Grid.Col>
                      </Grid>
                    </Paper>
                  ))}
                    </Stack>
                  </Tabs.Panel>
                </Tabs>
              </Paper>
            </Grid.Col>
            <Grid.Col span={2}>
              <Select
                required
                label={t('admin.content.games.challenges.network_mode.label')}
                description={t('admin.content.games.challenges.network_mode.description')}
                value={networkMode ?? NetworkMode.Open}
                disabled={locked}
                onChange={(e) => {
                  setNetworkMode(e)
                  setChallengeInfo({ ...challengeInfo, networkMode: e as NetworkMode })
                }}
                renderOption={NetworkModeItem}
                data={NetworkModeList.map((mode) => {
                  const data = networkModeLabelMap.get(mode)
                  return { value: mode, ...data } as ComboboxItem
                })}
              />
            </Grid.Col>
            <Grid.Col span={2}>
              <NumberInput
                label={t('admin.content.games.challenges.cpu_limit.label')}
                description={t('admin.content.games.challenges.cpu_limit.description')}
                min={1}
                max={1024}
                required
                disabled={locked}
                stepHoldDelay={500}
                stepHoldInterval={(t) => Math.max(1000 / t ** 2, 25)}
                value={challengeInfo.cpuCount ?? 1}
                onChange={(e) => {
                  const number = getInputNumber(e)
                  if (isNaN(number)) return
                  setChallengeInfo({ ...challengeInfo, cpuCount: number })
                }}
              />
            </Grid.Col>
            <Grid.Col span={2}>
              <NumberInput
                label={t('admin.content.games.challenges.memory_limit.label')}
                description={t('admin.content.games.challenges.memory_limit.description')}
                min={32}
                max={1048576}
                required
                disabled={locked}
                stepHoldDelay={500}
                stepHoldInterval={(t) => Math.max(1000 / t ** 2, 25)}
                value={challengeInfo.memoryLimit ?? 32}
                onChange={(e) => {
                  const number = getInputNumber(e)
                  if (isNaN(number)) return
                  setChallengeInfo({ ...challengeInfo, memoryLimit: number })
                }}
              />
            </Grid.Col>
            <Grid.Col span={2}>
              <NumberInput
                label={t('admin.content.games.challenges.storage_limit.label')}
                description={t('admin.content.games.challenges.storage_limit.description')}
                min={0}
                max={1048576}
                required
                disabled={locked}
                stepHoldDelay={500}
                stepHoldInterval={(t) => Math.max(1000 / t ** 2, 25)}
                value={challengeInfo.storageLimit ?? 32}
                onChange={(e) => {
                  const number = getInputNumber(e)
                  if (isNaN(number)) return
                  setChallengeInfo({ ...challengeInfo, storageLimit: number })
                }}
              />
            </Grid.Col>
            <Grid.Col span={4} display="flex" className={misc.alignCenter}>
              <Switch
                disabled={locked}
                checked={challengeInfo.enableTrafficCapture ?? false}
                label={SwitchLabel(
                  t('admin.content.games.challenges.traffic_capture.label'),
                  t('admin.content.games.challenges.traffic_capture.description')
                )}
                onChange={(e) => setChallengeInfo({ ...challengeInfo, enableTrafficCapture: e.target.checked })}
              />
            </Grid.Col>
          </Grid>
        )}
      </Stack>
      <ChallengePreviewModal
        challenge={{
          title: tryDefault([challengeInfo?.title, challenge?.title], ''),
          content: tryDefault([challengeInfo?.content, challenge?.content]),
          hints: tryDefault([challengeInfo?.hints, challenge?.hints], []),
          score: tryDefault([challengeInfo?.originalScore, challenge?.originalScore], 0),
          limit: tryDefault([challengeInfo?.submissionLimit, challenge?.submissionLimit], 0),
          category: category as ChallengeCategory,
          deadline: deadline ? deadline.valueOf() : undefined,
          type: challenge?.type ?? ChallengeType.StaticAttachment,
        }}
        opened={previewOpened}
        onClose={() => setPreviewOpened(false)}
        cateData={challengeCategoryLabelMap.get(
          (challengeInfo?.category as ChallengeCategory) ?? ChallengeCategory.Misc
        )!}
      />
    </WithChallengeEdit>
  )
}

export default GameChallengeEdit
