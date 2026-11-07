import { useState } from 'react'
import { CopyIcon, CheckIcon } from './Icons'

export function CopyButton({ value, label = 'Copy' }: { value: string; label?: string }) {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1600)
    } catch {
      setCopied(false)
    }
  }

  return (
    <button type="button" className="btn btn-ghost copy-button" onClick={copy} aria-live="polite">
      {copied ? <CheckIcon size={13} /> : <CopyIcon size={13} />}
      <span>{copied ? 'Copied' : label}</span>
    </button>
  )
}
