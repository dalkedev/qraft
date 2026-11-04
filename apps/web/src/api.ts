export function errorMessage(error: unknown): string {
  if (error instanceof DOMException && error.name === 'TimeoutError') return 'Serwis nie odpowiedział w ciągu 8 sekund.'
  return error instanceof Error ? error.message : 'Nie udało się połączyć z serwisem.'
}
function requestSignal(signal: AbortSignal): AbortSignal {
  return AbortSignal.any([signal, AbortSignal.timeout(8_000)])
}
export async function requestJson<T>(
  path: string, parser: (value: unknown) => T, signal: AbortSignal,
  options?: { method: 'POST'; body?: unknown },
): Promise<T> {
  const response = await fetch(path, {
    signal: requestSignal(signal),
    method: options?.method ?? 'GET',
    headers: { Accept: 'application/json', ...(options?.body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: options?.body !== undefined ? JSON.stringify(options.body) : undefined,
  })
  if (!response.ok) {
    let detail = ''
    if (response.headers.get('content-type')?.includes('json')) {
      const problem: unknown = await response.json()
      if (problem && typeof problem === 'object') {
        const p = problem as Record<string, unknown>
        detail = typeof p.detail === 'string' ? p.detail : typeof p.message === 'string' ? p.message : typeof p.title === 'string' ? p.title : ''
      }
    }
    throw new Error(detail || `Serwis zwrócił błąd HTTP ${response.status}. Sprawdź, czy jest uruchomiony.`)
  }
  return parser(await response.json())
}
export interface ServiceHealth {
  state: 'checking' | 'ready' | 'degraded' | 'offline'
  label: string
  details: string
}
export async function checkHealth(path: string, signal: AbortSignal): Promise<ServiceHealth> {
  try {
    const response = await fetch(path, { signal: requestSignal(signal), headers: { Accept: 'application/json' } })
    const payload: unknown = await response.json()
    const state = payload && typeof payload === 'object' ? (payload as Record<string, unknown>).status : undefined
    const ready = response.ok && typeof state === 'string' && ['ok', 'ready', 'healthy'].includes(state.toLowerCase())
    return {
      state: ready ? 'ready' : 'degraded',
      label: ready ? 'Gotowe' : 'Niegotowe',
      details: `HTTP ${response.status}\n${JSON.stringify(payload, null, 2)}`,
    }
  } catch (error) {
    if (signal.aborted) throw error
    return { state: 'offline', label: 'Brak połączenia', details: errorMessage(error) }
  }
}
