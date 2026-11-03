import { useEffect, useId, useRef, useState, type RefObject } from 'react';
import { useProblemPhotoUploads, type UploadStatus } from '../../hooks/useProblemPhotoUploads';
import {
  ACCEPTED_PHOTO_TYPES,
  MAX_PROBLEM_PHOTOS,
  forgetPreview,
  photoCount,
  pickPhotos,
  previewOf,
  type PickedPhoto,
} from '../../utils/problemPhotos';

interface PhotoButtonProps {
  label: string;
  room: number;
  disabled?: boolean;
  buttonRef?: RefObject<HTMLButtonElement | null>;
  onPicked: (picked: PickedPhoto[], refused: string | null) => void;
}

/** «Agregar fotos»: the phone offers its camera or its gallery, and only what fits is taken. */
function PhotoButton({ label, room, disabled = false, buttonRef, onPicked }: PhotoButtonProps) {
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
        ref={buttonRef}
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

function Thumb({ file, alt }: { file: File; alt: string }) {
  return <img src={previewOf(file)} alt={alt} className="rounded border d-block" width={72} height={72} style={{ objectFit: 'cover' }} />;
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
  const add = useRef<HTMLButtonElement>(null);
  // The removed photo's button goes away with it: «Agregar» takes the focus once painted, when it takes one again.
  const focusAdd = useRef(false);
  useEffect(() => {
    if (!focusAdd.current) return;
    focusAdd.current = false;
    add.current?.focus();
  }, [photos]);

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
              <Thumb file={photo.file} alt={`Foto ${index + 1} de lo que pasa`} />
              <button
                type="button"
                className="btn btn-light btn-sm position-absolute top-0 end-0 p-0 lh-1 rounded-circle border d-flex align-items-center justify-content-center"
                style={{ width: 32, height: 32, transform: 'translate(25%, -25%)' }}
                disabled={disabled}
                aria-label={`Quitar la foto ${index + 1}`}
                onClick={() => {
                  forgetPreview(photo.file);
                  setRefused(null);
                  focusAdd.current = true;
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
        buttonRef={add}
        onPicked={(picked, why) => {
          setRefused(why);
          if (picked.length > 0) onChange([...photos, ...picked]);
        }}
      />
      {refused && <div className="small text-danger mt-1" role="alert">{refused}</div>}
    </div>
  );
}

interface ProblemPhotosSenderProps {
  token: string;
  /** How many the visit has, as the page last read it from the server. */
  sent: number;
  /** The visit takes more, as the page last read it from the server (a cancelled visit, or one whose time came, does not). */
  canAdd: boolean;
  /** The photos picked in the request, sent as soon as this shows (once, also if it shows again). */
  initial?: PickedPhoto[];
}

const STATUS_LABEL: Record<UploadStatus, string> = {
  waiting: 'En espera',
  sending: 'Mandando…',
  sent: 'Enviada',
  failed: 'No se mandó',
};

const NO_PHOTOS: PickedPhoto[] = [];

/**
 * The photos of what happens going to the visit of a link (Fase 29H), one at a time, and how each went: the ones picked in
 * the request as soon as it is saved, and more from here while the visit takes them. They keep going when the customer
 * opens the visit's link, which shows the same ones; one that failed for the connection can go again. How many fit is what
 * the server last said, less the ones still going.
 */
export function ProblemPhotosSender({ token, sent, canAdd, initial = NO_PHOTOS }: ProblemPhotosSenderProps) {
  const { photos, latest, closed, store } = useProblemPhotoUploads(token);
  const [refused, setRefused] = useState<string | null>(null);
  const summaryRef = useRef<HTMLParagraphElement>(null);
  const focusSummary = useRef(false);

  useEffect(() => {
    if (initial.length > 0) store.enqueue(initial);
  }, [initial, store]);

  // A button that had the focus went away (all sent, nothing more fits): the summary takes it.
  useEffect(() => {
    if (!focusSummary.current) return;
    focusSummary.current = false;
    summaryRef.current?.focus();
  });

  const pending = photos.filter((photo) => photo.status === 'waiting' || photo.status === 'sending').length;
  const failed = photos.filter((photo) => photo.status === 'failed');
  const retryable = failed.filter((photo) => photo.retryable);
  const done = photos.filter((photo) => photo.status === 'sent').length;
  const takes = canAdd && !closed && (latest?.canAddPhotos ?? true);
  const has = Math.max(sent, latest?.problemPhotos ?? 0);
  const room = takes ? Math.max(MAX_PROBLEM_PHOTOS - has - pending - retryable.length, 0) : 0;

  const summary = pending > 0
    ? `Mandando ${photoCount(pending)}…`
    : failed.length > 0
      ? `${failed.length === 1 ? 'Una foto no se mandó' : `${failed.length} fotos no se mandaron`}: ${failed[0].error}`
      : done > 0
        ? `Listo: ${done === 1 ? 'se mandó una foto' : `se mandaron ${done} fotos`}.`
        : null;

  return (
    <div>
      <p ref={summaryRef} tabIndex={-1} className={`small ${summary ? 'mb-2' : 'mb-0'}`} aria-live="polite">{summary}</p>
      {photos.length > 0 && (
        <ul className="list-unstyled d-flex flex-wrap gap-2 mb-2">
          {photos.map((photo, index) => (
            <li key={photo.id} className="text-center" style={{ width: 72 }}>
              <Thumb file={photo.file} alt={`Foto ${index + 1} de lo que pasa`} />
              <span className={`d-block small ${photo.status === 'failed' ? 'text-danger' : 'text-muted'}`}>{STATUS_LABEL[photo.status]}</span>
              {photo.status === 'failed' && !photo.retryable && (
                <button
                  type="button"
                  className="btn btn-link btn-sm p-0"
                  aria-label={`Quitar la foto ${index + 1}, que no se mandó`}
                  onClick={() => {
                    forgetPreview(photo.file);
                    store.dismiss(photo.id);
                    focusSummary.current = true;
                  }}
                >
                  Quitar
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {room > 0 && pending === 0 && <p className="small text-muted mb-2">Caben {room} más.</p>}
      <div className="d-flex flex-wrap gap-2">
        {pending === 0 && retryable.length > 0 && (
          <button
            type="button"
            className="btn btn-outline-secondary btn-sm"
            aria-label="Reintentar las fotos que no se mandaron"
            onClick={() => {
              store.retry();
              focusSummary.current = true;
            }}
          >
            Reintentar
          </button>
        )}
        {room > 0 && (
          <PhotoButton
            label={photos.length > 0 || has > 0 ? 'Agregar más fotos' : 'Agregar fotos'}
            room={room}
            onPicked={(picked, why) => {
              setRefused(why);
              if (picked.length === 0) return;
              if (picked.length >= room) focusSummary.current = true;
              store.enqueue(picked);
            }}
          />
        )}
      </div>
      {refused && <div className="small text-danger mt-1" role="alert">{refused}</div>}
    </div>
  );
}
