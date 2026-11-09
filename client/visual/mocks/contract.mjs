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
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * The routes whose last parameter takes the rest of the path ({**key} in ASP.NET), which the document writes as one more
 * {key} and does not mark: FilesController serves /files/a/b/c.png. Every other parameter is one segment.
 */
const catchAll = new Set(['/api/v1/files/{key}']);

/** Each operation of the contract, with the regular expression of its path ({id} matches one segment). */
const operations = Object.entries(document.paths).flatMap(([template, methods]) => Object.entries(methods).map(([method, operation]) => ({
  method: method.toUpperCase(),
  template,
  // A literal segment beats a parameter: /orders/attention is not /orders/{id}.
  literals: template.split('/').filter((segment) => segment && !segment.startsWith('{')).length,
  pattern: new RegExp(`^${template.split('/').map((segment) => (segment.startsWith('{') ? '[^/]+' : escape(segment))).join('/')}$`, 'i'),
  // Tried only when no route matches segment by segment.
  rest: catchAll.has(template)
    ? new RegExp(`^${template.split('/').map((segment, index, all) => (segment.startsWith('{') ? (index === all.length - 1 ? '.+' : '[^/]+') : escape(segment))).join('/')}$`, 'i')
    : null,
  // The segments that must be a uuid ({id:guid} in ASP.NET): with anything else the API answers 404.
  uuids: template.split('/').flatMap((segment, index) => {
    const name = segment.startsWith('{') ? segment.slice(1, -1) : null;
    const parameter = name && (operation.parameters ?? []).find((p) => p.in === 'path' && p.name === name);
    return parameter && resolve(parameter.schema)?.format === 'uuid' ? [{ index, name }] : [];
  }),
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
  const segments = pathname.split('/');
  const problems = found.uuids
    .filter(({ index }) => !uuid.test(decodeURIComponent(segments[index])))
    .map(({ name }) => `el parámetro {${name}} de la ruta debía ser un uuid y la API contesta 404`);
  // 204 has no body (the routes that write `json: null` for it send nothing a screen reads). ASP.NET answers 204 for any
  // action whose result is null, so a route need not declare it.
  if (status === 204 && json != null) problems.push('204 lleva cuerpo y la API no manda ninguno');
  else if (status >= 200 && status < 300 && json !== undefined && status !== 204) {
    const response = found.operation.responses?.[String(status)] ?? found.operation.responses?.['200'];
    // Without a schema there is nothing to compare: Swashbuckle declares no content for an untyped IActionResult, such as
    // Ok(new { message }).
    const schema = response?.content?.['application/json']?.schema;
    if (schema) check(schema, json, '$', problems);
  }
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
  const flags = schema['x-enum-flags'] === true;
  const all = flags ? (schema.enum ?? []).reduce((bits, member) => bits | member, 0) : 0;
  if (schema.enum && (flags ? !Number.isInteger(value) || (value & ~all) !== 0 : !schema.enum.includes(value))) {
    const what = flags ? 'una combinación de valores' : 'un valor';
    problems.push(`${at} vale ${JSON.stringify(value)}, que no es ${what} de ${declared.$ref?.split('/').pop() ?? 'su enum'}`);
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
        if (Object.hasOwn(properties, name)) check(properties[name], item, `${at}.${name}`, problems, !(schema.required ?? []).includes(name));
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
      else if (schema.format === 'uuid' && !uuid.test(value)) problems.push(`${at} debía ser un uuid y es ${JSON.stringify(value)}`);
      else if (formats[schema.format] && (!formats[schema.format].pattern.test(value) || Number.isNaN(Date.parse(formats[schema.format].parse(value))))) {
        problems.push(`${at} debía ser ${formats[schema.format].name} y es ${JSON.stringify(value)}`);
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

/**
 * How System.Text.Json writes a DateTimeOffset or DateTime (date-time), a DateOnly (date) and a TimeSpan (date-span).
 * Date.parse alone takes "1" or "Oct 4", and a whole date and time where the API sends only the day.
 */
const formats = {
  'date-time': { name: 'una fecha con hora', pattern: /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})?$/, parse: (value) => value },
  date: { name: 'una fecha sin hora (aaaa-mm-dd)', pattern: /^\d{4}-\d{2}-\d{2}$/, parse: (value) => value },
  'date-span': { name: 'una duración (hh:mm:ss)', pattern: /^-?(\d+\.)?\d{2}:[0-5]\d:[0-5]\d(\.\d{1,7})?$/, parse: () => '2000-01-01' },
};

function describe(value) {
  if (Array.isArray(value)) return 'una lista';
  if (typeof value === 'object') return 'un objeto';
  return `${typeof value} ${JSON.stringify(value)}`;
}
