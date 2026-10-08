import {
  Badge,
  Box,
  Button,
  Card,
  Center,
  Group,
  Image,
  SimpleGrid,
  Stack,
  Text,
  ThemeIcon,
  Title,
} from '@mantine/core'
import { Carousel } from '@mantine/carousel'
import Autoplay from 'embla-carousel-autoplay'
import { mdiBookOpenPageVariantOutline, mdiChevronRight, mdiFlagCheckered, mdiShieldCrownOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'
import { SITE_LINKS } from '@Utils/SiteLinks'
import '@mantine/carousel/styles.css'

interface HomeBanner {
  id: string
  title?: string | null
  imageUrl?: string | null
  linkUrl?: string | null
  enabled: boolean
  sortOrder: number
}

interface BannerResponse {
  intervalMs: number
  banners: HomeBanner[]
}

/// 赛道入口：主办赛道 / 公开赛道
interface TrackEntry {
  key: string
  title: string
  description: string
  link: string
  color: string
  icon: string
}

const TRACKS: TrackEntry[] = [
  {
    key: 'official',
    title: '主办赛道',
    description: '由赛事主办方统一命题，面向受邀队伍开放。',
    link: '/games?track=official',
    color: 'teal',
    icon: mdiShieldCrownOutline,
  },
  {
    key: 'public',
    title: '公开赛道',
    description: '面向所有注册选手开放，可自由报名参与。',
    link: '/games?track=public',
    color: 'blue',
    icon: mdiFlagCheckered,
  },
]

const Home: FC = () => {
  const [banners, setBanners] = useState<HomeBanner[]>([])
  const [intervalMs, setIntervalMs] = useState(5000)
  const [autoplay, setAutoplay] = useState(Autoplay({ delay: 5000, stopOnInteraction: false, playOnInit: true }))

  usePageTitle()

  const loadBanners = useCallback(async () => {
    try {
      const res = await fetch('/api/banner')
      if (!res.ok) return

      const data = (await res.json()) as BannerResponse
      setBanners(data.banners ?? [])
      if (data.intervalMs > 0) setIntervalMs(data.intervalMs)
    } catch {
      /* 拉取失败时退化为无 Banner 展示 */
    }
  }, [])

  useEffect(() => {
    loadBanners()
  }, [loadBanners])

  useEffect(() => {
    setAutoplay(Autoplay({ delay: intervalMs, stopOnInteraction: false, playOnInit: true }))
  }, [intervalMs])

  const visibleBanners = banners.filter((b) => b.enabled && b.imageUrl)

  return (
    <WithNavBar minWidth={0} withFooter withHeader stickyHeader>
      <Stack gap="xl" py="md">
        {/* Banner 轮播：后台未配置时回退为一块占位横幅 */}
        {visibleBanners.length > 0 ? (
          <Carousel
            withIndicators
            withControls={visibleBanners.length > 1}
            height={320}
            plugins={[autoplay]}
            emblaOptions={{ loop: visibleBanners.length > 1 }}
          >
            {visibleBanners.map((b) => {
              const inner = (
                <Box pos="relative" h="100%" w="100%">
                  <Image src={b.imageUrl ?? ''} alt={b.title ?? ''} h={320} fit="cover" radius="md" />
                </Box>
              )

              return (
                <Carousel.Slide key={b.id}>
                  {b.linkUrl ? (
                    <Box
                      component="a"
                      href={b.linkUrl}
                      target={b.linkUrl.startsWith('http') ? '_blank' : undefined}
                      rel="noopener noreferrer"
                      style={{ display: 'block', height: '100%', textDecoration: 'none' }}
                    >
                      {inner}
                    </Box>
                  ) : (
                    inner
                  )}
                </Carousel.Slide>
              )
            })}
          </Carousel>
        ) : (
          <Card withBorder radius="md" h={220} p={0}>
            <Center h="100%">
              <Stack align="center" gap="xs">
                <Icon path={mdiFlagCheckered} size={2} />
                <Text c="dimmed">暂无轮播内容</Text>
              </Stack>
            </Center>
          </Card>
        )}

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
                to={track.link}
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
        </Stack>
      </Stack>
    </WithNavBar>
  )
}

export default Home
