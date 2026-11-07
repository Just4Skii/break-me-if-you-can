export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

import { apiUrl } from './config'
import { getSandboxToken } from './session'
import { getStoredPlayer } from './auth'

function requestHeaders(): Record<string, string> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const token = getSandboxToken()
  if (token) {
    headers['X-Sandbox-Token'] = token
  }

  const player = getStoredPlayer()
  if (player) {
    headers.Authorization = 'Bearer ' + player.token
  }

  return headers
}

export async function fetchJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(apiUrl(path), { headers: requestHeaders(), signal })

  if (!response.ok) {
    throw new ApiError(await readProblem(response), response.status)
  }

  return (await response.json()) as T
}

export async function postJson<T>(path: string, body: unknown, headers?: Record<string, string>): Promise<T> {
  const response = await fetch(apiUrl(path), {
    method: 'POST',
    headers: { ...requestHeaders(), 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(body),
  })

  if (!response.ok) {
    throw new ApiError(await readProblem(response), response.status)
  }

  return (await response.json()) as T
}

async function readProblem(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { detail?: string; title?: string }
    return body.detail ?? body.title ?? 'Request failed with status ' + response.status
  } catch {
    return 'Request failed with status ' + response.status
  }
}
