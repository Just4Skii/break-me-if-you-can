import { shortDigest } from '../format'
import type { EvidenceState, ProofReport } from '../types'

function stateOf(report: ProofReport, digest: string | undefined, ids: string[]): EvidenceState {
  if (!digest) return 'unknown'
  const relevant = ids
    .map((id) => report.checks.find((check) => check.id === id)?.state)
    .filter((state): state is EvidenceState => Boolean(state))

  if (relevant.some((state) => state === 'failed')) return 'failed'
  if (relevant.length > 0 && relevant.every((state) => state === 'verified')) return 'verified'
  return 'unknown'
}

export function DigestChain({ report }: { report: ProofReport }) {
  const chainState = report.checks.find((check) => check.id === 'digest.chain')?.state ?? 'unknown'

  const nodes = [
    {
      key: 'built',
      label: 'Built',
      caption: 'docker build output',
      digest: report.image.builtDigest,
      state: stateOf(report, report.image.builtDigest, ['digest.build_vs_attested', 'digest.built_vs_observed']),
    },
    {
      key: 'attested',
      label: 'Attested',
      caption: 'github attestation subject',
      digest: report.image.attestedDigest,
      state: stateOf(report, report.image.attestedDigest, ['digest.build_vs_attested', 'digest.attested_vs_requested']),
    },
    {
      key: 'requested',
      label: 'Requested',
      caption: 'deploy intent record',
      digest: report.image.requestedDeploymentDigest,
      state: stateOf(report, report.image.requestedDeploymentDigest, [
        'digest.attested_vs_requested',
        'digest.requested_vs_observed',
      ]),
    },
    {
      key: 'observed',
      label: 'Observed',
      caption: 'fly machines api',
      digest: report.image.observedRunningDigest,
      state: stateOf(report, report.image.observedRunningDigest, [
        'digest.requested_vs_observed',
        'digest.built_vs_observed',
      ]),
    },
  ]

  return (
    <section className="chain" aria-label="Image digest chain">
      <div className="chain-head">
        <span className="label">Digest chain</span>
        <span className={'chain-state mono state-' + chainState}>{chainState}</span>
      </div>

      <ol className="chain-list">
        {nodes.map((node, index) => (
          <li key={node.key} className={'chain-node chain-node-' + node.state}>
            <div className="chain-rail" aria-hidden="true">
              <span className="chain-marker" />
              {index < nodes.length - 1 ? <span className="chain-line" /> : null}
            </div>
            <div className="chain-body">
              <div className="chain-title">
                <span className="chain-label">{node.label}</span>
                <span className={'chain-dot state-' + node.state} />
              </div>
              <code className="chain-digest">{shortDigest(node.digest)}</code>
              <span className="chain-caption">{node.caption}</span>
            </div>
          </li>
        ))}
      </ol>

      <p className={'chain-verdict state-' + chainState}>
        {chainState === 'verified'
          ? 'One image, from source to running machine. The circuit is complete.'
          : chainState === 'failed'
            ? 'The circuit is broken: at least two digests disagree.'
            : 'The circuit is incomplete. A digest that was not observed cannot be verified.'}
      </p>
    </section>
  )
}
