// A transfer receipt or a service photo of the business: "orders/{tenant}/{area}/{yyyyMM}/{guid}.{ext}".
const PRIVATE_KEY =
  /(?:^|\/)(orders\/[0-9a-fA-F-]{36}\/(?:payment-evidence|service-intake)\/\d{6}\/[0-9a-fA-F-]{36}\.(?:webp|pdf|jpg|jpeg|png))$/;

/**
 * The API path of a private file of the business, from what a record keeps: "/files/{key}" or the public address the
 * file had before it became private ("/uploads/{key}" or a storage URL). Null for anything else, so no record can
 * point the screen at a foreign address.
 */
export function toPrivateFilePath(reference: string | null | undefined): string | null {
  if (!reference) return null;
  const value = reference.trim();
  let path: string;
  if (value.startsWith('/') && !value.startsWith('//')) {
    path = value;
  } else {
    try {
      const url = new URL(value);
      if (url.protocol !== 'https:') return null;
      path = url.pathname;
    } catch {
      return null;
    }
  }

  // Segments cannot start with a dot, like on the server: no climbing out of the business's folder.
  if (path.split('/').some((segment) => segment.startsWith('.'))) return null;
  const match = PRIVATE_KEY.exec(path);
  return match ? `/files/${match[1]}` : null;
}

export function isPdfPath(path: string): boolean {
  return path.toLowerCase().endsWith('.pdf');
}
