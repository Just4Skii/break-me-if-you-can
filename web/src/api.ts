export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

const JSON_HEADERS = { Accept: 'application/json' }

export async function fetchJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { headers: JSON_HEADERS, signal })

  if (!response.ok) {
    throw new ApiError(await readProblem(response), response.status)
  }

  return (await response.json()) as T
}

export async function postJson<T>(path: string, body: unknown, headers?: Record<string, string>): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { ...JSON_HEADERS, 'Content-Type': 'application/json', ...headers },
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
