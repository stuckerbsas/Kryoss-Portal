import { msalInstance } from '@/auth/msalInstance';
import { loginRequest, API_BASE } from '@/auth/msalConfig';

async function getAccessToken(): Promise<string> {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) {
    throw new Error('No authenticated account');
  }

  try {
    const response = await msalInstance.acquireTokenSilent({
      ...loginRequest,
      account: accounts[0],
    });
    return response.accessToken;
  } catch {
    // Silent token acquisition failed, trigger interactive login
    const response = await msalInstance.acquireTokenPopup(loginRequest);
    return response.accessToken;
  }
}

export async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const token = await getAccessToken();

  const res = await fetch(`${API_BASE}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`,
      ...options?.headers,
    },
    ...options,
  });

  if (res.status === 401) {
    // Token expired or invalid, clear cache and retry login
    msalInstance.clearCache();
    throw new Error('Authentication required');
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    const err = new Error(body.error || res.statusText);
    (err as any).status = res.status;
    throw err;
  }

  return res.json();
}
