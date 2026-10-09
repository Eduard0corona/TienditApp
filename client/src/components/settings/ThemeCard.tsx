import { useEffect, useState, type CSSProperties } from 'react';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { BusinessTheme } from '../../types';
import { BUSINESS_THEMES, getBusinessTheme, type BusinessThemeDefinition } from '../../utils/businessThemes';
import { previewBusinessTheme } from '../../utils/theme';
import './ThemeCard.css';

function swatchStyle(definition: BusinessThemeDefinition): CSSProperties {
  return {
    '--swatch-page': definition.light.pageBg,
    '--swatch-accent': definition.light.accent,
    '--swatch-on-accent': definition.light.onAccent,
    '--swatch-subtle': definition.light.accentSubtle,
    '--swatch-text': definition.light.accentText,
    '--swatch-dark-accent': definition.dark.accent,
  } as CSSProperties;
}

/**
 * Apariencia: the colors of the business for its whole team and its public menu (owners and admins, like the rest of
 * this page). Picking a theme shows it on the whole app right away; Guardar keeps it for everyone, Descartar or
 * leaving the page goes back to the saved one.
 */
export default function ThemeCard() {
  const { tenant, refreshTenant } = useTenant();
  const toast = useToast();
  const [choice, setChoice] = useState<BusinessTheme | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (choice === null) return undefined;
    previewBusinessTheme(choice);
    return () => previewBusinessTheme(undefined);
  }, [choice]);

  if (!tenant) return null;

  const saved = getBusinessTheme(tenant.theme).theme;
  const selected = choice ?? saved;
  const dirty = choice !== null && choice !== saved;

  const save = async () => {
    if (choice === null) return;
    setSaving(true);
    try {
      await apiRequest('put', '/api/v1/auth/my-tenant/theme', { body: { theme: choice } });
      await refreshTenant();
      setChoice(null);
      toast.success(`Tema ${getBusinessTheme(choice).name} guardado: tu equipo y tu menú en línea ya lo ven.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar el tema.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-1">Apariencia</h4>
        <p className="text-muted small mb-3">
          El color de tu negocio en la app de tu equipo y en tu menú en línea. Toca un tema para verlo en toda la app
          antes de guardarlo.
        </p>

        <fieldset disabled={saving}>
          <legend className="visually-hidden">Tema del negocio</legend>
          <div className="theme-options">
            {BUSINESS_THEMES.map((definition) => {
              const isSelected = selected === definition.theme;
              return (
                <label key={definition.theme} className={`theme-option ${isSelected ? 'selected' : ''}`}>
                  <input
                    type="radio"
                    name="business-theme"
                    className="visually-hidden"
                    value={definition.theme}
                    checked={isSelected}
                    onChange={() => setChoice(definition.theme === saved ? null : definition.theme)}
                  />
                  <span className="theme-swatch" style={swatchStyle(definition)} aria-hidden="true">
                    <span className="theme-swatch-button">Cobrar</span>
                    <span className="theme-swatch-chip">Elegido</span>
                    <span className="theme-swatch-dark"></span>
                  </span>
                  <span className="theme-option-name">
                    {definition.name}
                    {saved === definition.theme && <span className="badge bg-secondary ms-2">Actual</span>}
                  </span>
                  <span className="theme-option-description">{definition.description}</span>
                </label>
              );
            })}
          </div>
        </fieldset>

        <p className="small text-muted mt-3 mb-0">
          Los colores de los estados (pendiente, en preparación, listo, cancelado) no cambian con el tema, para que nadie
          se confunda. El modo oscuro lo elige cada persona en su dispositivo, con la luna de la barra superior.
        </p>

        {dirty && (
          <div className="d-flex flex-wrap gap-2 mt-3" role="group" aria-label="Cambio de tema">
            <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={saving}>
              {saving ? 'Guardando...' : `Guardar tema ${getBusinessTheme(choice).name}`}
            </button>
            <button type="button" className="btn btn-light" onClick={() => setChoice(null)} disabled={saving}>
              Descartar
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
