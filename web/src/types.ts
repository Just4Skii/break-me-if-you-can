export type EvidenceState = 'verified' | 'failed' | 'unknown'

export interface ProofCheck {
  id: string
  label: string
  state: EvidenceState
  detail: string
  required: boolean
}

export interface ProofReport {
  state: EvidenceState
  summary: string
  generatedAt: string
  evidenceSource: string
  evidenceFetchedAt?: string
  freshness: {
    evidenceGeneratedAt?: string
    ageSeconds?: number
    maxAgeSeconds: number
    stale: boolean
    detail?: string
  }
  source: {
    repository: string
    commitSha: string
    shortCommitSha: string
    branch: string
    runningCommitSha: string
  }
  workflow: {
    state: EvidenceState
    name: string
    file: string
    runId: string
    runAttempt: string
    runUrl: string
    event: string
    conclusion: string
  }
  build: {
    state: EvidenceState
    configuration: string
    framework: string
    dotnetVersion: string
    startedAt?: string
    completedAt?: string
    runner: string
  }
  tests: {
    state: EvidenceState
    total: number
    passed: number
    failed: number
    skipped: number
    durationSeconds: number
    assemblies: TestAssembly[]
    reports: string[]
  }
  coverage: {
    state: EvidenceState
    linePercent?: number
    branchPercent?: number
    thresholdPercent: number
    meetsThreshold?: boolean
    linesCovered?: number
    linesValid?: number
    reportArtifact?: string
  }
  dependencyAudit: {
    state: EvidenceState
    status: string
    directCount: number
    transitiveCount: number
    bySeverity: Record<string, number>
    packages: VulnerablePackage[]
    command: string
    scope: string
  }
  image: {
    reference: string
    builtDigest?: string
    attestedDigest?: string
    requestedDeploymentDigest?: string
    observedRunningDigest?: string
    verify: { verifyCommand?: string; ociReference?: string }
  }
  deployment: {
    state: EvidenceState
    health: string
    detail?: string
    observedAt?: string
    machineCreatedAt?: string
    appName?: string
    machineId?: string
    region?: string
    source: string
    sourceUrl?: string
  }
  attestation: {
    state: EvidenceState
    subjectDigest?: string
    predicateType?: string
    repository?: string
    commitSha?: string
    workflowRef?: string
    runId?: string
    observedAt?: string
    source: string
    sourceUrl?: string
    command?: string
    note?: string
  }
  securityPosture: {
    disclaimer: string
    scanScope: string
    implementedControls: string[]
    notAssessed: string[]
  }
  checks: ProofCheck[]
  notes: string[]
  verifyCommand?: string
}

export interface TestAssembly {
  assembly: string
  total: number
  passed: number
  failed: number
  skipped: number
  durationSeconds: number
}

export interface VulnerablePackage {
  id: string
  version: string
  severity: string
  advisoryUrl?: string
  transitive: boolean
}

export interface Meta {
  name: string
  commit: string
  shortCommit: string
  environment: string
  startedAt: string
  runtime: string
  repository: string
  repositoryUrl?: string
  workflowFile: string
}

export interface HealthReport {
  status: string
  durationMs: number
  checks: { name: string; status: string; durationMs: number; description?: string; data?: Record<string, unknown> }[]
}

export interface AccountView {
  id: string
  code: string
  label: string
  balanceCents: number
}

export interface ArenaLimits {
  maxRequestsPerRun: number
  maxConcurrencyPerRun: number
  maxAmountCents: number
  globalConcurrencyLimit: number
  maxRetryAttempts: number
}

export interface SandboxView {
  sandboxId: string
  scenarioKey: string
  scenarioName: string
  scenarioDescription: string
  initialBalanceCents: number
  accounts: AccountView[]
  limits: ArenaLimits
  createdAt: string
}

export interface RunReceipt {
  index: number
  status: string
  replayed: boolean
  attempts: number
  conflicts: number
  durationMs: number
  failureReason?: string
}

export interface LedgerIntegrityReport {
  balanced: boolean
  netCents: number
  completedTransfers: number
  ledgerEntries: number
  malformedTransfers: number
  negativeAccounts: number
  totalBalanceCents: number
  ok: boolean
}

export interface LatencyStats {
  minMs: number
  p50Ms: number
  p95Ms: number
  maxMs: number
  meanMs: number
}

export interface ArenaRunResult {
  runId: string
  strategy: string
  pattern: string
  idempotency: string
  requested: number
  concurrency: number
  amountCents: number
  accepted: number
  rejected: number
  replayed: number
  totalRetries: number
  conflicts: number
  durationMs: number
  latency: LatencyStats
  finalBalances: AccountView[]
  integrity: LedgerIntegrityReport
  receipts: RunReceipt[]
  startedAt: string
  completedAt: string
}

export interface LedgerEntryView {
  direction: string
  amountCents: number
  accountCode: string
}

export interface LedgerTransferView {
  transferId: string
  amountCents: number
  status: string
  strategy: string
  attempts: number
  conflicts: number
  fromCode: string
  toCode: string
  failureReason?: string
  createdAt: string
  entries: LedgerEntryView[]
}

export interface LedgerView {
  transfers: LedgerTransferView[]
  integrity: LedgerIntegrityReport
}

export interface TransferResponse {
  transferId: string
  status: string
  replayed: boolean
  strategy: string
  attempts: number
  conflicts: number
  durationMs: number
  failureReason?: string
  balances: AccountView[]
}

export interface RunRequest {
  strategy: 'optimistic' | 'pessimistic'
  requests: number
  concurrency: number
  amountCents: number
  pattern: 'alternating' | 'a_to_b'
  idempotency: 'unique' | 'replay'
  replayKeys: number
}
