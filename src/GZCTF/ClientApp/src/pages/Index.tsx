import { Badge, Box, Button, Card, Center, Group, Image, SimpleGrid, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import { mdiBookOpenPageVariantOutline, mdiChevronRight, mdiFlagCheckered, mdiShieldCrownOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { Link } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'
import { SITE_LINKS } from '@Utils/SiteLinks'
import logoImage from '@Resources/pctf-logo.png'

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
  usePageTitle()

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
