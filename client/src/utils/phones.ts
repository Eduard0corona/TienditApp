/**
 * A phone as a person in Mexico writes it: the 10 digits of a Mexican number kept in E.164 (+52, or the old +521),
 * anything else as it comes. The server reads either form the same way.
 */
export function localPhone(phone: string | null | undefined): string {
  const value = (phone ?? '').trim();
  return /^\+521?(\d{10})$/.exec(value)?.[1] ?? value;
}

/**
 * Whether a phone typed for someone else (the WhatsApp of an invoice, Fase 28M) can be a number, with the server's rule: a
 * Mexican one as its 10 digits (or 52 and the 10), any other with its international prefix (+ or 00). The server decides.
 */
export function isTypedPhone(phone: string): boolean {
  const value = phone.trim();
  if (!/^[+\d(][\d\s().-]*$/.test(value)) return false;
  const digits = value.replace(/\D/g, '');
  if (value.startsWith('+')) return digits.length >= 8 && digits.length <= 15;
  if (value.startsWith('00')) return digits.length >= 10 && digits.length <= 17;
  return digits.length === 10 || (digits.length === 12 && digits.startsWith('52'));
}

/** Whether two phones are the same number as a person in Mexico writes them, spaces and dashes apart. */
export function isSamePhone(a: string | null | undefined, b: string | null | undefined): boolean {
  const plain = (phone: string | null | undefined) => localPhone((phone ?? '').replace(/[\s().-]/g, '').replace(/^00/, '+'));
  return plain(a) !== '' && plain(a) === plain(b);
}
