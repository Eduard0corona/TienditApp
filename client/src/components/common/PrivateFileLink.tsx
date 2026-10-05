import { useState, type ReactNode } from 'react';
import { filesApi } from '../../api/filesApi';
import { getApiErrorMessage } from '../../api/errors';
import { useToast } from '../../hooks/useToast';
import { isPdfPath } from '../../utils/privateFiles';

interface PrivateFileLinkProps {
  /** A private file path ("/files/orders/..."). */
  path: string;
  children: ReactNode;
  className?: string;
}

// Long enough for the new tab or the download to read it.
const OBJECT_URL_LIFETIME_MS = 60_000;

/** Opens a private file of the business (a receipt): fetched with the session only when asked for. */
export default function PrivateFileLink({ path, children, className = 'btn btn-link btn-sm p-0' }: PrivateFileLinkProps) {
  const toast = useToast();
  const [opening, setOpening] = useState(false);

  const open = async () => {
    const pdf = isPdfPath(path);
    // Opened while the click still counts, so the browser does not take it for a pop-up.
    const tab = pdf ? null : window.open('', '_blank');
    setOpening(true);
    try {
      const { data } = await filesApi.get(path);
      const url = URL.createObjectURL(data);
      if (tab) {
        tab.location.href = url;
      } else {
        // A PDF is only downloaded, never shown by the app.
        const link = document.createElement('a');
        link.href = url;
        link.download = path.split('/').pop() ?? 'comprobante';
        link.click();
      }
      window.setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_LIFETIME_MS);
    } catch (err: unknown) {
      tab?.close();
      toast.error(getApiErrorMessage(err, 'No se pudo abrir el archivo.'));
    } finally {
      setOpening(false);
    }
  };

  return (
    <button type="button" className={className} onClick={open} disabled={opening}>
      {children}
    </button>
  );
}
