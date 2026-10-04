interface BrandLogoProps {
  /** `full` shows the icon and the name; `icon` fits the condensed menu and small headers. */
  variant?: 'full' | 'icon';
  /** `light` for dark backgrounds (dark menu, colored headers), `dark` for light ones. */
  tone?: 'light' | 'dark';
}

// TienditApp mark, drawn inline so it follows the page font and colors. Shown wherever the business
// has not uploaded its own logo.
export default function BrandLogo({ variant = 'full', tone = 'dark' }: BrandLogoProps) {
  return (
    <span className={`brand-logo brand-logo-${tone}`} role="img" aria-label="TienditApp">
      <svg className="brand-logo-icon" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
        <rect width="32" height="32" rx="8" fill="#0E7A5C" />
        <path
          d="M8 9h16l2 5a2.5 2.5 0 0 1-5 0a2.5 2.5 0 0 1-5 0a2.5 2.5 0 0 1-5 0a2.5 2.5 0 0 1-5 0z"
          fill="#fff"
        />
        <path d="M9 17v7h14v-7M14 24v-4.5h4V24" fill="none" stroke="#fff" strokeWidth="2" strokeLinejoin="round" />
      </svg>
      {variant === 'full' && (
        <span className="brand-logo-text" aria-hidden="true">
          Tiendit<span className="brand-logo-accent">App</span>
        </span>
      )}
    </span>
  );
}
