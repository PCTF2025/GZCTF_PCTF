import { Badge, Box, Button, Card, Center, Group, Image, SimpleGrid, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import { mdiBookOpenPageVariantOutline, mdiChevronRight, mdiFlagCheckered, mdiShieldCrownOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { Link } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { SITE_LINKS } from '@Utils/SiteLinks'
import logoImage from '@Resources/pctf-logo.png'
import dkdunLogo from '@Resources/dkdun-logo.png'

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

  /// 后台已把赛道绑定到具体赛事时，首页卡片直达赛事详情页
  const trackLink = (track: TrackEntry) => {
    const bound = track.key === 'official' ? config?.tracks?.officialGameId : config?.tracks?.publicGameId
    // 0 表示后台未绑定该赛道
    return bound && bound > 0 ? `/games/${bound}` : track.fallbackLink
  }

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
            {TRACKS.map((track) => (
              <Card
                key={track.key}
                withBorder
                radius="md"
                padding="xl"
                component={Link}
                to={trackLink(track)}
                style={{ textDecoration: 'none' }}
              >
                <Stack gap="sm">
                  <Group justify="space-between" align="flex-start">
                    <Group gap="sm">
                      <Icon path={track.icon} size={1.6} />
                      <Title order={4}>{track.title}</Title>
                    </Group>
                    <Badge color={track.color} variant="light">
                      进入
                    </Badge>
                  </Group>
                  <Text size="sm" c="dimmed">
                    {track.description}
                  </Text>
                  <Button
                    variant="light"
                    color={track.color}
                    rightSection={<Icon path={mdiChevronRight} size={0.8} />}
                    w="fit-content"
                    mt="xs"
                  >
                    查看赛事
                  </Button>
                </Stack>
              </Card>
            ))}
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
        </Stack>
      </Stack>
    </WithNavBar>
  )
}

export default Home
