// Fase 29N endpoints for the fake backend (ADR 0068): whether the person gets an email with each request online of the
// business (GET and PUT /appointments/request-emails, from `data.requestEmails`). Mariana Ortiz owns Café La Esquina and
// has not chosen, so she gets them, at the email of her account (confirmed).

/** The owner's choice, as the API answers it before she makes one. */
export function demoRequestEmails(data) {
  return { enabled: true, email: data.profile.email, emailConfirmed: true };
}

export function phase29nRoutes(data, { ok }) {
  data.requestEmails ??= demoRequestEmails(data);

  return [
    ['GET', /^\/appointments\/request-emails$/, () => ok(data.requestEmails)],
    ['PUT', /^\/appointments\/request-emails$/, (m, q, body) => {
      data.requestEmails = { ...data.requestEmails, enabled: !!body?.enabled };
      return ok(data.requestEmails);
    }],
  ];
}
