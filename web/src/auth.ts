export interface StoredPlayer {
  token: string
  username: string
}

const KEY = 'bm.player'

export function getStoredPlayer(): StoredPlayer | null {
  try {
    const raw = window.localStorage.getItem(KEY)
    if (!raw) return null

    const parsed = JSON.parse(raw) as Partial<StoredPlayer>
    if (!parsed.token || !parsed.username) return null

    return { token: parsed.token, username: parsed.username }
  } catch {
    return null
  }
}

export function setStoredPlayer(player: StoredPlayer): void {
  try {
    window.localStorage.setItem(KEY, JSON.stringify(player))
  } catch {
    // Storage can be unavailable; the form still reports a successful sign-in.
  }
}

export function clearStoredPlayer(): void {
  try {
    window.localStorage.removeItem(KEY)
  } catch {
    // Nothing to clear.
  }
}
