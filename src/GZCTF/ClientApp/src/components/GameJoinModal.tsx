import { Alert, Button, Modal, ModalProps, Select, Stack, Text, TextInput } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiAccountOutline, mdiAlertCircleOutline, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { useGame } from '@Hooks/useGame'
import { useSyncOnChange } from '@Hooks/useSyncOnChange'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { GameJoinModel, DetailedGameInfoModel, VerifyStatus } from '@Api'

const defaultTeam = (teams?: { id?: number | null }[]) => (teams && teams.length >= 1 ? teams[0].id!.toString() : null)

const defaultDivisionId = (game?: DetailedGameInfoModel) => {
  if (typeof game?.division === 'number') return game.division.toString()
  if (game?.divisions && game.divisions.length >= 1 && !!game.divisions[0].id) return game.divisions[0].id.toString()
  return ''
}

interface GameJoinModalProps extends ModalProps {
  onSubmitJoin: (info: GameJoinModel) => Promise<void>
}

export const GameJoinModal: FC<GameJoinModalProps> = (props) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { onSubmitJoin, ...modalProps } = props

  const { teams } = useTeams()
  const { user } = useUser()
  const { game } = useGame(numId)

  const { data: checkInfo } = api.game.useGameGetGameJoinCheckInfo(numId, OnceSWRConfig, props.opened && numId > 0)

  const [inviteCode, setInviteCode] = useState('')
  const [divisionId, setDivisionId] = useState(() => defaultDivisionId(game))
  const [team, setTeam] = useState<string | null>(() => defaultTeam(teams))
  const [disabled, setDisabled] = useState(false)

  const { t } = useTranslation()

  /// 个人赛：队伍人数上限为 1 时，报名无需手动建队，
  /// 由后端自动创建个人队，前端隐藏队伍选择步骤。
  const isSolo = game?.limit === 1

  useSyncOnChange([team, teams], () => {
    if (!team) setTeam(defaultTeam(teams))
  })

  useSyncOnChange([divisionId, game], () => {
    if (!divisionId) setDivisionId(defaultDivisionId(game))
  })

  const divisionOptions = useMemo(
    () =>
      (game?.divisions ?? [])
        .filter((d) => d.id && (!checkInfo?.joinableDivisions || checkInfo.joinableDivisions.includes(d.id!)))
        .map((d) => ({
          value: d.id!.toString(),
          label: d.name ?? `Division #${d.id}`,
        })),
    [game?.divisions, checkInfo]
  )

  const gameCheckInfo = useMemo(() => {
    const map = new Map<string, { div: number | null; joinable?: boolean }>()
    checkInfo?.joinedTeams?.forEach((jt) => {
      map.set(jt.id.toString(), { div: jt.division, joinable: checkInfo.joinableDivisions?.includes(jt.division) })
    })
    return map
  }, [checkInfo])

  const selectedDivision = useMemo(
    () => (game?.divisions ?? []).find((d) => d.id?.toString() === divisionId) ?? null,
    [divisionId, game?.divisions]
  )

  const teamsData = useMemo(() => {
    return teams?.map((t) => ({ label: t.name!, value: t.id!.toString() })) ?? []
  }, [teams])

  const joinedTeam = team ? gameCheckInfo.get(team) : null
  const joinedDivision = joinedTeam?.div ? game?.divisions?.find((d) => d.id === joinedTeam?.div) : null
  const hasDivision = divisionOptions.length > 0 || joinedTeam?.div
  const canSelectDivision = !joinedTeam

  /// 个人赛按「学校 + 年级」自动匹配分组赛区。
  /// 走 /api/school/resolve（登录即可查自己的分组，无需管理权限），
  /// 由后端按学校设置的年级分组规则推导出赛区。
  /// 不传 userId：后端自动取当前登录用户，避免越权查询他人
  const { data: schoolResolve } = api.school.useSchoolResolve(
    undefined,
    OnceSWRConfig,
    Boolean(isSolo && user)
  )

  const autoDivisionId = useMemo(
    () => (schoolResolve?.resolved ? (schoolResolve.divisionId ?? null) : null),
    [schoolResolve]
  )

  /// 自动匹配到的赛区是否在可选列表中
  const autoDivisionAvailable = useMemo(
    () => Boolean(autoDivisionId && divisionOptions.some((o) => o.value === autoDivisionId.toString())),
    [autoDivisionId, divisionOptions]
  )

  // 命中自动分组时预选赛区；仅在用户尚未手动选择时生效
  useEffect(() => {
    if (isSolo && autoDivisionAvailable && autoDivisionId) {
      setDivisionId(autoDivisionId.toString())
    }
  }, [isSolo, autoDivisionAvailable, autoDivisionId])

  const shouldRequireInviteCode = hasDivision
    ? Boolean(selectedDivision?.inviteCodeRequired)
    : Boolean(game?.inviteCodeRequired)

  useSyncOnChange([shouldRequireInviteCode], () => {
    if (!shouldRequireInviteCode) {
      setInviteCode('')
    }
  })

  // 主办赛道要求真实姓名 + 学号 + 学校绑定，且学籍审核通过，缺项在报名前提示
  const profileIncomplete = useMemo(() => {
    const missing: string[] = []
    if (!user?.realName?.trim()) missing.push('真实姓名')
    if (!user?.stdNumber?.trim()) missing.push('学号')
    if (!user?.school?.trim()) missing.push('所属学校')
    return missing
  }, [user])

  // 资料齐全时，还要看学校管理员的学籍审核结果；
  // 平台本身可以正常使用（浏览赛题、组队），仅主办赛道报名受此限制
  const verifyBlocked = useMemo(() => {
    if (profileIncomplete.length > 0) return null
    switch (user?.verifyStatus) {
      case VerifyStatus.Approved:
        return null
      case VerifyStatus.Pending:
        return { color: 'orange', title: '学籍审核中', message: '你的学籍信息正在等待学校管理员审核，通过后即可报名主办赛道。平台其他功能不受影响。' }
      case VerifyStatus.Rejected:
        return {
          color: 'red',
          title: '学籍审核未通过',
          message: user.verifyNote
            ? `驳回原因：${user.verifyNote}。请前往「个人资料」页面修正后重新提交。`
            : '请前往「个人资料」页面检查学籍信息后重新提交。',
        }
      default:
        return { color: 'orange', title: '学籍待提交', message: '请前往「个人资料」页面补全学籍信息并提交审核。' }
    }
  }, [user, profileIncomplete])

  const onJoinGame = async () => {
    setDisabled(true)

    // 个人赛无需选择队伍（由后端自动创建），跳过该检查
    if (!isSolo && !team) {
      showNotification({
        color: 'orange',
        message: t('game.notification.no_team'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      setDisabled(false)
      return
    }

    // 主办赛道报名前需完善个人信息：真实姓名 + 学号 + 学校绑定
    if (profileIncomplete.length > 0) {
      showNotification({
        color: 'orange',
        title: '报名信息不完整',
        message: `报名主办赛道前请先完善：${profileIncomplete.join('、')}。可前往「个人资料」页面填写。`,
        icon: <Icon path={mdiClose} size={1} />,
        autoClose: 8000,
      })
      setDisabled(false)
      return
    }

    // 资料齐全但学籍未通过审核：给出明确原因，避免提交后收到后端报错
    if (verifyBlocked) {
      showNotification({
        color: verifyBlocked.color,
        title: verifyBlocked.title,
        message: verifyBlocked.message,
        icon: <Icon path={mdiAlertCircleOutline} size={1} />,
        autoClose: 8000,
      })
      setDisabled(false)
      return
    }

    if (shouldRequireInviteCode && !inviteCode) {
      showNotification({
        color: 'orange',
        message: t('game.notification.no_invite_code'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      setDisabled(false)
      return
    }

    if (canSelectDivision && hasDivision && !divisionId) {
      showNotification({
        color: 'orange',
        message: t('game.notification.no_division'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      setDisabled(false)
      return
    }

    try {
      await onSubmitJoin({
        teamId: isSolo ? undefined : parseInt(team!, 10),
        inviteCode: shouldRequireInviteCode ? inviteCode : undefined,
        divisionId:
          canSelectDivision && hasDivision
            ? parseInt(divisionId, 10)
            : !canSelectDivision && joinedDivision
              ? joinedDivision.id
              : undefined,
      })
    } finally {
      setInviteCode('')
      setDivisionId('')
      setDisabled(false)
      props.onClose()
    }
  }

  return (
    <Modal {...modalProps}>
      <Stack>
        {profileIncomplete.length > 0 && (
          <Alert color="orange" icon={<Icon path={mdiAlertCircleOutline} size={1} />} title="报名主办赛道需先完善资料">
            <Text size="sm">
              缺少：{profileIncomplete.join('、')}。请前往「个人资料」页面填写真实姓名、学号并绑定学校。
            </Text>
          </Alert>
        )}
        {verifyBlocked && (
          <Alert color={verifyBlocked.color} icon={<Icon path={mdiAlertCircleOutline} size={1} />} title={verifyBlocked.title}>
            <Text size="sm">{verifyBlocked.message}</Text>
          </Alert>
        )}
        {isSolo ? (
          <Alert color="teal" variant="light" icon={<Icon path={mdiAccountOutline} size={1} />}>
            <Text size="sm">
              本赛事为个人赛，报名后系统将自动为你创建个人队伍，无需手动组队。
            </Text>
          </Alert>
        ) : (
          <Select
            required
            label={t('game.content.join.team.label')}
            description={t('game.content.join.team.description')}
            data={teamsData}
            disabled={disabled}
            value={team}
            onChange={(e) => e && setTeam(e)}
          />
        )}

        {/* 个人赛：按学校 + 年级自动匹配分组，展示匹配结果而非让选手手选 */}
        {isSolo && autoDivisionAvailable && selectedDivision && (
          <Select
            label={t('game.content.join.division.label')}
            description="按你的学校与年级自动匹配，如需调整请联系赛事管理员"
            readOnly
            data={divisionOptions}
            disabled
            value={divisionId}
          />
        )}

        {!canSelectDivision && joinedDivision && (
          <Select
            required
            label={t('game.content.join.division.label')}
            description={t('game.content.join.division.description')}
            readOnly={true}
            disabled={true}
            data={[
              {
                label: joinedDivision.name ?? `Division #${joinedDivision.id}`,
                value: joinedDivision.id!.toString(),
              },
            ]}
            value={joinedDivision.id!.toString()}
          />
        )}

        {/* 非个人赛：保留手动选择赛区 */}
        {!isSolo && canSelectDivision && hasDivision && (
          <Select
            required
            label={t('game.content.join.division.label')}
            description={t('game.content.join.division.description')}
            readOnly={!canSelectDivision}
            data={divisionOptions}
            disabled={disabled}
            value={divisionId}
            onChange={(e) => setDivisionId(e ?? '')}
          />
        )}
        {shouldRequireInviteCode && (
          <TextInput
            required
            label={t('game.content.join.invite_code.label')}
            description={t('game.content.join.invite_code.description')}
            value={inviteCode}
            onChange={(e) => setInviteCode(e.target.value)}
            disabled={disabled}
          />
        )}
        <Button disabled={disabled} onClick={onJoinGame}>
          {t('game.button.join')}
        </Button>
      </Stack>
    </Modal>
  )
}
