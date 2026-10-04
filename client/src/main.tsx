import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from './api/queryClient';
import SimpleBar from 'simplebar';
import { AuthProvider } from './contexts/AuthContext';
import { TenantProvider } from './contexts/TenantContext';
import ToastProvider from './components/feedback/ToastProvider';
import ConfirmProvider from './components/feedback/ConfirmProvider';
import App from './App';
import { applyTheme, readStoredTheme } from './utils/theme';

// Bootstrap JS (dropdowns, collapses, tooltips, etc.)
import 'bootstrap/dist/js/bootstrap.bundle.min.js';

// Icon fonts and the app typeface (self-hosted: no request to font services)
import 'remixicon/fonts/remixicon.css';
import '@mdi/font/css/materialdesignicons.min.css';
import '@fontsource-variable/manrope';

// Hyper CSS, then the TienditApp theme and fixes on top of it
import './assets/css/vendor.min.css';
import './assets/css/app.min.css';
import './assets/css/theme.css';
import './assets/css/overrides.css';

// Before the first render, so a dark theme does not flash light on reload.
const storedTheme = readStoredTheme();
if (storedTheme) applyTheme(storedTheme);

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

SimpleBar.initHtmlApi();
