import { useCallback, useEffect, useState } from 'react'
import { ApiError, fetchJson } from '../api'
import { apiConfigured } from '../config'

export type AsyncState<T> =
  | { status: 'loading' }
  | { status: 'success'; data: T }
  | { status: 'error'; error: string }

export function useApi<T>(path: string | null, refreshKey = 0): AsyncState<T> & { reload: () => void } {
  const [state, setState] = useState<AsyncState<T>>({ status: 'loading' })
  const [manualKey, setManualKey] = useState(0)

  const reload = useCallback(() => setManualKey((key) => key + 1), [])

  useEffect(() => {
    if (!path) return

    if (!apiConfigured) {
      setState({
        status: 'error',
        error: 'The API endpoint is not configured for this static build. Set VITE_API_BASE_URL and redeploy.',
      })
      return
    }

    const controller = new AbortController()
    let active = true

    setState({ status: 'loading' })

    fetchJson<T>(path, controller.signal)
      .then((data) => {
        if (active) setState({ status: 'success', data })
      })
      .catch((error: unknown) => {
        if (!active) return
        if (error instanceof DOMException && error.name === 'AbortError') return
        const message = error instanceof ApiError ? error.message : 'The API could not be reached.'
        setState({ status: 'error', error: message })
      })

    return () => {
      active = false
      controller.abort()
    }
  }, [path, refreshKey, manualKey])

  return { ...state, reload }
}
