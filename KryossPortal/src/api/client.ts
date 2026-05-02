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

const genericMessages: Record<number, string> = {
  400: 'Invalid request',
  401: 'Authentication required',
  403: 'Access denied',
  404: 'Not found',
  409: 'Conflict',
  429: 'Too many requests',
};

export class ApiError extends Error {
  status: number;
  traceId?: string;

  constructor(message: string, status: number, traceId?: string) {
    super(message);
    this.status = status;
    this.traceId = traceId;
  }
}

export function qs(params: Record<string, string | number | boolean | undefined | null>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v != null) sp.set(k, String(v));
  }
  return sp.toString() ? `?${sp}` : '';
}

async function getFreshAccessToken(): Promise<string> {
  const response = await msalInstance.acquireTokenPopup({
    ...loginRequest,
    prompt: 'login',
  });
  return response.accessToken;
}

async function apiFetchWithToken<T>(path: string, token: string, options?: RequestInit): Promise<T> {
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
    throw new ApiError('Authentication required', 401);
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    const traceId = (body as Record<string, unknown>)?.traceId as string | undefined;
    const errorField = (body as Record<string, unknown>)?.error as string | undefined;
    if (res.status === 403 && errorField === 'fresh_auth_required')
      throw new ApiError('Re-authentication required', 403);
    const msg = errorField || genericMessages[res.status] || 'Request failed';
    throw new ApiError(
      traceId ? `${msg} — ref: ${traceId}` : msg,
      res.status,
      traceId,
    );
  }

  if (res.status === 204) return undefined as T;

  const data = await res.json();
  return toCamelCase(data) as T;
}

export async function apiFetchFresh<T>(path: string, options?: RequestInit): Promise<T> {
  const token = await getFreshAccessToken();
  return apiFetchWithToken<T>(path, token, options);
}

export async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const token = await getAccessToken();
  return apiFetchWithToken<T>(path, token, options);
}
