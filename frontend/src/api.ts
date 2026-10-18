import type { Envelope } from './types';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fields?: Record<string, string[] | null> | null;

  constructor(status: number, code: string, message: string, fields?: Record<string, string[] | null> | null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.fields = fields;
  }
}

let csrfToken: string | null = null;

async function readEnvelope<T>(response: Response): Promise<T> {
  let body: Envelope<T> | null = null;
  try {
    body = (await response.json()) as Envelope<T>;
  } catch {
    throw new ApiError(response.status, 'INVALID_RESPONSE', 'Сервер вернул ответ, который не удалось прочитать.');
  }

  if (!response.ok || body.success !== true || body.data === null) {
    throw new ApiError(
      response.status,
      body.error?.code ?? 'REQUEST_FAILED',
      body.error?.message ?? 'Не удалось выполнить запрос.',
      body.error?.fields,
    );
  }
  return body.data;
}

export async function refreshCsrfToken(): Promise<void> {
  csrfToken = null;
  const response = await fetch('/api/auth/csrf', { credentials: 'include' });
  const data = await readEnvelope<{ requestToken: string }>(response);
  csrfToken = data.requestToken;
}

async function requireCsrfToken(): Promise<string> {
  if (!csrfToken) await refreshCsrfToken();
  if (!csrfToken) throw new ApiError(0, 'CSRF_TOKEN_MISSING', 'Не удалось получить защитный токен. Обновите страницу.');
  return csrfToken;
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase();
  const headers = new Headers(options.headers);
  if (options.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
    headers.set('X-CSRF-TOKEN', await requireCsrfToken());
  }
  const response = await fetch(path, {
    ...options,
    method,
    headers,
    credentials: 'include',
  });
  return readEnvelope<T>(response);
}

export async function signIn(login: string, password: string) {
  await refreshCsrfToken();
  const session = await api<{ id: string; login: string; name: string; role: string }>('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ login, password }),
  });
  await refreshCsrfToken();
  return session;
}

export async function signOut(): Promise<void> {
  await api<{ signedOut: boolean }>('/api/auth/logout', { method: 'POST' });
  csrfToken = null;
}

export function resetApiStateForTests() {
  csrfToken = null;
}
