import { useToast } from '../../hooks/useToast';
import { invitationLink, whatsAppInvitationLink } from '../../utils/teamRoles';

interface InvitationShareProps {
  /** App-relative link returned by the API. */
  invitationPath: string;
  firstName: string;
  email: string;
  phone?: string | null;
  businessName: string;
}

/** The invitation link of a person, to copy or send by WhatsApp: there they choose their own password. */
export default function InvitationShare({ invitationPath, firstName, email, phone, businessName }: InvitationShareProps) {
  const toast = useToast();
  const link = invitationLink(invitationPath);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(link);
      toast.success('Enlace copiado.');
    } catch {
      toast.error('No se pudo copiar: selecciona el enlace y cópialo.');
    }
  };

  return (
    <div>
      <p className="mb-2">
        Comparte este enlace con <strong>{firstName}</strong>: ahí elige su contraseña y entra. Vale 7 días.
      </p>
      <div className="input-group mb-2">
        <input
          className="form-control"
          readOnly
          value={link}
          aria-label="Enlace de invitación"
          onFocus={(event) => event.target.select()}
        />
        <button type="button" className="btn btn-light" onClick={() => void copy()}>
          <i className="ri-file-copy-line me-1" aria-hidden="true"></i>Copiar
        </button>
      </div>
      <a
        className="btn btn-success"
        href={whatsAppInvitationLink(link, businessName, firstName, phone)}
        target="_blank"
        rel="noopener noreferrer"
      >
        <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Enviar por WhatsApp
      </a>
      <p className="small text-muted mt-2 mb-0">
        Si el correo está configurado, también le llega a {email}. Quien abra el enlace entra con ese correo.
      </p>
    </div>
  );
}
