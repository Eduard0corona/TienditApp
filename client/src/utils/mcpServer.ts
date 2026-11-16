/**
 * The address of TienditApp's MCP server (Fase 32B, ADR 0096): this same site at /mcp, as /asistentes gives it (Fase 32D).
 * Apart from the store's utilities, so it travels with that page and not with the first visit.
 */
export function mcpServerUrl(origin: string): string {
  return `${origin.replace(/\/+$/, '')}/mcp`;
}

/** The same server with the customer's account (Fase 32E, ADR 0097): the assistant asks to sign in and for permission. */
export function mcpAccountServerUrl(origin: string): string {
  return `${mcpServerUrl(origin)}/cuenta`;
}
