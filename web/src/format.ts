export function formatZar(cents: number): string {
  const value = (cents / 100).toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  return 'R ' + value
}

export function formatNumber(value: number): string {
  return value.toLocaleString('en-US')
}

export function formatPercent(value: number | undefined, digits = 1): string {
  if (value === undefined || value === null || Number.isNaN(value)) return '—'
  return value.toFixed(digits) + '%'
}

export function formatDurationMs(value: number): string {
  if (value >= 1000) return (value / 1000).toFixed(2) + ' s'
  return value.toFixed(value < 10 ? 1 : 0) + ' ms'
}

export function formatAge(seconds: number | undefined): string {
  if (seconds === undefined || seconds === null) return 'unknown age'
  const abs = Math.abs(seconds)
  if (abs < 60) return Math.round(abs) + 's'
  if (abs < 3600) return Math.round(abs / 60) + ' min'
  if (abs < 86400) return (abs / 3600).toFixed(1) + ' h'
  return (abs / 86400).toFixed(1) + ' d'
}

export function formatTimestamp(value: string | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toISOString().replace('T', ' ').replace('.000Z', 'Z').replace('Z', ' UTC')
}

export function shortDigest(digest: string | undefined): string {
  if (!digest) return 'not observed'
  const [algorithm, hex] = digest.split(':')
  if (!hex || hex.length < 16) return digest
  return algorithm + ':' + hex.slice(0, 8) + '…' + hex.slice(-8)
}

export function shortSha(value: string | undefined): string {
  if (!value) return 'unknown'
  return value.length > 12 ? value.slice(0, 12) : value
}

export function stateWord(state: string): string {
  return state.charAt(0).toUpperCase() + state.slice(1)
}
