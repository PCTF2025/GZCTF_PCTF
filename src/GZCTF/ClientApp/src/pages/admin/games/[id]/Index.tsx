import { Center, Loader } from '@mantine/core'
import { FC, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router'
import { Role } from '@Api'
import { useUser } from '@Hooks/useUser'

/**
 * 比赛编辑入口重定向页。
 *
 * `/admin/games/{id}` 本身没有内容，需落到具体子页。
 * 系统管理员默认进「比赛设置」（info），其余职能用户（出题人等）
 * 只看得到题目管理，因此默认进「题目」（challenges）。
 */
const GameEditRedirect: FC = () => {
  const { id } = useParams()
  const navigate = useNavigate()
  const { user } = useUser()

  useEffect(() => {
    if (!id) {
      navigate('/admin/games', { replace: true })
      return
    }

    // 等待用户信息就绪，避免管理员被误判为出题人
    if (!user) return

    const target = user.role === Role.Admin ? 'info' : 'challenges'
    navigate(`/admin/games/${id}/${target}`, { replace: true })
  }, [id, user])

  return (
    <Center mih={300}>
      <Loader />
    </Center>
  )
}

export default GameEditRedirect
