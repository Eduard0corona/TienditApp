import { useState } from 'react';
import { ordersApi } from '../../api/ordersApi';
import type { CreateServiceOrderDetailRequest, CreateServiceOrderPieceRequest } from '../../types';
import { businessDateTimeToIso, toBusinessDateTimeInput } from '../../utils/dates';
import {
  buildChecklistItem,
  buildPhoto,
  buildServicePiece,
  cleanChecklist,
  cleanServicePieces,
  type CartLine,
  type ServiceChecklistItem,
  type ServicePhotoItem,
} from './ordersCartModel';

export type ServicePhotoKind = 'before' | 'after';

/**
 * The reception of a service order in Nuevo pedido: its dates, priority, notes, checklist, photos and pieces, and what the
 * order sends of them. `setError` is the page's message: an upload clears it and says when one fails.
 */
export function useServiceIntake(openedAt: Date, setError: (message: string) => void) {
  // Both dates are the business's wall clock, like the service board shows them, not the device's. The received date
  // starts when the order was opened, and stays that instant while it shows it.
  const [receivedAt, setReceivedAt] = useState(() => toBusinessDateTimeInput(openedAt));
  const [promisedAt, setPromisedAt] = useState('');
  const [priority, setPriority] = useState(3);
  const [intakeNotes, setIntakeNotes] = useState('');
  const [deliveryNotes, setDeliveryNotes] = useState('');
  const [checklist, setChecklist] = useState<ServiceChecklistItem[]>(() => [buildChecklistItem('')]);
  const [beforePhotos, setBeforePhotos] = useState<ServicePhotoItem[]>([]);
  const [afterPhotos, setAfterPhotos] = useState<ServicePhotoItem[]>([]);
  const [uploading, setUploading] = useState(false);
  const [uploadingMessage, setUploadingMessage] = useState('Subiendo evidencia de servicio...');
  const [pieces, setPieces] = useState<CreateServiceOrderPieceRequest[]>(() => [buildServicePiece()]);

  const updatePiece = (index: number, changes: Partial<CreateServiceOrderPieceRequest>) => {
    setPieces((current) =>
      current.map((piece, currentIndex) =>
        currentIndex === index ? { ...piece, ...changes } : piece,
      ),
    );
  };

  /** A new piece starts with the first service of the cart, the one usually asked for. */
  const addPiece = (firstServiceLine: CartLine | undefined) => {
    setPieces((current) => [
      ...current,
      {
        ...buildServicePiece(),
        requestedService: firstServiceLine?.productName ?? '',
        serviceProductId: firstServiceLine?.productId,
      },
    ]);
  };

  // The form keeps at least one piece and one point of the checklist.
  const removePiece = (index: number) => {
    setPieces((current) =>
      current.length <= 1 ? current : current.filter((_, currentIndex) => currentIndex !== index),
    );
  };

  const updateChecklistItem = (id: string, changes: Partial<ServiceChecklistItem>) => {
    setChecklist((current) =>
      current.map((item) => (item.id === id ? { ...item, ...changes } : item)),
    );
  };

  const addChecklistItem = () => {
    setChecklist((current) => [...current, buildChecklistItem('')]);
  };

  const removeChecklistItem = (id: string) => {
    setChecklist((current) =>
      current.length <= 1 ? current : current.filter((item) => item.id !== id),
    );
  };

  const removePhoto = (kind: ServicePhotoKind, id: string) => {
    if (kind === 'before') {
      setBeforePhotos((current) => current.filter((photo) => photo.id !== id));
      return;
    }

    setAfterPhotos((current) => current.filter((photo) => photo.id !== id));
  };

  const uploadPhotos = async (files: FileList | null, kind: ServicePhotoKind) => {
    if (!files || files.length === 0) return;

    setError('');
    setUploading(true);
    setUploadingMessage(kind === 'before'
      ? 'Subiendo fotos de recepción...'
      : 'Subiendo fotos de entrega...');

    try {
      const uploads: ServicePhotoItem[] = [];
      for (const file of Array.from(files)) {
        const { data } = await ordersApi.uploadServicePhoto(file);
        uploads.push(buildPhoto(data));
      }

      if (kind === 'before') {
        setBeforePhotos((current) => [...current, ...uploads]);
      } else {
        setAfterPhotos((current) => [...current, ...uploads]);
      }
    } catch {
      setError('No se pudieron subir una o más imágenes. Intenta nuevamente.');
    } finally {
      setUploading(false);
    }
  };

  /** What the order sends of its reception: the dates as instants, trimmed texts, and the photos and checklist as JSON. */
  const toServiceDetail = (): CreateServiceOrderDetailRequest => {
    const cleanedChecklist = cleanChecklist(checklist);
    return {
      receivedAt: receivedAt ? businessDateTimeToIso(receivedAt, openedAt) : undefined,
      promisedAt: promisedAt ? businessDateTimeToIso(promisedAt) : undefined,
      priority,
      intakeNotes: intakeNotes.trim() || undefined,
      deliveryNotes: deliveryNotes.trim() || undefined,
      intakeChecklistJson: cleanedChecklist.length > 0 ? JSON.stringify(cleanedChecklist) : undefined,
      beforePhotosJson: beforePhotos.length > 0 ? JSON.stringify(beforePhotos) : undefined,
      afterPhotosJson: afterPhotos.length > 0 ? JSON.stringify(afterPhotos) : undefined,
      pieces: cleanServicePieces(pieces),
    };
  };

  return {
    receivedAt,
    setReceivedAt,
    promisedAt,
    setPromisedAt,
    priority,
    setPriority,
    intakeNotes,
    setIntakeNotes,
    deliveryNotes,
    setDeliveryNotes,
    checklist,
    updateChecklistItem,
    addChecklistItem,
    removeChecklistItem,
    beforePhotos,
    afterPhotos,
    removePhoto,
    uploadPhotos,
    uploading,
    uploadingMessage,
    pieces,
    updatePiece,
    addPiece,
    removePiece,
    toServiceDetail,
  };
}

export type ServiceIntake = ReturnType<typeof useServiceIntake>;
