const FUNC_BASE = 'https://func-kryoss.azurewebsites.net';
const API_BASE = import.meta.env.DEV ? '/api' : FUNC_BASE;
const LOGIN_URL = import.meta.env.DEV
  ? '/.auth/login/aad'
  : `${FUNC_BASE}/.auth/login/aad?post_login_redirect_uri=${encodeURIComponent(window.location.origin + '/')}`;

export async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...options?.headers },
    ...options,
  });

  if (res.status === 401) {
    window.location.href = LOGIN_URL;
    throw new Error('Unauthorized');
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    const err = new Error(body.error || res.statusText);
    (err as any).status = res.status;
    throw err;
  }

  return res.json();
}
