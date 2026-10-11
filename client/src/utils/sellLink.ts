/**
 * Link to Vender with the customer filled in. Only with the chat's conversation is the order recorded as a WhatsApp
 * order; without it (the customer card) it is a counter sale.
 */
export function buildSellLink(options: { phone?: string | null; name?: string | null; conversationId?: string | null }): string {
  const params = new URLSearchParams();
  if (options.phone) params.set('phone', options.phone);
  if (options.name) params.set('name', options.name);
  if (options.conversationId) params.set('conversation', options.conversationId);
  const query = params.toString();
  return query ? `/sell?${query}` : '/sell';
}
