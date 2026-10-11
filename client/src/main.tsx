import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from './api/queryClient';
import { AuthProvider } from './contexts/AuthContext';
import { TenantProvider } from './contexts/TenantContext';
import ToastProvider from './components/feedback/ToastProvider';
import ConfirmProvider from './components/feedback/ConfirmProvider';
import App from './App';
import { applyTheme, readStoredTheme, restoreBusinessTheme } from './utils/theme';
import { reloadOnceForNewVersion } from './utils/chunkReload';

// Bootstrap JS (dropdowns, collapses, tooltips, etc.)
import 'bootstrap/dist/js/bootstrap.bundle.min.js';

// Icon fonts and the app typeface (self-hosted: no request to font services)
import 'remixicon/fonts/remixicon.css';
import '@fontsource-variable/manrope';

// Hyper CSS and the classes the app took from it, then the TienditApp theme, the frame of the app and fixes on top
import './assets/css/app.min.css';
import './assets/css/components.css';
import './assets/css/theme.css';
import './assets/css/shell.css';
import './assets/css/overrides.css';

// After a deploy, an open page asks for files that no longer exist: load the new version instead of failing.
window.addEventListener('vite:preloadError', (event) => {
  if (reloadOnceForNewVersion()) event.preventDefault();
});

// Before the first render, so a dark theme does not flash light on reload, nor the business colors flash green.
const storedTheme = readStoredTheme();
if (storedTheme) applyTheme(storedTheme);
restoreBusinessTheme();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <TenantProvider>
          <ToastProvider>
            <ConfirmProvider>
              <App />
            </ConfirmProvider>
          </ToastProvider>
        </TenantProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
