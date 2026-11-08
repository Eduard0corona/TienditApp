import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import {
  CHIME_TONES,
  CHIME_VOLUMES,
  previewChime,
  saveChimeSettings,
  useChimeSettings,
} from '../../hooks/useArrivalChime';

/**
 * Fase 29Z: the button next to each sound's switch (orders, requests, quotes, kitchen) that picks the tone and volume of every
 * sound of this device. Each sound still turns on or off by itself; picking a tone rings it, so it is heard before it is kept.
 */
export default function ChimeSettingsButton() {
  const [open, setOpen] = useState(false);
  const { tone, volume } = useChimeSettings();
  const id = useId();
  const titleId = `${id}-title`;
  const volumeId = `${id}-volume`;

  return (
    <>
      <button
        type="button"
        className="btn btn-sm btn-light"
        onClick={() => setOpen(true)}
        aria-haspopup="dialog"
        aria-label="Tono y volumen de los sonidos"
        title="Tono y volumen de los sonidos"
      >
        <i className="ri-equalizer-line" aria-hidden="true"></i>
      </button>
      <Modal show={open} onHide={() => setOpen(false)} centered aria-labelledby={titleId}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Tono y volumen</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="text-muted small">
            Para todos los sonidos de este dispositivo: pedidos, solicitudes, cotizaciones y cocina. Cada uno se prende o se
            apaga aparte.
          </p>
          <fieldset className="mb-3">
            <legend className="form-label fs-6 mb-1">Tono</legend>
            {CHIME_TONES.map((option) => (
              <div className="form-check" key={option.value}>
                <input
                  id={`${id}-${option.value}`}
                  className="form-check-input"
                  type="radio"
                  name={`${id}-tone`}
                  checked={tone === option.value}
                  onChange={() => {
                    saveChimeSettings({ tone: option.value });
                    // Chosen with a click or a key: the moment the browser lets it ring.
                    previewChime();
                  }}
                />
                <label className="form-check-label" htmlFor={`${id}-${option.value}`}>{option.label}</label>
              </div>
            ))}
          </fieldset>
          <label className="form-label" htmlFor={volumeId}>Volumen</label>
          <input
            id={volumeId}
            type="range"
            className="form-range"
            min={CHIME_VOLUMES.min}
            max={CHIME_VOLUMES.max}
            step={CHIME_VOLUMES.step}
            value={volume}
            aria-valuetext={`${volume} %`}
            onChange={(e) => saveChimeSettings({ volume: Number(e.target.value) })}
          />
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => previewChime()}>
            <i className="ri-play-line me-1" aria-hidden="true"></i>Probar
          </button>
          <button type="button" className="btn btn-primary" onClick={() => setOpen(false)}>Listo</button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
