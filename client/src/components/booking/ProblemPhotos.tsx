import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import { publicMenuApi, type CustomerAppointment } from '../../api/publicMenuApi';
import {
  ACCEPTED_PHOTO_TYPES,
  MAX_PROBLEM_PHOTOS,
  forgetPhotos,
  photoCount,
  pickPhotos,
  type PickedPhoto,
} from '../../utils/problemPhotos';

interface PhotoButtonProps {
  label: string;
  room: number;
  disabled?: boolean;
  onPicked: (picked: PickedPhoto[], refused: string | null) => void;
}

/** «Agregar fotos»: the phone offers its camera or its gallery, and only what fits is taken. */
function PhotoButton({ label, room, disabled = false, onPicked }: PhotoButtonProps) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <>
      <input
        ref={input}
        type="file"
        accept={ACCEPTED_PHOTO_TYPES.join(',')}
        multiple
        hidden
        tabIndex={-1}
        aria-hidden="true"
        data-testid="problem-photos-input"
        onChange={(event) => {
          const files = Array.from(event.target.files ?? []);
          // The same photo picked again is a new pick.
          event.target.value = '';
          if (files.length === 0) return;
          const { picked, refused } = pickPhotos(files, room);
          onPicked(picked, refused);
        }}
      />
      <button
        type="button"
        className="btn btn-outline-secondary btn-sm"
        disabled={disabled || room <= 0}
        onClick={() => input.current?.click()}
      >
        <i className="ri-camera-line me-1" aria-hidden="true"></i>{label}
      </button>
    </>
  );
}

interface ProblemPhotosPickerProps {
  photos: PickedPhoto[];
  onChange: (photos: PickedPhoto[]) => void;
  businessName: string;
  disabled?: boolean;
}

/**
 * In the request (Fase 29H): the customer picks up to five photos of what happens and sees them before sending; they go
 * once the request is saved, with its link.
 */
export function ProblemPhotosPicker({ photos, onChange, businessName, disabled = false }: ProblemPhotosPickerProps) {
  const id = useId();
  const [refused, setRefused] = useState<string | null>(null);

  return (
    <div className="mb-2" role="group" aria-labelledby={`${id}-label`} aria-describedby={`${id}-hint`}>
      <div id={`${id}-label`} className="form-label small mb-1">Fotos de lo que pasa (opcional)</div>
      <div id={`${id}-hint`} className="form-text mt-0 mb-2">
        Hasta {MAX_PROBLEM_PHOTOS} fotos de hasta 10 MB. Solo las ve {businessName}.
      </div>
      {photos.length > 0 && (
        <ul className="list-unstyled d-flex flex-wrap gap-2 mb-2">
          {photos.map((photo, index) => (
            <li key={photo.id} className="position-relative">
              <img src={photo.preview} alt={`Foto ${index + 1} de lo que pasa`} className="rounded border" width={72} height={72} style={{ objectFit: 'cover' }} />
              <button
                type="button"
                className="btn btn-light btn-sm position-absolute top-0 end-0 p-0 lh-1 rounded-circle border"
                style={{ width: 24, height: 24 }}
                disabled={disabled}
                aria-label={`Quitar la foto ${index + 1}`}
                onClick={() => {
                  forgetPhotos([photo]);
                  setRefused(null);
                  onChange(photos.filter((item) => item.id !== photo.id));
                }}
              >
                <i className="ri-close-line" aria-hidden="true"></i>
              </button>
            </li>
          ))}
        </ul>
      )}
      <PhotoButton
        label={photos.length > 0 ? 'Agregar más fotos' : 'Agregar fotos'}
        room={MAX_PROBLEM_PHOTOS - photos.length}
        disabled={disabled}
        onPicked={(picked, why) => {
          setRefused(why);
          if (picked.length > 0) onChange([...photos, ...picked]);
        }}
      />
      {refused && <div className="small text-danger mt-1" role="alert">{refused}</div>}
    </div>
  );
}

type SendStatus = 'waiting' | 'sending' | 'sent' | 'failed';

interface Sending extends PickedPhoto {
  status: SendStatus;
  error?: string;
  /** It may go again: the connection or the server failed, not a refusal of the visit. */
  retryable?: boolean;
}

interface ProblemPhotosSenderProps {
  token: string;
  /** How many the visit already has, as the server said. */
  sent: number;
  /** The visit still takes photos, as the server said. */
  canAdd: boolean;
  /** The photos picked in the request, sent as soon as this shows. */
  initial?: PickedPhoto[];
  /** The visit as the server answered the last photo it took. */
  onSent: (appointment: CustomerAppointment) => void;
}

const STATUS_LABEL: Record<SendStatus, string> = {
  waiting: 'En espera',
  sending: 'Enviando…',
  sent: 'Enviada',
  failed: 'No se envió',
};

const NO_PHOTOS: PickedPhoto[] = [];

/**
 * Sends the photos of what happens with the link of the visit (Fase 29H), one at a time: the ones picked in the request as
 * soon as it is saved, and more from here while the visit takes them. Each says how it went; one that failed for the
 * connection can go again.
 */
export function ProblemPhotosSender({ token, sent, canAdd, initial = NO_PHOTOS, onSent }: ProblemPhotosSenderProps) {
  const [queue, setQueue] = useState<Sending[]>(() => initial.map((photo) => ({ ...photo, status: 'waiting' })));
  const [refused, setRefused] = useState<string | null>(null);
  const [allowed, setAllowed] = useState({ sent, canAdd });
  const waiting = useRef<PickedPhoto[]>([]);
  const busy = useRef(false);
  const started = useRef(false);
  const onSentRef = useRef(onSent);
  useEffect(() => {
    onSentRef.current = onSent;
  });

  const mark = (id: string, change: Partial<Sending>) =>
    setQueue((current) => current.map((photo) => (photo.id === id ? { ...photo, ...change } : photo)));

  // One at a time, in the order picked: the server gives each the next place.
  const send = useCallback(async (photos: PickedPhoto[]) => {
    waiting.current.push(...photos);
    if (busy.current) return;
    busy.current = true;
    for (let next = waiting.current.shift(); next; next = waiting.current.shift()) {
      const { id, file } = next;
      mark(id, { status: 'sending', error: undefined });
      try {
        const appointment = await publicMenuApi.addAppointmentPhoto(token, file);
        setAllowed({ sent: appointment.problemPhotos, canAdd: appointment.canAddPhotos });
        mark(id, { status: 'sent' });
        onSentRef.current(appointment);
      } catch (err: unknown) {
        const status = getApiErrorStatus(err);
        mark(id, {
          status: 'failed',
          error: getApiErrorMessage(err, 'Revisa tu conexión e inténtalo otra vez.'),
          retryable: status === undefined || status === 429 || status >= 500,
        });
      }
    }
    busy.current = false;
  }, [token]);

  // The photos of the request go once, as soon as it is saved (also when the page mounts twice while developing).
  useEffect(() => {
    if (started.current || initial.length === 0) return;
    started.current = true;
    void Promise.resolve().then(() => send(initial));
  }, [initial, send]);

  const pending = queue.filter((photo) => photo.status === 'waiting' || photo.status === 'sending').length;
  const failed = queue.filter((photo) => photo.status === 'failed');
  const retryable = failed.filter((photo) => photo.retryable);
  const done = queue.filter((photo) => photo.status === 'sent').length;
  const room = allowed.canAdd ? MAX_PROBLEM_PHOTOS - allowed.sent - pending - retryable.length : 0;

  const summary = pending > 0
    ? `Enviando tus fotos (${done + 1} de ${done + pending})…`
    : failed.length > 0
      ? `${failed.length === 1 ? 'Una foto no se envió' : `${failed.length} fotos no se enviaron`}: ${failed[0].error}`
      : done > 0
        ? `Enviaste ${photoCount(done)} de lo que pasa.`
        : null;

  return (
    <div>
      <p className={`small ${summary ? 'mb-2' : 'mb-0'}`} aria-live="polite">{summary}</p>
      {queue.length > 0 && (
        <ul className="list-unstyled d-flex flex-wrap gap-2 mb-2">
          {queue.map((photo, index) => (
            <li key={photo.id} className="text-center" style={{ width: 72 }}>
              <img src={photo.preview} alt={`Foto ${index + 1} de lo que pasa`} className="rounded border d-block" width={72} height={72} style={{ objectFit: 'cover' }} />
              <span className={`d-block small ${photo.status === 'failed' ? 'text-danger' : 'text-muted'}`}>{STATUS_LABEL[photo.status]}</span>
            </li>
          ))}
        </ul>
      )}
      <div className="d-flex flex-wrap gap-2">
        {pending === 0 && retryable.length > 0 && (
          <button
            type="button"
            className="btn btn-outline-secondary btn-sm"
            onClick={() => {
              for (const photo of retryable) mark(photo.id, { status: 'waiting', error: undefined });
              void send(retryable);
            }}
          >
            Reintentar
          </button>
        )}
        {room > 0 && (
          <PhotoButton
            label={queue.length > 0 || allowed.sent > 0 ? 'Agregar más fotos' : 'Agregar fotos'}
            room={room}
            onPicked={(picked, why) => {
              setRefused(why);
              if (picked.length === 0) return;
              setQueue((current) => [...current, ...picked.map((photo) => ({ ...photo, status: 'waiting' as const }))]);
              void send(picked);
            }}
          />
        )}
      </div>
      {refused && <div className="small text-danger mt-1" role="alert">{refused}</div>}
    </div>
  );
}
