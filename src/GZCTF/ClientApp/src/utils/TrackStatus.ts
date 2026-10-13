import dayjs from 'dayjs'
import { DetailedGameInfoModel } from '@Api'

/// 首页赛道卡片的状态
export enum TrackState {
  /// 报名中（比赛开始前）
  Registering = 'registering',
  /// 已发布赛题（比赛进行中，按周提示）
  Released = 'released',
  /// 敬请期待（赛事未开始或未开放）
  Coming = 'coming',
  /// 已结束
  Ended = 'ended',
}

export interface TrackStatusInfo {
  state: TrackState
  /// 徽章文案
  badge: string
  /// 徽章颜色
  color: string
  /// 副标题（按钮下方说明）
  hint: string
  /// 当前/最近已发布到第几周（1-5），为 0 表示尚未发布任何赛题
  releasedWeek: number
  /// 下一批赛题的发布时间（用于倒计时提示），无则为 null
  nextRelease: dayjs.Dayjs | null
}

/// 赛题发布批次：第 1-4 批
/// 第一批 2026-11-01（周日）起，之后每周六滚动，最后一批 2026-11-21
/// 全部赛题持续开放至 2026-12-01 比赛结束
export const RELEASE_BATCHES: { week: number; at: string; label: string }[] = [
  { week: 1, at: '2026-11-01T00:00:00+08:00', label: '第一批赛题' },
  { week: 2, at: '2026-11-07T00:00:00+08:00', label: '第二批赛题' },
  { week: 3, at: '2026-11-14T00:00:00+08:00', label: '第三批赛题' },
  { week: 4, at: '2026-11-21T00:00:00+08:00', label: '第四批赛题' },
]

/// 比赛结束时间（所有赛题持续开放至此）
export const COMPETITION_END = '2026-12-01T23:59:59+08:00'

/// 根据当前时间，算出已发布到第几批（0 表示尚未开始发布）
export const releasedBatchOf = (now: dayjs.Dayjs = dayjs()) =>
  RELEASE_BATCHES.filter((b) => !now.isBefore(dayjs(b.at))).length

/// 下一批发布信息（全部发布完则为 null）
export const nextBatchOf = (now: dayjs.Dayjs = dayjs()) =>
  RELEASE_BATCHES.find((b) => now.isBefore(dayjs(b.at))) ?? null

const WEEK_CN = ['一', '二', '三', '四', '五']

/// 依据赛事数据与发布时间表，推导赛道卡片状态
export const getTrackStatus = (
  track: 'official' | 'public',
  game: DetailedGameInfoModel | undefined,
  bound: boolean,
  now: dayjs.Dayjs = dayjs()
): TrackStatusInfo => {
  const releasedWeek = releasedBatchOf(now)
  const nextBatch = nextBatchOf(now)
  const ended = !now.isBefore(dayjs(COMPETITION_END))

  /// 公开赛道：固定「敬请期待」，不随比赛进程变化
  if (track === 'public') {
    return {
      state: TrackState.Coming,
      badge: '敬请期待',
      color: 'gray',
      hint: '公开赛道尚未开放，开放后将在此处提供入口。',
      releasedWeek: 0,
      nextRelease: null,
    }
  }

  /// 主办赛道：未绑定具体赛事时按发布时间表展示
  if (!bound || !game) {
    if (ended)
      return {
        state: TrackState.Ended,
        badge: '已结束',
        color: 'gray',
        hint: '本届比赛已结束，感谢参与。',
        releasedWeek: RELEASE_BATCHES.length,
        nextRelease: null,
      }

    if (releasedWeek === 0)
      return {
        state: TrackState.Registering,
        badge: '报名中',
        color: 'teal',
        hint: `赛题将于 ${dayjs(RELEASE_BATCHES[0].at).format('M月D日')} 起分批发布，现可报名。`,
        releasedWeek: 0,
        nextRelease: nextBatch ? dayjs(nextBatch.at) : null,
      }

    return {
      state: TrackState.Released,
      badge: `已发布第${WEEK_CN[releasedWeek - 1]}周赛题`,
      color: 'green',
      hint: nextBatch
        ? `下一批赛题将于 ${dayjs(nextBatch.at).format('M月D日')} 发布。`
        : '全部赛题已发布，比赛持续至 12 月 1 日结束。',
      releasedWeek,
      nextRelease: nextBatch ? dayjs(nextBatch.at) : null,
    }
  }

  /// 已绑定赛事：以赛事自身的周次窗口为准（更精确）
  const weekBuckets = (game.weekBuckets ?? []).filter(
    (b) => typeof b.key === 'number' && b.key >= 1 && b.key <= 5 && b.startUtc
  )

  /// 已到开始时间、且已经开始过的周次（含正在进行的那一周）
  const openedWeeks = weekBuckets.filter((b) => b.startUtc && !now.isBefore(dayjs(b.startUtc)))
  const currentWeek = openedWeeks.reduce((max, b) => Math.max(max, b.key ?? 0), 0)

  /// 已绑定赛事的开始时间：报名中 → 已发布
  const gameStart = dayjs(game.start)
  const gameEnd = dayjs(game.end)

  if (gameEnd.isValid() && !now.isBefore(gameEnd))
    return {
      state: TrackState.Ended,
      badge: '已结束',
      color: 'gray',
      hint: '本届比赛已结束，感谢参与。',
      releasedWeek: currentWeek,
      nextRelease: null,
    }

  if (gameStart.isValid() && now.isBefore(gameStart))
    return {
      state: TrackState.Registering,
      badge: '报名中',
      color: 'teal',
      hint: '比赛尚未开始，现可报名。',
      releasedWeek: 0,
      nextRelease: nextBatch ? dayjs(nextBatch.at) : null,
    }

  /// 比赛进行中：优先用已绑定的周次，缺失时回退到发布时间表
  const week = currentWeek || releasedWeek
  const upcoming = weekBuckets.find((b) => b.startUtc && now.isBefore(dayjs(b.startUtc)))
  const fallbackNext = nextBatch ? dayjs(nextBatch.at) : null

  return {
    state: TrackState.Released,
    badge: week > 0 ? `已发布第${WEEK_CN[week - 1]}周赛题` : '进行中',
    color: 'green',
    hint: upcoming?.startUtc
      ? `下一批赛题将于 ${dayjs(upcoming.startUtc).format('M月D日')} 发布。`
      : fallbackNext
        ? `下一批赛题将于 ${fallbackNext.format('M月D日')} 发布。`
        : '全部赛题已发布，比赛持续至 12 月 1 日结束。',
    releasedWeek: week,
    nextRelease: upcoming?.startUtc ? dayjs(upcoming.startUtc) : fallbackNext,
  }
}
