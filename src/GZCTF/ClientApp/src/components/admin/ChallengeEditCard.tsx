import {
  ActionIcon,
  Badge,
  Card,
  Group,
  Progress,
  Stack,
  Switch,
  Text,
  Tooltip,
  useMantineColorScheme,
  useMantineTheme,
} from '@mantine/core'
import { mdiDatabaseEditOutline, mdiEyeOutline, mdiLockOutline, mdiPuzzleEditOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { Dispatch, FC, SetStateAction, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import { ChallengeInfoModel, ChallengeCategory } from '@Api'
import classes from '@Styles/ChallengeEditCard.module.css'

interface ChallengeEditCardProps {
  challenge: ChallengeInfoModel
  onToggle: (challenge: ChallengeInfoModel, setDisabled: Dispatch<SetStateAction<boolean>>) => void
}

export const ChallengeEditCard: FC<ChallengeEditCardProps> = ({ challenge, onToggle }) => {
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const data = challengeCategoryLabelMap.get(challenge.category as ChallengeCategory)
  const theme = useMantineTheme()
  const { id } = useParams()

  const [disabled, setDisabled] = useState(false)

  const { t } = useTranslation()
  const { colorScheme } = useMantineColorScheme()

  const color = data?.color ?? theme.primaryColor
  const colors = theme.colors[color]

  const minIdx = colorScheme === 'dark' ? 8 : 6
  const curIdx = colorScheme === 'dark' ? 6 : 4

  const [min, cur, tot] = [challenge.minScore ?? 0, challenge.score ?? 500, challenge.originalScore ?? 500]
  const minRate = (min / tot) * 100
  const curRate = (cur / tot) * 100

  const contentWidth = 'calc(100% - 12rem)'

  // 出题人能看到全部题目，但非本人（且非被授权方向）的题目为只读
  const canEdit = challenge.canEdit !== false
  // 同方向交叉查看：可进入详情页查看与测题，但不能修改
  const canTest = challenge.canTest === true

  return (
    <Card shadow="sm" p="sm" opacity={canEdit ? 1 : 0.65}>
      <Group wrap="nowrap" justify="space-between" gap="xs">
        <Switch
          color={color}
          disabled={disabled || !canEdit}
          checked={challenge.isEnabled}
          onChange={() => canEdit && onToggle(challenge, setDisabled)}
        />

        <Icon path={data!.icon} color={theme.colors[data?.color ?? theme.primaryColor][5]} size={1.2} />

        <Stack gap={0} maw={contentWidth} miw={contentWidth}>
          <Text truncate fw="bold">
            {challenge.title}
          </Text>
          <Text size="sm" fw="bold" ff="monospace" w="5rem">
            {challenge.score}
            <Text span fw="bold" c="dimmed">
              /{challenge.originalScore}pts
            </Text>
          </Text>
        </Stack>

        {canEdit ? (
          <>
            <Tooltip label={t('admin.button.challenges.edit')} position="left" offset={10} classNames={classes}>
              <ActionIcon c={color} component={Link} to={`/admin/games/${id}/challenges/${challenge.id}`}>
                <Icon path={mdiPuzzleEditOutline} size={1} />
              </ActionIcon>
            </Tooltip>
            <Tooltip
              label={t('admin.button.challenges.edit_more')}
              ta="end"
              position="left"
              offset={54}
              classNames={classes}
            >
              <ActionIcon c={color} component={Link} to={`/admin/games/${id}/challenges/${challenge.id}/flags`}>
                <Icon path={mdiDatabaseEditOutline} size={1} />
              </ActionIcon>
            </Tooltip>
          </>
        ) : canTest ? (
          // 同方向交叉查看：可进入详情页查看与测题，但不可修改
          <Tooltip
            label={challenge.authorName ? `由 ${challenge.authorName} 创建，可查看与测题` : '可查看与测题'}
            position="left"
            offset={10}
            classNames={classes}
          >
            <ActionIcon c="dimmed" component={Link} to={`/admin/games/${id}/challenges/${challenge.id}`}>
              <Icon path={mdiEyeOutline} size={1} />
            </ActionIcon>
          </Tooltip>
        ) : (
          <Tooltip
            label={challenge.authorName ? `由 ${challenge.authorName} 创建，你无权修改` : '你无权修改该题目'}
            position="left"
            offset={10}
            classNames={classes}
          >
            <ActionIcon variant="subtle" c="dimmed" style={{ cursor: 'not-allowed' }}>
              <Icon path={mdiLockOutline} size={1} />
            </ActionIcon>
          </Tooltip>
        )}
      </Group>

      {challenge.authorName && (
        <Text size="xs" c="dimmed" mt={4} truncate>
          出题人：{challenge.authorName}
        </Text>
      )}

      <Card.Section mt="sm">
        <Progress.Root radius={0}>
          <Progress.Section value={minRate} color={colors[minIdx]} />
          <Progress.Section value={curRate - minRate} color={colors[curIdx]} />
        </Progress.Root>
      </Card.Section>
    </Card>
  )
}
