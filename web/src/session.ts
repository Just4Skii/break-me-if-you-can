const TOKEN_KEY = 'bm.sandbox.token'

/**
 * The API protects the anonymous sandbox identity with Data Protection. A cookie works
 * for same-origin deployments; a cross-origin client (GitHub Pages calling a separate API)
 * cannot rely on third-party cookies, so the same protected value travels in an
 * X-Sandbox-Token header instead.
 */
export function getSandboxToken(): string | null {
  try {
    return window.localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

export function setSandboxToken(token: string | null | undefined): void {
  try {
    if (token) {
      window.localStorage.setItem(TOKEN_KEY, token)
    } else {
      window.localStorage.removeItem(TOKEN_KEY)
    }
  } catch {
    // Private browsing or a blocked storage partition: the cookie path still works same-origin.
  }
}
