import { useState } from 'react';
import { applyTheme, storeTheme } from '../../utils/theme';

const isDark = () => document.documentElement.getAttribute('data-bs-theme') === 'dark';

/** Light or dark for this device. The icon shows the mode the page is in: a moon in light mode, a sun in dark. */
export default function ThemeToggle() {
  const [dark, setDark] = useState(isDark);

  const toggle = () => {
    const next = isDark() ? 'light' : 'dark';
    applyTheme(next);
    storeTheme(next);
    setDark(next === 'dark');
  };

  return (
    <button
      type="button"
      className="nav-link border-0 bg-transparent"
      onClick={toggle}
      aria-label="Cambiar entre modo claro y oscuro"
      title="Modo claro u oscuro"
    >
      <i className={`${dark ? 'ri-sun-line' : 'ri-moon-line'} font-22`} aria-hidden="true"></i>
    </button>
  );
}
