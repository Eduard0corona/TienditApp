/** An address a customer account keeps, in one line: «Av. Juárez 120 · CP 72000». */
export function describeAccountAddress(address: { addressLine: string; postalCode: string }): string {
  return `${address.addressLine} · CP ${address.postalCode}`;
}
