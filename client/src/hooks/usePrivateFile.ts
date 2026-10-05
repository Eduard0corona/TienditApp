import { useEffect, useState } from 'react';
import { filesApi } from '../api/filesApi';

interface PrivateFileState {
  path: string | null;
  /** An object URL of the file, valid while the component that asked for it is shown. */
  url: string | null;
  failed: boolean;
}

/** A private file of the business fetched with the session: null while it loads or when it cannot be opened. */
export function usePrivateFile(path: string | null): PrivateFileState {
  const [state, setState] = useState<PrivateFileState>({ path: null, url: null, failed: false });

  useEffect(() => {
    if (!path) return;
    let active = true;
    let objectUrl: string | null = null;
    filesApi.get(path)
      .then(({ data }) => {
        if (!active) return;
        objectUrl = URL.createObjectURL(data);
        setState({ path, url: objectUrl, failed: false });
      })
      .catch(() => {
        if (active) setState({ path, url: null, failed: true });
      });
    return () => {
      active = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [path]);

  // What was loaded for another path is not this file.
  return state.path === path ? state : { path, url: null, failed: false };
}
