// Phase 32E endpoints for the fake backend: the AI assistants connected to a customer account (GET /me/assistants,
// DELETE /me/assistants/{id}) and the permission page of one (GET and POST /auth/assistant-authorizations). They are
// `data.accountAssistants` and `data.assistantAuthorization`; a scene file changes them in its `prepareData`.

/** Claude, connected by María Fernanda last week and used yesterday. */
export function demoAssistants() {
  return [
    { id: '7d3c5a10-2b4e-4f8a-9c61-0e5b8f2a4d17', name: 'Claude', site: 'claude.ai', grantedAt: '2026-09-28T17:10:00.000Z', lastUsedAt: '2026-10-03T21:15:00.000Z' },
  ];
}

/** Claude asks for the account, and returns to claude.ai. */
export function demoAssistantAuthorization() {
  return {
    valid: true, problem: null, errorRedirectUrl: null, clientName: 'Claude', redirectSite: 'claude.ai', redirectsToThisDevice: false,
  };
}

export function phase32eRoutes(data, { ok }) {
  data.accountAssistants ??= demoAssistants();
  data.assistantAuthorization ??= demoAssistantAuthorization();

  return [
    ['GET', /^\/me\/assistants$/, () => ok(data.accountAssistants)],
    ['DELETE', /^\/me\/assistants\/([^/]+)$/, (m) => {
      data.accountAssistants = data.accountAssistants.filter((assistant) => assistant.id !== m[1]);
      return { status: 204, json: null };
    }],
    ['GET', /^\/auth\/assistant-authorizations$/, () => ok(data.assistantAuthorization)],
    ['POST', /^\/auth\/assistant-authorizations$/, (m, q, body) => ok({
      redirectUrl: body?.allow
        ? `${body.redirectUri}?code=demo-code&state=${encodeURIComponent(body.state ?? '')}`
        : `${body?.redirectUri}?error=access_denied&state=${encodeURIComponent(body?.state ?? '')}`,
    })],
  ];
}
