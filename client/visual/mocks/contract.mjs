// The contract of the fake backend (Fase 30D): each answer a route gives is compared with what openapi/openapi.json says the
// API answers for that method and path, so the data of the scenes cannot drift from the API without a scene failing. Before,
// the routes were only checked to exist: a field the API stopped sending, one it never sent, or a list where it sends an
// object went unnoticed, and the screens were shot with data the real API never returns.
//
// It reads the subset of OpenAPI 3.0 that Swashbuckle writes for this API: $ref, type, format, nullable, enum, required,
// properties, additionalProperties (false on every DTO: a field the API does not send is reported too) and items.
import { readFileSync } from 'node:fs';

const document = JSON.parse(readFileSync(new URL('../../openapi/openapi.json', import.meta.url), 'utf8'));
const schemas = document.components?.schemas ?? {};

/** Each operation of the contract, with the regular expression of its path ({id} matches one segment). */
const operations = Object.entries(document.paths).flatMap(([template, methods]) => Object.entries(methods).map(([method, operation]) => ({
  method: method.toUpperCase(),
  template,
  // A literal segment beats a parameter: /orders/attention is not /orders/{id}.
  literals: template.split('/').filter((segment) => segment && !segment.startsWith('{')).length,
  pattern: new RegExp(`^${template.split('/').map((segment) => (segment.startsWith('{') ? '[^/]+' : escape(segment))).join('/')}$`, 'i'),
  // A route whose last parameter takes the rest of the path ({**key} in ASP.NET, written {key} in the document), such as
  // /files/{key}: tried only when no route matches segment by segment.
  rest: template.endsWith('}')
    ? new RegExp(`^${template.split('/').map((segment, index, all) => (segment.startsWith('{') ? (index === all.length - 1 ? '.+' : '[^/]+') : escape(segment))).join('/')}$`, 'i')
    : null,
  operation,
})));

function escape(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** The operation of the contract for a request, or undefined when the API has none. */
export function findOperation(method, pathname) {
  const byMethod = operations.filter((candidate) => candidate.method === method);
  const exact = byMethod.filter((candidate) => candidate.pattern.test(pathname));
  const found = exact.length ? exact : byMethod.filter((candidate) => candidate.rest?.test(pathname));
  return found.sort((a, b) => b.literals - a.literals)[0];
}

/**
 * What is wrong with an answer of the fake backend, by the contract: an empty list when it matches. Only answers with a
 * body and a 2xx status are compared (a rejection is a ProblemDetails the screens read by its code).
 */
export function checkAnswer(method, pathname, status, json) {
  const found = findOperation(method, pathname);
  if (!found) return [`${method} ${pathname}: la API no tiene esta ruta`];
  // 204 has no body (the routes that write `json: null` for it send nothing a screen reads).
  if (status === 204) return json == null ? [] : [`${method} ${found.template}: 204 lleva cuerpo y la API no manda ninguno`];
  if (status < 200 || status >= 300 || json === undefined) return [];
  const response = found.operation.responses?.[String(status)] ?? found.operation.responses?.['200'];
  const schema = response?.content?.['application/json']?.schema;
  if (!schema) return [`${method} ${found.template}: la API no devuelve cuerpo con ${status}`];
  const problems = [];
  check(schema, json, '$', problems);
  return problems.map((problem) => `${method} ${found.template}: ${problem}`);
}

/**
 * A problem as the list of the known ones writes it (contract-known.txt): without the index of each item of a list nor the
 * value found, so one line stands for every item and every value.
 */
export function normalize(problem) {
  return problem.replace(/\[\d+\]/g, '[]').replace(/ y es .*$/, '').replace(/ vale .*?, que/, ' vale X, que');
}

/** The problems that were already there when the check began (Fase 30D): the list only shrinks. */
export const known = new Set(
  readFileSync(new URL('./contract-known.txt', import.meta.url), 'utf8')
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line && !line.startsWith('#')),
);

function resolve(schema) {
  let current = schema;
  while (current?.$ref) current = schemas[current.$ref.replace('#/components/schemas/', '')];
  return current;
}

function check(declared, value, at, problems, optional = false) {
  const schema = resolve(declared);
  if (!schema) return;
  if (value === null) {
    // The API writes as required every property that is never null (RequireNonNullablePropertiesSchemaFilter); one that is
    // not may be null even when its schema is a $ref, which OpenAPI 3.0 cannot mark nullable.
    if (!schema.nullable && !declared.nullable && !optional) problems.push(`${at} es null y la API nunca lo manda null`);
    return;
  }
  if (value === undefined) return;

  // A [Flags] enum (x-enum-flags) takes any combination of its values.
  if (schema.enum && !schema['x-enum-flags'] && !schema.enum.includes(value)) {
    problems.push(`${at} vale ${JSON.stringify(value)}, que no es un valor de ${declared.$ref?.split('/').pop() ?? 'su enum'}`);
    return;
  }

  switch (schema.type) {
    case 'object': {
      if (typeof value !== 'object' || Array.isArray(value)) {
        problems.push(`${at} debía ser un objeto y es ${describe(value)}`);
        return;
      }
      const properties = schema.properties ?? {};
      for (const name of schema.required ?? []) {
        if (value[name] === undefined) problems.push(`${at}.${name} falta`);
      }
      for (const [name, item] of Object.entries(value)) {
        if (item === undefined) continue;
        if (properties[name]) check(properties[name], item, `${at}.${name}`, problems, !(schema.required ?? []).includes(name));
        else if (schema.additionalProperties && typeof schema.additionalProperties === 'object') {
          check(schema.additionalProperties, item, `${at}.${name}`, problems);
        } else if (schema.additionalProperties === false || schema.properties) {
          problems.push(`${at}.${name} sobra: la API no lo manda`);
        }
      }
      return;
    }
    case 'array':
      if (!Array.isArray(value)) {
        problems.push(`${at} debía ser una lista y es ${describe(value)}`);
        return;
      }
      value.forEach((item, index) => check(schema.items, item, `${at}[${index}]`, problems));
      return;
    case 'string':
      if (typeof value !== 'string') problems.push(`${at} debía ser texto y es ${describe(value)}`);
      else if (schema.format === 'uuid' && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) {
        problems.push(`${at} debía ser un uuid y es ${JSON.stringify(value)}`);
      } else if ((schema.format === 'date-time' || schema.format === 'date') && Number.isNaN(Date.parse(value))) {
        problems.push(`${at} debía ser una fecha y es ${JSON.stringify(value)}`);
      }
      return;
    case 'integer':
      if (!Number.isInteger(value)) problems.push(`${at} debía ser un entero y es ${describe(value)}`);
      return;
    case 'number':
      if (typeof value !== 'number') problems.push(`${at} debía ser un número y es ${describe(value)}`);
      return;
    case 'boolean':
      if (typeof value !== 'boolean') problems.push(`${at} debía ser true o false y es ${describe(value)}`);
      return;
    default:
  }
}

function describe(value) {
  if (Array.isArray(value)) return 'una lista';
  if (typeof value === 'object') return 'un objeto';
  return `${typeof value} ${JSON.stringify(value)}`;
}
