import { useEffect, useId, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { publicMenuApi } from '../../api/publicMenuApi';
import CustomerAppointmentCard from '../../components/booking/CustomerAppointmentCard';
import { ProblemPhotosSender } from '../../components/booking/ProblemPhotos';
import PageTitle from '../../components/common/PageTitle';
import { useConfirm } from '../../hooks/useConfirm';
import { useProblemPhotoUploads } from '../../hooks/useProblemPhotoUploads';
import { useToast } from '../../hooks/useToast';
import { cancelAppointmentMessage } from '../../utils/onlineBooking';

/**
 * An appointment booked online followed with the link of its receipt (/cita#link, Fase 24D), without an account: its
 * status, when and where, and cancelling it before it starts. Like an order's link, it lives in the fragment, which the
 * browser never sends to a server; the page sends it in the body of its request. A visit requested online also takes the
 * photos of what happens from here while it waits or is agendada (Fase 29H).
 */
export default function AppointmentTrackingPage() {
  const token = useLocation().hash.replace(/^#/, '');
  const queryClient = useQueryClient();
  const { confirm } = useConfirm();
  const toast = useToast();
  const [cancelling, setCancelling] = useState(false);
  const photosId = useId();
  // The photos going with this link (also from the receipt): the visit as the server answers each one while the page shows
  // is the one shown. An answer from before is old: the visit may have changed since, and the page reads it itself.
  const uploads = useProblemPhotoUploads(token);
  const { store } = uploads;
  useEffect(
    () => store.onAnswer((latest) => queryClient.setQueryData(['public', 'appointment', token], latest)),
    [store, queryClient, token],
  );
  const queryKey = ['public', 'appointment', token];
  const appointmentQuery = useQuery({
    queryKey,
    queryFn: ({ signal }) => publicMenuApi.trackAppointment(token, signal),
    enabled: token.length > 0,
    retry: false,
  });
  const appointment = appointmentQuery.data;

  const cancel = async () => {
    if (!appointment) return;
    const accepted = await confirm({
      title: 'Cancelar cita',
      message: cancelAppointmentMessage(appointment),
      confirmLabel: 'Cancelar cita',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    setCancelling(true);
    try {
      queryClient.setQueryData(queryKey, await publicMenuApi.cancelAppointmentByLink(token));
      toast.success('Cancelaste tu cita.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar tu cita.'));
      void appointmentQuery.refetch();
    } finally {
      setCancelling(false);
    }
  };

  return (
    <div className="cl-page">
      <PageTitle title="Tu cita" />

      {!token && (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-calendar-event-line fs-1 text-muted d-block mb-2" aria-hidden="true"></i>
            <h2 className="h5">Abre el enlace de tu cita</h2>
            <p className="text-muted mb-0">Al agendar en línea recibes un enlace para ver tu cita. <Link to="/explorar">Explorar negocios</Link></p>
          </div>
        </div>
      )}
      {appointmentQuery.isLoading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {appointmentQuery.error && (
        <div className="alert alert-warning">
          {getApiErrorMessage(appointmentQuery.error, 'No se pudo cargar tu cita.')}
          {appointment && ' Lo de abajo es lo último que vimos.'}
        </div>
      )}

      {appointment && (
        <div className="row">
          <div className="col-12 col-lg-7">
            <CustomerAppointmentCard appointment={appointment} onCancel={() => void cancel()} cancelling={cancelling} titleAs="h2" />
          </div>
          {/* Its own column: the card fills the height of its column. */}
          {/* While it takes them, or while there are photos of this link to say how they went. */}
          {(appointment.canAddPhotos || uploads.photos.length > 0) && (
            <div className="col-12 col-lg-7">
              <section className="card mt-3 mb-0" aria-labelledby={photosId}>
                <div className="card-body">
                  <h2 id={photosId} className="h6">Fotos de lo que pasa</h2>
                  <p className="small text-muted mb-2">Ayudan a {appointment.businessName} a preparar la visita. Solo las ve el negocio.</p>
                  <ProblemPhotosSender token={token} sent={appointment.problemPhotos} canAdd={appointment.canAddPhotos} />
                </div>
              </section>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
