/** A postal code of Mexico: five digits, as the server keeps them. */
export function isPostalCode(value: string): boolean {
  return /^\d{5}$/.test(value);
}

export interface ParsedPostalCodes {
  /** Each valid one once, in numeric order. */
  codes: string[];
  /** What is not a postal code, as it was written, once each. */
  invalid: string[];
}

/**
 * The postal codes of a zone as a person writes or pastes them: separated by commas, spaces, semicolons or lines. The
 * server keeps them the same way (once each, without spaces), so what is shown here is what will be saved.
 */
export function parsePostalCodes(text: string): ParsedPostalCodes {
  const codes = new Set<string>();
  const invalid: string[] = [];
  for (const token of text.split(/[\s,;]+/)) {
    if (!token) continue;
    if (isPostalCode(token)) codes.add(token);
    else if (!invalid.includes(token)) invalid.push(token);
  }
  return { codes: [...codes].sort(), invalid };
}
