import type { EvidenceState } from '../types'
import { CheckIcon, CrossIcon, DashIcon } from './Icons'

export function StateMark({ state, size = 14 }: { state: EvidenceState; size?: number }) {
  if (state === 'verified') {
    return (
      <span className="state-mark state-verified" title="Verified">
        <CheckIcon size={size} />
      </span>
    )
  }

  if (state === 'failed') {
    return (
      <span className="state-mark state-failed" title="Failed">
        <CrossIcon size={size} />
      </span>
    )
  }

  return (
    <span className="state-mark state-unknown" title="Unknown">
      <DashIcon size={size} />
    </span>
  )
}
