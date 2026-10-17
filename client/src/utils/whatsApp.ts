/**
 * A wa.me link to a business's phone with a message ready, or null when the business has no phone of ten digits or more.
 * Mexican ten-digit numbers get the country code.
 */
export function businessWhatsAppUrl(phone: string | null | undefined, text: string): string | null {
  const digits = (phone ?? '').replace(/\D/g, '');
  if (digits.length < 10) return null;
  const target = digits.length === 10 ? `52${digits}` : digits;
  return `https://wa.me/${target}?text=${encodeURIComponent(text)}`;
}
