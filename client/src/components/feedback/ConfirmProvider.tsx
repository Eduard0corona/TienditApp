import { useCallback, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { Modal } from 'react-bootstrap';
import { ConfirmContext, type ConfirmApi, type ConfirmOptions, type PromptOptions } from './feedbackContexts';

type Request =
  | { kind: 'confirm'; options: ConfirmOptions; resolve: (value: boolean) => void }
  | { kind: 'prompt'; options: PromptOptions; resolve: (value: string | null) => void };

/**
 * In-app confirmation and text prompts that replace window.confirm/prompt: they name what is affected,
 * follow the theme and keep keyboard focus inside the dialog.
 */
export default function ConfirmProvider({ children }: { children: ReactNode }) {
  const titleId = useId();
  const [request, setRequest] = useState<Request | null>(null);
  const [value, setValue] = useState('');
  const settled = useRef(false);

  const confirm = useCallback(
    (options: ConfirmOptions) =>
      new Promise<boolean>((resolve) => {
        settled.current = false;
        setValue('');
        setRequest({ kind: 'confirm', options, resolve });
      }),
    [],
  );

  const prompt = useCallback(
    (options: PromptOptions) =>
      new Promise<string | null>((resolve) => {
        settled.current = false;
        setValue(options.defaultValue ?? '');
        setRequest({ kind: 'prompt', options, resolve });
      }),
    [],
  );

  const api = useMemo<ConfirmApi>(() => ({ confirm, prompt }), [confirm, prompt]);

  const close = (accepted: boolean) => {
    if (!request || settled.current) return;
    settled.current = true;
    if (request.kind === 'confirm') {
      request.resolve(accepted);
    } else {
      request.resolve(accepted ? value.trim() : null);
    }
    setRequest(null);
  };

  const options = request?.options;
  const promptOptions = request?.kind === 'prompt' ? request.options : null;
  const canConfirm = !promptOptions?.required || value.trim().length > 0;

  return (
    <ConfirmContext.Provider value={api}>
      {children}
      <Modal show={!!request} onHide={() => close(false)} centered aria-labelledby={titleId}>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (canConfirm) close(true);
          }}
        >
          <Modal.Header closeButton>
            <Modal.Title id={titleId} as="h5">{options?.title}</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            {options?.message && <div className={promptOptions ? 'mb-3' : undefined}>{options.message}</div>}
            {promptOptions && (
              <>
                <label className="form-label" htmlFor="confirm-dialog-input">{promptOptions.inputLabel}</label>
                {promptOptions.multiline ? (
                  <textarea
                    id="confirm-dialog-input"
                    className="form-control"
                    rows={3}
                    autoFocus
                    placeholder={promptOptions.placeholder}
                    maxLength={promptOptions.maxLength}
                    value={value}
                    onChange={(event) => setValue(event.target.value)}
                  />
                ) : (
                  <input
                    id="confirm-dialog-input"
                    className="form-control"
                    autoFocus
                    placeholder={promptOptions.placeholder}
                    maxLength={promptOptions.maxLength}
                    value={value}
                    onChange={(event) => setValue(event.target.value)}
                  />
                )}
              </>
            )}
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light btn-touch" onClick={() => close(false)}>
              {options?.cancelLabel ?? 'Cancelar'}
            </button>
            <button
              type="submit"
              className={`btn btn-touch ${options?.danger ? 'btn-danger' : 'btn-primary'}`}
              disabled={!canConfirm}
              autoFocus={!promptOptions}
            >
              {options?.confirmLabel ?? 'Confirmar'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>
    </ConfirmContext.Provider>
  );
}
