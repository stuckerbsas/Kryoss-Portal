import type { Configuration } from '@azure/msal-browser';
import { LogLevel } from '@azure/msal-browser';

const clientId = import.meta.env.VITE_AZURE_CLIENT_ID
  ?? '83bd6db8-3cbb-40fa-bdd4-0ef5347b1923';

const authority = import.meta.env.VITE_AZURE_AUTHORITY
  ?? 'https://login.microsoftonline.com/840e016d-d1c4-4329-8cb0-670f2554525d';

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: 'sessionStorage',
  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
    },
  },
};

export const loginRequest = {
  scopes: [`api://${clientId}/.default`],
};

export const API_BASE = import.meta.env.VITE_API_BASE
  ?? 'https://func-kryoss.azurewebsites.net';
