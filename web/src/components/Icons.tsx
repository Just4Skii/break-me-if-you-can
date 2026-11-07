import type { SVGProps } from 'react'

type IconProps = SVGProps<SVGSVGElement> & { size?: number }

function base({ size = 16, ...props }: IconProps) {
  return {
    width: size,
    height: size,
    viewBox: '0 0 16 16',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.4,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
    ...props,
  }
}

export function CheckIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <path d="m3 8.5 3.2 3.2L13 5" />
    </svg>
  )
}

export function CrossIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <path d="M4 4l8 8M12 4l-8 8" />
    </svg>
  )
}

export function DashIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <path d="M4 8h8" />
    </svg>
  )
}

export function ArrowUpRightIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <path d="M5 11 11 5M5.5 5H11v5.5" />
    </svg>
  )
}

export function CopyIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <rect x="5.5" y="5.5" width="8" height="8" rx="1" />
      <path d="M10.5 5.5v-2a1 1 0 0 0-1-1h-6a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2" />
    </svg>
  )
}

export function PlayIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <path d="M5 3.5 12.5 8 5 12.5v-9Z" fill="currentColor" stroke="none" />
    </svg>
  )
}

export function RefreshIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <path d="M13 8a5 5 0 1 1-1.6-3.7M13 3.5V8h-4.5" />
    </svg>
  )
}

export function GithubIcon(props: IconProps) {
  return (
    <svg {...base(props)}>
      <path d="M6 12.5c-2.2.6-2.2-1.1-3-1.4M11.5 14v-2.1c0-.6-.2-1.1-.6-1.5 1.6-.2 2.9-.9 2.9-3.4 0-.7-.2-1.4-.6-1.9.1-.4.2-1.2-.1-1.9 0 0-.6-.2-1.9.7a6.6 6.6 0 0 0-3.4 0C6.5 3 5.9 3.2 5.9 3.2c-.3.7-.2 1.5-.1 1.9-.4.5-.6 1.2-.6 1.9 0 2.5 1.3 3.2 2.9 3.4-.4.4-.6 1-.6 1.5V14" />
    </svg>
  )
}
