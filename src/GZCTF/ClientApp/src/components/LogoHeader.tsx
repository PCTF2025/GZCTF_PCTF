import { Group, GroupProps, Title } from '@mantine/core'
import { forwardRef } from 'react'
import { LogoBox } from '@Components/LogoBox'
import classes from '@Styles/LogoHeader.module.css'

/// 顶部标题固定展示，不受后台 title 配置影响
const PLATFORM_TITLE = 'PCTF 2026'

export const LogoHeader = forwardRef<HTMLDivElement, GroupProps>((props, ref) => (
  <Group ref={ref} wrap="nowrap" align="center" justify="flex-start" gap="sm" {...props}>
    <LogoBox size="50px" pr="sm" />
    <Title textWrap="nowrap" className={classes.title}>
      {PLATFORM_TITLE}
    </Title>
  </Group>
))
