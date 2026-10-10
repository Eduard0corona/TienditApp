/** The first letter of a name, as one character even when accented or an emoji: "ñoquis" gives "Ñ". */
export function initialOf(name: string): string {
  return (Array.from(name.trim())[0] ?? '').toUpperCase();
}
