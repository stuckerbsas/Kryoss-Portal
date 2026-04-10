import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { msalInstance } from './auth/msalInstance';
import App from './App';
import './index.css';

// Initialize MSAL before rendering
msalInstance.initialize().then(() => {
  // Handle redirect callback (after login redirect)
  msalInstance.handleRedirectPromise().then(() => {
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <App />
      </StrictMode>,
    );
  });
});
