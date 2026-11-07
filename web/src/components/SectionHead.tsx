import type { ReactNode } from 'react'

export function SectionHead({
  label,
  title,
  meta,
  children,
}: {
  label: string
  title: ReactNode
  meta?: ReactNode
  children?: ReactNode
}) {
  return (
    <div className="section-head">
      <div className="section-head-left">
        <span className="label">{label}</span>
        <h2>{title}</h2>
      </div>
      <div className="section-head-right">
        {meta ? <span className="mono section-meta">{meta}</span> : null}
        {children}
      </div>
    </div>
  )
}
