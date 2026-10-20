import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { messagingApi } from '../../api/messagingApi';
import { MessagePurpose, type OutgoingMessageDto, type PaginatedList } from '../../types';
import { MessageHistory } from './RemindersTab';

/**
 * What the reminders of the fiado did (Fase 26C): sent, failed (for example, outside the 24 hours without a template
 * approved in Meta) or skipped, with why. They are turned on in Clientes › Por cobrar.
 */
export default function CreditRemindersTab() {
  const [history, setHistory] = useState<PaginatedList<OutgoingMessageDto> | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    let current = true;
    messagingApi.getMessages({ purpose: MessagePurpose.CreditReminder, pageNumber: 1, pageSize: 20 })
      .then(({ data }) => {
        if (current) setHistory(data);
      })
      .catch((err: unknown) => {
        if (current) setError(getApiErrorMessage(err, 'No se pudieron cargar los recordatorios del fiado.'));
      });
    return () => {
      current = false;
    };
  }, []);

  if (error) return <div className="alert alert-danger" role="alert">{error}</div>;
  if (!history) return <p className="text-muted">Cargando…</p>;
  return (
    <MessageHistory
      title="Recordatorios del fiado"
      hint={(
        <>
          Lo que debe cada cliente con algo vencido, por WhatsApp. Se encienden y se cambian en{' '}
          <Link to="/customers/receivables">Clientes › Por cobrar</Link>.
        </>
      )}
      empty="Todavía no se ha enviado ningún recordatorio del fiado."
      history={history}
    />
  );
}
