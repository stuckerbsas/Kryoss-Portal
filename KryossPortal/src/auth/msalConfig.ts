import type { Configuration } from '@azure/msal-browser';
import { LogLevel } from '@azure/msal-browser';

export const msalConfig: Configuration = {
  auth: {
    clientId: '83bd6db8-3cbb-40fa-bdd4-0ef5347b1923',
    authority: 'https://login.microsoftonline.com/840e016d-d1c4-4329-8cb0-670f2554525d',
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
  scopes: [`api://83bd6db8-3cbb-40fa-bdd4-0ef5347b1923/.default`],
};

export const API_BASE = import.meta.env.DEV
  ? 'http://localhost:7071'
  : 'https://func-kryoss.azurewebsites.net';
