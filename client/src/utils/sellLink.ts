/** Link to Vender with the customer filled in; from a chat, the order is recorded as a WhatsApp order. */
export function buildSellLink(options: { phone?: string; name?: string; conversationId?: string }): string {
  const params = new URLSearchParams();
  if (options.phone) params.set('phone', options.phone);
  if (options.name) params.set('name', options.name);
  if (options.conversationId) params.set('conversation', options.conversationId);
  const query = params.toString();
  return query ? `/sell?${query}` : '/sell';
}
