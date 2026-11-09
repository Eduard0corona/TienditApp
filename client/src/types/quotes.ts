// Quotes.

/**
 * Same numbers as Domain.Quotes (Fase 29C, ADR 0053). The server sends a quote's status as it stands today: a sent one whose
 * validity passed reads Expired, which is never stored.
 */
export { QuoteEmailFailure, QuoteListFilter, QuoteReplyChannel, QuoteStatus } from '../api/generated/enums';
