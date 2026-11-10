import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { messagingApi } from '../../api/messagingApi';
import { MessagePurpose, type OutgoingMessageDto, type PaginatedList } from '../../types';
import { MessageHistory } from './RemindersTab';

/**
 * What the WhatsApp messages of the fiado did: the notices before something comes due (Fase 26E) and, below them, the
 * reminders of what is overdue (Fase 26C), each sent, failed (for example, outside the 24 hours without a template
 * approved in Meta) or skipped, with why. They are turned on in Clientes › Por cobrar, in the same order.
 */
export default function CreditRemindersTab() {
  const [history, setHistory] = useState<PaginatedList<OutgoingMessageDto> | null>(null);
  const [notices, setNotices] = useState<PaginatedList<OutgoingMessageDto> | null>(null);
  const [error, setError] = useState('');
  /** Each «Reintentar» reads them again. */
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let current = true;
    const page = { pageNumber: 1, pageSize: 20 };
    Promise.all([
      messagingApi.getMessages({ purpose: MessagePurpose.CreditReminder, ...page }),
      messagingApi.getMessages({ purpose: MessagePurpose.CreditDueSoon, ...page }),
    ])
      .then(([reminders, dueSoon]) => {
        if (!current) return;
        setHistory(reminders.data);
        setNotices(dueSoon.data);
      })
      .catch((err: unknown) => {
        if (current) setError(getApiErrorMessage(err, 'No se pudieron cargar los recordatorios del fiado.'));
      });
    return () => {
      current = false;
    };
  }, [attempt]);

  if (error) {
    return (
      <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
        <span className="me-auto">{error}</span>
        {/* Reading again takes the notice away while it loads. */}
        <button type="button" className="btn btn-sm btn-light" onClick={() => { setError(''); setAttempt((value) => value + 1); }}>
          Reintentar
        </button>
      </div>
    );
  }
  if (!history || !notices) return <p className="text-muted" role="status">Cargando…</p>;
  const turnedOnIn = (
    <>
      Se encienden y se cambian en <Link to="/customers/receivables">Clientes › Por cobrar</Link>.
    </>
  );
  return (
    <>
      <MessageHistory
        title="Avisos antes de vencer"
        hint={<>Lo que vence pronto a cada cliente que va al corriente, por WhatsApp. {turnedOnIn}</>}
        empty="Todavía no se ha enviado ningún aviso antes de vencer."
        history={notices}
      />
      <MessageHistory
        title="Recordatorios del fiado"
        hint={<>Lo que debe cada cliente con algo vencido, por WhatsApp. {turnedOnIn}</>}
        empty="Todavía no se ha enviado ningún recordatorio del fiado."
        history={history}
      />
    </>
  );
}
