import { useId, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ordersApi } from '../../api/ordersApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import PrivatePhoto from '../common/PrivatePhoto';
import { ServicePhotoStage, type OrderStatus, type ServiceOrderPhotoDto, type UploadedFileDto } from '../../types';
import { describeServicePhoto, SERVICE_PHOTO_NOTE_MAX_LENGTH, servicePhotosQueryKey } from '../../utils/servicePhotos';

const STAGES = [
  { stage: ServicePhotoStage.Before, title: 'Antes', option: 'Antes del servicio' },
  { stage: ServicePhotoStage.After, title: 'Después', option: 'Después del servicio' },
] as const;

interface ServicePhotosPanelProps {
  orderId: string;
  /**
   * The status of the order as the screen shows it: when it changes (delivered, cancelled), the photos are asked for
   * again, since what may be added or removed changes with it.
   */
  orderStatus?: OrderStatus;
  /** Side of each thumbnail, in pixels. */
  thumbnailSize?: number;
  /** Level of the headings of each stage, below the heading of the section that holds the panel. */
  headingLevel?: 4 | 5 | 6;
}

/**
 * The photos of a service for the business's team (Fase 25, ADR 0024): before and after, the ones taken when the order
 * was received and the ones added later, each with its note and who took it. The customer does not see them.
 */
export default function ServicePhotosPanel({ orderId, orderStatus, thumbnailSize = 72, headingLevel = 4 }: ServicePhotosPanelProps) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { confirm } = useConfirm();
  const formId = useId();
  const fileInput = useRef<HTMLInputElement>(null);
  const [stage, setStage] = useState<ServicePhotoStage>(ServicePhotoStage.After);
  const [note, setNote] = useState('');
  const [file, setFile] = useState<File | null>(null);
  // The upload of the chosen photo, kept so trying again after the server refused it does not upload it twice.
  const [uploaded, setUploaded] = useState<{ file: File; upload: UploadedFileDto } | null>(null);
  const [formError, setFormError] = useState('');
  const Heading = `h${headingLevel}` as const;

  const photosQuery = useQuery({
    queryKey: [...servicePhotosQueryKey(orderId), orderStatus ?? null],
    queryFn: async () => (await ordersApi.getServicePhotos(orderId)).data,
    // Opening the detail again shows what others added or removed meanwhile.
    refetchOnMount: 'always',
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: servicePhotosQueryKey(orderId) });

  const clearForm = () => {
    setNote('');
    setFile(null);
    setUploaded(null);
    if (fileInput.current) fileInput.current.value = '';
  };

  const add = useMutation({
    mutationFn: async ({ photo, stage: photoStage, note: photoNote }: { photo: File; stage: ServicePhotoStage; note: string }) => {
      let upload = uploaded?.file === photo ? uploaded.upload : null;
      if (!upload) {
        upload = (await ordersApi.uploadServicePhoto(photo)).data;
        setUploaded({ file: photo, upload });
      }
      return (await ordersApi.addServicePhoto(orderId, {
        stage: photoStage,
        fileUrl: upload.url,
        fileName: upload.fileName,
        note: photoNote.trim() || undefined,
      })).data;
    },
    onSuccess: () => {
      clearForm();
      toast.success('Foto agregada.');
    },
    onError: (error) => setFormError(getApiErrorMessage(error, 'No se pudo agregar la foto.')),
    onSettled: refresh,
  });

  const remove = useMutation({
    mutationFn: (photoId: string) => ordersApi.removeServicePhoto(orderId, photoId),
    onSuccess: () => toast.success('Foto quitada.'),
    onError: (error) => toast.error(getApiErrorMessage(error, 'No se pudo quitar la foto.')),
    onSettled: refresh,
  });

  const canAddBefore = photosQuery.data?.canAddBefore ?? false;
  // Once delivered or finished only the after goes: the stage follows what the order takes.
  const chosenStage = canAddBefore ? stage : ServicePhotoStage.After;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setFormError('');
    if (!file) {
      setFormError('Elige o toma una foto.');
      return;
    }
    add.mutate({ photo: file, stage: chosenStage, note });
  };

  const askToRemove = async (photo: ServiceOrderPhotoDto) => {
    if (!photo.id) return;
    const accepted = await confirm({
      title: 'Quitar foto',
      message: photo.note
        ? `La foto «${photo.note}» deja de verse en la orden.`
        : 'La foto deja de verse en la orden.',
      confirmLabel: 'Quitar foto',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (accepted) remove.mutate(photo.id);
  };

  const retry = (
    <button type="button" className="btn btn-link btn-sm p-0 ms-2 align-baseline" onClick={() => void photosQuery.refetch()}>
      Reintentar
    </button>
  );

  if (photosQuery.isPending) {
    return <div className="text-muted small mb-3" role="status">Cargando fotos…</div>;
  }
  if (!photosQuery.data) {
    return (
      <div className="alert alert-danger small" role="alert">
        {getApiErrorMessage(photosQuery.error, 'No se pudieron cargar las fotos.')}
        {retry}
      </div>
    );
  }

  const { photos, canAdd } = photosQuery.data;
  const stages = canAddBefore ? STAGES : STAGES.filter((option) => option.stage === ServicePhotoStage.After);

  return (
    <div className="mb-3">
      {photosQuery.isError && (
        // Asking again failed: what was read stays on screen, with a way to try again.
        <div className="alert alert-warning small py-2" role="alert">
          {getApiErrorMessage(photosQuery.error, 'No se pudieron actualizar las fotos.')}
          {retry}
        </div>
      )}

      {STAGES.map(({ stage: section, title }) => {
        const ofStage = photos.filter((photo) => photo.stage === section);
        return (
          <section key={section} className="mb-3" aria-label={`Fotos de ${title.toLowerCase()}`}>
            <Heading className="h6 mb-2">{title}</Heading>
            {ofStage.length === 0 ? (
              <div className="text-muted small">Sin fotos.</div>
            ) : (
              <ul className="list-unstyled row row-cols-1 row-cols-sm-2 g-2 mb-0">
                {ofStage.map((photo, index) => {
                  const name = photo.note || photo.fileName || `Foto de ${title.toLowerCase()} ${index + 1}`;
                  return (
                    <li key={photo.id ?? `${photo.url}-${index}`} className="col d-flex gap-2 align-items-start">
                      <PrivatePhoto
                        path={photo.url}
                        alt={name}
                        linked
                        className="border rounded flex-shrink-0"
                        style={{ width: thumbnailSize, height: thumbnailSize }}
                      />
                      <div className="small text-break" style={{ minWidth: 0 }}>
                        {photo.note && <div>{photo.note}</div>}
                        <div className="text-muted">{describeServicePhoto(photo)}</div>
                        {photo.canRemove && photo.id && (
                          <button
                            type="button"
                            className="btn btn-link btn-sm text-danger p-0"
                            aria-label={`Quitar la foto ${name}`}
                            disabled={remove.isPending}
                            onClick={() => void askToRemove(photo)}
                          >
                            Quitar
                          </button>
                        )}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>
        );
      })}

      {canAdd ? (
        <form onSubmit={submit} className="border rounded p-2" aria-label="Agregar foto del servicio">
          <div className="row g-2">
            <div className="col-sm-5">
              <label htmlFor={`${formId}-stage`} className="form-label small mb-1">Etapa</label>
              <select
                id={`${formId}-stage`}
                className="form-select form-select-sm"
                value={chosenStage}
                onChange={(event) => setStage(Number(event.target.value) as ServicePhotoStage)}
              >
                {stages.map((option) => (
                  <option key={option.stage} value={option.stage}>{option.option}</option>
                ))}
              </select>
            </div>
            <div className="col-sm-7">
              <label htmlFor={`${formId}-note`} className="form-label small mb-1">Nota (opcional)</label>
              <input
                id={`${formId}-note`}
                type="text"
                className="form-control form-control-sm"
                value={note}
                maxLength={SERVICE_PHOTO_NOTE_MAX_LENGTH}
                placeholder="Qué se ve en la foto"
                onChange={(event) => setNote(event.target.value)}
              />
            </div>
            <div className="col-12">
              {/* No `capture`: the phone offers its camera or a photo already taken. */}
              <label htmlFor={`${formId}-file`} className="form-label small mb-1">Foto</label>
              <input
                id={`${formId}-file`}
                ref={fileInput}
                type="file"
                accept="image/*"
                className="form-control form-control-sm"
                onChange={(event) => {
                  setFormError('');
                  setFile(event.target.files?.[0] ?? null);
                }}
              />
            </div>
          </div>
          {!canAddBefore && (
            <div className="form-text">La orden ya se entregó: solo se le agregan fotos del después.</div>
          )}
          {formError && <div className="text-danger small mt-2" role="alert">{formError}</div>}
          <button type="submit" className="btn btn-light btn-sm mt-2" disabled={add.isPending}>
            {add.isPending ? 'Subiendo…' : 'Agregar foto'}
          </button>
        </form>
      ) : (
        <div className="text-muted small">La orden está cancelada: ya no se le agregan fotos.</div>
      )}
    </div>
  );
}
