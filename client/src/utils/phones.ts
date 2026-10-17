/**
 * A phone as a person in Mexico writes it: the 10 digits of a Mexican number kept in E.164 (+52, or the old +521),
 * anything else as it comes. The server reads either form the same way.
 */
export function localPhone(phone: string | null | undefined): string {
  const value = (phone ?? '').trim();
  return /^\+521?(\d{10})$/.exec(value)?.[1] ?? value;
}
