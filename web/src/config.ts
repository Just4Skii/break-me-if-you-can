const configuredBase = ((import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '')
  .trim()
  .replace(/\/+$/, '')

/**
 * The API origin for this build. Empty means "same origin", which is what the Vite
 * development proxy provides. GitHub Pages builds must set VITE_API_BASE_URL.
 */
export const apiBaseUrl = configuredBase

/** Whether this build can reach an API at all. */
export const apiConfigured =
  configuredBase.length > 0 || import.meta.env.DEV || import.meta.env.BASE_URL === '/'

export function apiUrl(path: string): string {
  if (!path.startsWith('/')) {
    throw new Error('API paths must be absolute: ' + path)
  }

  return configuredBase.length > 0 ? configuredBase + path : path
}