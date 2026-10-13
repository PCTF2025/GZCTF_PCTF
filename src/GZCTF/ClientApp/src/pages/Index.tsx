import { Badge, Box, Button, Card, Center, Group, Image, SimpleGrid, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import { mdiBookOpenPageVariantOutline, mdiChevronRight, mdiFlagCheckered, mdiShieldCrownOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { CSSProperties, FC, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { useConfig } from '@Hooks/useConfig'
import { useGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import { getTrackStatus, TrackState } from '@Utils/TrackStatus'
import { SITE_LINKS } from '@Utils/SiteLinks'
import logoImage from '@Resources/pctf-logo.png'
import dkdunLogo from '@Resources/dkdun-logo.png'
import digiforensicsLogo from '@Resources/digiforensics-logo.png'

/// 赛道入口：主办赛道 / 公开赛道
interface TrackEntry {
  key: 'official' | 'public'
  title: string
  description: string
  /// 未绑定赛事时的兜底链接（按赛道筛选）
  fallbackLink: string
  color: string
  icon: string
}

const TRACKS: TrackEntry[] = [
  {
    key: 'official',
    title: '主办赛道',
    description: '由赛事主办方统一命题，面向受邀队伍开放。',
    fallbackLink: '/games?track=official',
    color: 'teal',
    icon: mdiShieldCrownOutline,
  },
  {
    key: 'public',
    title: '公开赛道',
    description: '面向所有注册选手开放，可自由报名参与。',
    fallbackLink: '/games?track=public',
    color: 'blue',
    icon: mdiFlagCheckered,
  },
]

const Home: FC = () => {
  usePageTitle()
  const { config } = useConfig()

  /// 赛道绑定的赛事 Id（0 表示后台未绑定）
  const officialGameId = config?.tracks?.officialGameId ?? 0
  const publicGameId = config?.tracks?.publicGameId ?? 0

  /// 拉取主办赛道赛事详情，用于判断「报名中 / 已发布第 N 周」
  const { game: officialGame } = useGame(officialGameId)

  /// 每分钟刷新一次，跨越发布时间点时状态自动切换
  const [now, setNow] = useState(() => dayjs())
  useEffect(() => {
    const timer = setInterval(() => setNow(dayjs()), 60 * 1000)
    return () => clearInterval(timer)
  }, [])

  /// 后台已把赛道绑定到具体赛事时，首页卡片直达赛事详情页
  const trackLink = (track: TrackEntry) => {
    const bound = track.key === 'official' ? officialGameId : publicGameId
    return bound && bound > 0 ? `/games/${bound}` : track.fallbackLink
  }

  /// 各赛道当前状态
  const statusOf = (track: TrackEntry) =>
    getTrackStatus(
      track.key,
      track.key === 'official' ? officialGame : undefined,
      track.key === 'official' ? officialGameId > 0 : publicGameId > 0,
      now
    )

  return (
    <WithNavBar minWidth={0} withFooter withHeader stickyHeader>
      <Stack gap="xl" py="md">
        {/* 固定 Logo：居中限宽，下方两行赛事信息 */}
        <Stack align="center" gap="sm" mt="xs">
          <Image src={logoImage} alt="PCTF" maw={520} w="100%" h="auto" fit="contain" />
          <Stack gap={4} align="center">
            <Title order={3} ta="center">
              第三届 PCTF 2026
            </Title>
            <Text size="lg" c="dimmed" ta="center" ff="monospace">
              Date: 2026.11.01 - 2026.12.01
            </Text>
          </Stack>
        </Stack>

        {/* 两个赛道入口 */}
        <Stack gap="md">
          <Title order={3}>选择赛道</Title>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="lg">
            {TRACKS.map((track) => {
              const st = statusOf(track)
              /// 尚未开放（敬请期待）或已结束的赛道不可点击进入
              const locked = st.state === TrackState.Coming || st.state === TrackState.Ended

              const cardProps = {
                key: track.key,
                withBorder: true as const,
                radius: 'md' as const,
                padding: 'xl' as const,
                style: { textDecoration: 'none', opacity: locked ? 0.6 : 1 },
              }

              const body = (
                <Stack gap="sm">
                  <Group justify="space-between" align="flex-start">
                    <Group gap="sm">
                      <Icon path={track.icon} size={1.6} />
                      <Title order={4}>{track.title}</Title>
                    </Group>
                    <Badge color={st.color} variant="light">
                      {st.badge}
                    </Badge>
                  </Group>
                  <Text size="sm" c="dimmed">
                    {track.description}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {st.hint}
                  </Text>
                  {locked ? (
                    <Button
                      variant="light"
                      color="gray"
                      disabled
                      rightSection={<Icon path={mdiChevronRight} size={0.8} />}
                      w="fit-content"
                      mt="xs"
                    >
                      尚未开放
                    </Button>
                  ) : (
                    <Button
                      variant="light"
                      color={track.color}
                      rightSection={<Icon path={mdiChevronRight} size={0.8} />}
                      w="fit-content"
                      mt="xs"
                    >
                      查看赛事
                    </Button>
                  )}
                </Stack>
              )

              return locked ? (
                <Card {...cardProps}>{body}</Card>
              ) : (
                <Card {...cardProps} component={Link} to={trackLink(track)}>
                  {body}
                </Card>
              )
            })}
          </SimpleGrid>

          {/* PCTF Wiki 文档入口 */}
          <Card withBorder radius="md" p="lg">
            <Group justify="space-between" wrap="nowrap" align="center">
              <Group gap="md" wrap="nowrap" align="center">
                <ThemeIcon size={48} radius="md" variant="light" color="grape">
                  <Icon path={mdiBookOpenPageVariantOutline} size={1.4} />
                </ThemeIcon>
                <Box>
                  <Title order={4}>PCTF Wiki</Title>
                  <Text size="sm" c="dimmed">
                    赛事规则、平台使用说明与题目知识点文档
                  </Text>
                </Box>
              </Group>
              <Button
                component="a"
                href={SITE_LINKS.wiki}
                target="_blank"
                rel="noopener noreferrer"
                variant="light"
                color="grape"
                rightSection={<Icon path={mdiChevronRight} size={0.8} />}
                w="fit-content"
              >
                前往 Wiki
              </Button>
            </Group>
          </Card>

          {/* 赞助商广告：林枫云 */}
          <Card withBorder radius="md" p="lg">
            <Group justify="space-between" wrap="nowrap" align="flex-start" gap="lg">
              <Stack gap="sm" style={{ flex: 1, minWidth: 0 }}>
                <Group gap="md" wrap="nowrap" align="center">
                  <Box
                    p={6}
                    style={{
                      background: '#ffffff',
                      borderRadius: 8,
                      lineHeight: 0,
                    }}
                  >
                    <Image src={dkdunLogo} alt="林枫云" h={34} w="auto" fit="contain" />
                  </Box>
                  <Badge variant="light" color="blue" size="sm">
                    赞助商
                  </Badge>
                </Group>

                <Title order={4}>林枫云（四川）网络科技有限公司</Title>

                <Text size="sm" c="dimmed" style={{ lineHeight: 1.8 }}>
                  林枫云（四川）网络科技有限公司（简称“林枫云”）成立于2024年，是一家专注于云计算、高频算力的创新型科技公司。林枫云秉承“技术驱动未来，创新引领发展”的理念，致力于为全球客户提供稳定、安全、智能的云计算解决方案。公司正自主研发云平台和AI技术，自成立以来，林枫云核心团队一直深耕云计算和大数据领域，专注于云服务的优化、数据安全与AI算法的创新。通过不断创新和提升技术实力，林枫云为多个行业定制上云方案和数据解决方案，接受自托管与全托管。
                </Text>
                <Text size="sm" c="dimmed" style={{ lineHeight: 1.8 }}>
                  林枫云提供多元化的云服务，涵盖云服务器、高频物理机、内容分发等服务，结合8年行业经验，为客户提供高效、智能的服务支持。同时，林枫云还提供7*16小时技术支持，确保客户在使用过程中得到实时的帮助与服务。公司在全球范围内的数据中心租用多个机柜，资源覆盖中国大陆、中国香港、美国等地，并提供BGP多线接入、国际高速带宽以及自有硬件基础设施，保障服务的高效性与稳定性。
                </Text>

                <Button
                  component="a"
                  href={SITE_LINKS.dkdun}
                  target="_blank"
                  rel="noopener noreferrer"
                  variant="light"
                  color="blue"
                  rightSection={<Icon path={mdiChevronRight} size={0.8} />}
                  w="fit-content"
                >
                  访问 www.dkdun.cn
                </Button>
              </Stack>
            </Group>
          </Card>

          {/* 赞助商广告：DigiForensics 电子取证 */}
          <Card withBorder radius="md" p="lg">
            <Group justify="space-between" wrap="nowrap" align="flex-start" gap="lg">
              <Stack gap="sm" style={{ flex: 1, minWidth: 0 }}>
                <Group gap="md" wrap="nowrap" align="center">
                  <Box
                    p={6}
                    style={{
                      background: '#ffffff',
                      borderRadius: 8,
                      lineHeight: 0,
                    }}
                  >
                    <Image src={digiforensicsLogo} alt="DigiForensics" h={34} w="auto" fit="contain" />
                  </Box>
                  <Badge variant="light" color="cyan" size="sm">
                    赞助商
                  </Badge>
                </Group>

                <Title order={4}>DigiForensics · 数字取证学习平台</Title>

                <Text size="sm" c="dimmed" style={{ lineHeight: 1.8 }}>
                  面向电子取证、数字调查与应急响应实践的学习与知识沉淀平台，由电子取证及可信应用湖北省协同创新中心参与支持。
                  平台强调「可复现、可验证、可落地」：保留证据来源、痕迹解释与关键步骤，并通过多源证据交叉验证形成可被复核的结论。
                </Text>
                <Text size="sm" c="dimmed" style={{ lineHeight: 1.8 }}>
                  内容覆盖主机取证、日志溯源、流量与内存分析、移动与应用数据、恶意样本与攻击痕迹五大方向，
                  欢迎学习者与研究者参与文章投稿、案例复盘、工具推荐与勘误修正。
                </Text>

                <Button
                  component="a"
                  href={SITE_LINKS.digiforensics}
                  target="_blank"
                  rel="noopener noreferrer"
                  variant="light"
                  color="cyan"
                  rightSection={<Icon path={mdiChevronRight} size={0.8} />}
                  w="fit-content"
                >
                  访问 www.digiforensics.cn
                </Button>
              </Stack>
            </Group>
          </Card>
        </Stack>
      </Stack>
    </WithNavBar>
  )
}

export default Home
