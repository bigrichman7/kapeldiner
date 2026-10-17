import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, resetApiStateForTests } from './api';

afterEach(() => {
  vi.restoreAllMocks();
  resetApiStateForTests();
});

describe('API client', () => {
  it('gets a CSRF token and sends it with credentialed unsafe requests', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        success: true,
        data: { requestToken: 'token-123' },
        error: null,
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        success: true,
        data: { ok: true },
        error: null,
      }), { status: 200 }));

    await expect(api<{ ok: boolean }>('/api/example', {
      method: 'PUT',
      body: JSON.stringify({ value: 1 }),
    })).resolves.toEqual({ ok: true });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/auth/csrf');
    const options = fetchMock.mock.calls[1]?.[1] as RequestInit;
    expect(options.credentials).toBe('include');
    expect(new Headers(options.headers).get('X-CSRF-TOKEN')).toBe('token-123');
    expect(new Headers(options.headers).get('Content-Type')).toBe('application/json');
  });

  it('keeps structured API errors available to the form', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({
      success: false,
      data: null,
      error: { code: 'VALIDATION_FAILED', message: 'Исправьте данные.', fields: { title: ['Обязательно.'] } },
    }), { status: 400 }));

    await expect(api('/api/example')).rejects.toMatchObject({
      status: 400,
      code: 'VALIDATION_FAILED',
      message: 'Исправьте данные.',
      fields: { title: ['Обязательно.'] },
    });
  });
});
