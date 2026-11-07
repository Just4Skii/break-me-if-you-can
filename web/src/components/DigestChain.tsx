import { shortDigest } from '../format'
import type { EvidenceState, ProofReport } from '../types'

/** A node is verified when the link that binds it to its neighbour is verified. */
function linkState(report: ProofReport, digest: string | undefined, id: string): EvidenceState {
  if (!digest) return 'unknown'

  const state = report.checks.find((check) => check.id === id)?.state
  if (state === 'failed') return 'failed'
  return state === 'verified' ? 'verified' : 'unknown'
}

export function DigestChain({ report }: { report: ProofReport }) {
  const chainState = report.checks.find((check) => check.id === 'digest.chain')?.state ?? 'unknown'

  const nodes = [
    {
      key: 'built',
      label: 'Built',
      caption: 'docker build output',
      digest: report.image.builtDigest,
      state: linkState(report, report.image.builtDigest, 'digest.build_vs_attested'),
    },
    {
      key: 'attested',
      label: 'Attested',
      caption: 'github attestation subject',
      digest: report.image.attestedDigest,
      state: linkState(report, report.image.attestedDigest, 'digest.build_vs_attested'),
    },
    {
      key: 'requested',
      label: 'Requested',
      caption: 'deploy intent record',
      digest: report.image.requestedDeploymentDigest,
      state: linkState(report, report.image.requestedDeploymentDigest, 'digest.attested_vs_requested'),
    },
    {
      key: 'observed',
      label: 'Observed',
      caption: 'fly machines api',
      digest: report.image.observedRunningDigest,
      state: linkState(report, report.image.observedRunningDigest, 'digest.requested_vs_observed'),
    },
  ]

  return (
    <section className="chain" aria-label="Image digest chain">
      <div className="chain-head">
        <span className="label">Digest chain</span>
        <span className={'chain-state mono state-' + chainState}>{chainState}</span>
      </div>

      <ol className="chain-list">
        {nodes.map((node, index) => {
          const next = nodes[index + 1]
          const gap = next !== undefined && !next.digest

          return (
            <li key={node.key} className={'chain-node chain-node-' + node.state}>
              <span
                className={
                  'chain-track chain-track-' + node.state + (gap ? ' chain-track-gap' : '')
                }
                aria-hidden="true"
              />
              <span className={'chain-marker' + (node.digest ? ' is-present' : ' is-missing')} aria-hidden="true" />
              <div className="chain-body">
                <div className="chain-title">
                  <span className="chain-label">{node.label}</span>
                  <span className={'chain-dot state-' + node.state} />
                </div>
                <code className="chain-digest" title={node.digest ?? undefined}>
                  {shortDigest(node.digest)}
                </code>
                <span className="chain-caption">{node.caption}</span>
              </div>
            </li>
          )
        })}

        <li className={'chain-node chain-node-final chain-node-' + chainState}>
          <span className="chain-marker chain-marker-final" aria-hidden="true" />
          <div className="chain-body">
            <span className="chain-label">Verdict</span>
            <code className={'chain-digest chain-verdict-word state-' + chainState}>{chainState}</code>
            <span className="chain-caption">all required checks</span>
          </div>
        </li>
      </ol>

      <p className={'chain-verdict state-' + chainState}>
        {chainState === 'verified'
          ? 'One image, from source to running machine. The circuit is complete.'
          : chainState === 'failed'
            ? 'The circuit is broken: at least two digests disagree.'
            : 'The chain stops at the last value that was actually observed. Nothing beyond it can be claimed.'}
      </p>
    </section>
  )
}
