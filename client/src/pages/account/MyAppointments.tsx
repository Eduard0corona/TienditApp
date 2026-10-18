import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi } from '../../api/accountApi';
import { getApiErrorMessage } from '../../api/errors';
import type { CustomerAppointment } from '../../api/publicMenuApi';
import CustomerAppointmentCard from '../../components/booking/CustomerAppointmentCard';
import PageTitle from '../../components/common/PageTitle';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { MyAppointmentsScope } from '../../types';
import { cancelAppointmentMessage } from '../../utils/onlineBooking';

const PAGE_SIZE = 10;
/** The most the API gives in one page: the upcoming ones are few, and all of them show. */
const UPCOMING_PAGE_SIZE = 50;

/**
 * The appointments a customer account booked online, in every business (Fase 24D): the upcoming ones first, soonest
 * first, and the ones before by pages. One that has not started is cancelled from here.
 */
export default function MyAppointments() {
  const [page, setPage] = useState(1);
  const [cancellingId, setCancellingId] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const { confirm } = useConfirm();
  const toast = useToast();
  const upcomingQuery = useQuery({
    queryKey: ['me', 'appointments', 'upcoming'],
    queryFn: ({ signal }) => accountApi.getAppointments(MyAppointmentsScope.Upcoming, 1, UPCOMING_PAGE_SIZE, signal),
  });
  const pastQuery = useQuery({
    queryKey: ['me', 'appointments', 'past', page],
    queryFn: ({ signal }) => accountApi.getAppointments(MyAppointmentsScope.Past, page, PAGE_SIZE, signal),
  });
  const upcoming = upcomingQuery.data?.items ?? [];
  const past = pastQuery.data?.items ?? [];

  const cancel = async (appointment: CustomerAppointment) => {
    const accepted = await confirm({
      title: 'Cancelar cita',
      message: cancelAppointmentMessage(appointment),
      confirmLabel: 'Cancelar cita',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    setCancellingId(appointment.id);
    try {
      await accountApi.cancelAppointment(appointment.id);
      toast.success('Cancelaste tu cita.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar tu cita.'));
    } finally {
      setCancellingId(null);
      // A cancelled appointment goes to «Anteriores».
      void queryClient.invalidateQueries({ queryKey: ['me', 'appointments'] });
    }
  };

  const loading = upcomingQuery.isLoading || pastQuery.isLoading;
  const error = upcomingQuery.error ?? pastQuery.error;
  const card = (appointment: CustomerAppointment) => (
    <div className="col-12 col-lg-6" key={appointment.id}>
      <CustomerAppointmentCard
        appointment={appointment}
        onCancel={() => void cancel(appointment)}
        cancelling={cancellingId === appointment.id}
      />
    </div>
  );

  // Inside the customer's frame (CustomerLayout), which has no margins of its own.
  return (
    <div className="cl-page">
      <PageTitle title="Mis citas" />

      {loading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {error && (
        <div className="alert alert-danger">{getApiErrorMessage(error, 'No se pudieron cargar tus citas.')}</div>
      )}
      {!loading && !error && upcoming.length === 0 && past.length === 0 && (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-calendar-event-line fs-1 text-muted d-block mb-2" aria-hidden="true"></i>
            <h2 className="h5">Todavía no tienes citas</h2>
            <p className="text-muted mb-0">
              Aquí aparecen las citas que agendes en línea con tu cuenta. <Link to="/explorar">Explorar negocios</Link>
            </p>
          </div>
        </div>
      )}

      {upcoming.length > 0 && (
        <section aria-labelledby="mis-citas-proximas" className="mb-4">
          <h2 id="mis-citas-proximas" className="h5 mb-3">Próximas</h2>
          <div className="row g-3">{upcoming.map(card)}</div>
        </section>
      )}

      {past.length > 0 && (
        <section aria-labelledby="mis-citas-anteriores">
          <h2 id="mis-citas-anteriores" className="h5 mb-3">Anteriores</h2>
          <div className="row g-3">{past.map(card)}</div>

          {(pastQuery.data?.totalPages ?? 0) > 1 && (
            <div className="d-flex justify-content-center align-items-center gap-3 mt-3">
              <button type="button" className="btn btn-light btn-sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Más recientes
              </button>
              <span className="small text-muted">Página {page} de {pastQuery.data?.totalPages}</span>
              <button
                type="button"
                className="btn btn-light btn-sm"
                disabled={!pastQuery.data?.hasNextPage}
                onClick={() => setPage(page + 1)}
              >
                Más antiguas
              </button>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
