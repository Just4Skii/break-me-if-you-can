import { formatPercent } from '../format'
import type { EvidenceState } from '../types'

/**
 * A single 270-degree gauge. The amber arc is the measured line coverage; the tick marks
 * the enforced threshold. Nothing is scaled or capped to look better than the number.
 */
export function CoverageDial({
  linePercent,
  branchPercent,
  threshold,
  state,
}: {
  linePercent?: number
  branchPercent?: number
  threshold: number
  state: EvidenceState
}) {
  const size = 164
  const stroke = 2
  const radius = 64
  const center = size / 2
  const startAngle = 135
  const sweep = 270
  const circumference = 2 * Math.PI * radius
  const arcLength = (sweep / 360) * circumference
  const measured = linePercent ?? 0
  const filled = (Math.min(100, Math.max(0, measured)) / 100) * arcLength
  const tickAngle = ((startAngle + (Math.min(100, Math.max(0, threshold)) / 100) * sweep) * Math.PI) / 180
  const tickInner = radius - 10
  const tickOuter = radius + 6

  const polar = (angleDeg: number, r: number) => {
    const angle = (angleDeg * Math.PI) / 180
    return { x: center + r * Math.cos(angle), y: center + r * Math.sin(angle) }
  }

  const start = polar(startAngle, radius)
  const end = polar(startAngle + sweep, radius)
  const tickStart = {
    x: center + tickInner * Math.cos(tickAngle),
    y: center + tickInner * Math.sin(tickAngle),
  }
  const tickEnd = {
    x: center + tickOuter * Math.cos(tickAngle),
    y: center + tickOuter * Math.sin(tickAngle),
  }

  const arcPath = 'M ' + start.x + ' ' + start.y + ' A ' + radius + ' ' + radius + ' 0 1 1 ' + end.x + ' ' + end.y
  const tone =
    state === 'failed' ? 'var(--vermillion)' : state === 'unknown' ? 'var(--stone)' : 'var(--amber)'

  return (
    <figure className="dial">
      <svg
        width={size}
        height={size}
        viewBox={'0 0 ' + size + ' ' + size}
        role="img"
        aria-label={
          linePercent === undefined
            ? 'Line coverage is unknown'
            : 'Line coverage ' + linePercent + ' percent, threshold ' + threshold + ' percent'
        }
      >
        <path d={arcPath} fill="none" stroke="var(--line)" strokeWidth={stroke} />
        <path
          d={arcPath}
          fill="none"
          stroke={tone}
          strokeWidth={stroke + 1}
          strokeDasharray={arcLength + ' ' + circumference}
          strokeDashoffset={arcLength - filled}
          strokeLinecap="butt"
        />
        <line
          x1={tickStart.x}
          y1={tickStart.y}
          x2={tickEnd.x}
          y2={tickEnd.y}
          stroke="var(--ink-3)"
          strokeWidth={1}
        />
        <text x={center} y={center + 4} textAnchor="middle" className="dial-value">
          {formatPercent(linePercent)}
        </text>
        <text x={center} y={center + 26} textAnchor="middle" className="dial-caption">
          line coverage
        </text>
      </svg>
      <figcaption className="dial-foot">
        <span>threshold {formatPercent(threshold, 0)}</span>
        <span>branch {formatPercent(branchPercent)}</span>
      </figcaption>
    </figure>
  )
}
