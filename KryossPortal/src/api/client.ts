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
    const response = await msalInstance.acquireTokenPopup(loginRequest);
    return response.accessToken;
  }
}

// Convert PascalCase keys to camelCase recursively
// The .NET backend serializes with PascalCase, frontend expects camelCase
function toCamelCase(obj: unknown): unknown {
  if (obj === null || obj === undefined || typeof obj !== 'object') return obj;
  if (Array.isArray(obj)) return obj.map(toCamelCase);
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    const camelKey = key.charAt(0).toLowerCase() + key.slice(1);
    result[camelKey] = toCamelCase(value);
  }
  return result;
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
    msalInstance.clearCache();
    throw new Error('Authentication required');
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    const err = new Error(body.error || res.statusText);
    (err as any).status = res.status;
    throw err;
  }

  const data = await res.json();
  return toCamelCase(data) as T;
}
