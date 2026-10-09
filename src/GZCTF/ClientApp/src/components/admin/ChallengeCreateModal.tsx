import { Button, ComboboxItem, Modal, ModalProps, Select, Stack, TextInput } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { showErrorMsg } from '@Utils/Shared'
import { useUser } from '@Hooks/useUser'
import {
  ChallengeCategoryItem,
  ChallengeCategoryList,
  ChallengeTypeItem,
  useChallengeCategoryLabelMap,
  useChallengeTypeLabelMap,
} from '@Utils/Shared'
import api, { ChallengeInfoModel, ChallengeCategory, ChallengeType, Role } from '@Api'

interface ChallengeCreateModalProps extends ModalProps {
  onAddChallenge: (game: ChallengeInfoModel) => void
}

export const ChallengeCreateModal: FC<ChallengeCreateModalProps> = (props) => {
  const { id } = useParams()
  const { onAddChallenge, ...modalProps } = props
  const [disabled, setDisabled] = useState(false)
  const navigate = useNavigate()
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const challengeTypeLabelMap = useChallengeTypeLabelMap()

  const [title, setTitle] = useInputState('')
  const [category, setCategory] = useState<string | null>(null)
  const [type, setType] = useState<string | null>(null)

  const { user } = useUser()

  /// 出题人被指定了出题方向时，只允许在这些方向内建题；
  /// 未指定方向（或系统管理员、出题管理员未配方向）则不做限制。
  const allowedCategories = useMemo(() => {
    const managed = (user?.managedCategories ?? '')
      .split(',')
      .map((v) => v.trim())
      .filter(Boolean)

    const isAdmin = user?.role === Role.Admin
    if (isAdmin || managed.length === 0) return ChallengeCategoryList

    return ChallengeCategoryList.filter((c) => managed.includes(c))
  }, [user])

  /// 仅有一个可用方向时自动选中，减少出题人操作
  useMemo(() => {
    if (allowedCategories.length === 1 && !category) setCategory(allowedCategories[0])
  }, [allowedCategories])

  const { t } = useTranslation()

  const onCreate = async () => {
    if (!title || !category || !type) return

    setDisabled(true)
    const numId = parseInt(id ?? '-1')

    try {
      const res = await api.edit.editAddGameChallenge(numId, {
        title: title,
        category: category as ChallengeCategory,
        type: type as ChallengeType,
      })
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.challenges.created'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      onAddChallenge(res.data)
      navigate(`/admin/games/${id}/challenges/${res.data.id}`)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <Modal {...modalProps}>
      <Stack>
        <TextInput
          label={t('admin.content.games.challenges.title')}
          type="text"
          required
          placeholder="Title"
          value={title}
          onChange={setTitle}
        />
        <Select
          required
          label={t('admin.content.games.challenges.category')}
          placeholder="Category"
          value={category}
          onChange={setCategory}
          renderOption={ChallengeCategoryItem}
          data={allowedCategories.map((category) => {
            const data = challengeCategoryLabelMap.get(category)
            return { value: category, label: data?.name, ...data } as ComboboxItem
          })}
        />
        <Select
          required
          label={t('admin.content.games.challenges.type.label')}
          description={t('admin.content.games.challenges.type.description')}
          placeholder="Type"
          value={type}
          onChange={setType}
          renderOption={ChallengeTypeItem}
          data={Object.entries(ChallengeType).map((type) => {
            const data = challengeTypeLabelMap.get(type[1])
            return { value: type[1], label: data?.name, ...data } as ComboboxItem
          })}
        />
        <Button fullWidth disabled={disabled} onClick={onCreate}>
          {t('admin.button.challenges.new')}
        </Button>
      </Stack>
    </Modal>
  )
}
