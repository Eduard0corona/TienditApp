import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import SimpleBar from 'simplebar';
import { AuthProvider } from './contexts/AuthContext';
import { TenantProvider } from './contexts/TenantContext';
import App from './App';
import { applyTheme, readStoredTheme } from './utils/theme';

// Bootstrap JS (dropdowns, collapses, tooltips, etc.)
import 'bootstrap/dist/js/bootstrap.bundle.min.js';

// Icon fonts
import 'remixicon/fonts/remixicon.css';
import '@mdi/font/css/materialdesignicons.min.css';

// Hyper CSS
import './assets/css/vendor.min.css';
import './assets/css/app.min.css';
import './assets/css/overrides.css';

// Before the first render, so a dark theme does not flash light on reload.
const storedTheme = readStoredTheme();
if (storedTheme) applyTheme(storedTheme);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider>
      <TenantProvider>
        <App />
      </TenantProvider>
    </AuthProvider>
  </StrictMode>,
);

SimpleBar.initHtmlApi();
