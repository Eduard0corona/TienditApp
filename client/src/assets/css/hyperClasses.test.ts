import ts from 'typescript';
import { describe, expect, it } from 'vitest';
import hyperClassNamesJson from './hyperClassNames.json?raw';

// Fase 20D: every class the app takes from Hyper alone (not from Bootstrap, not from Remix) has a rule of our own.
// Since 20E Hyper's stylesheet is gone: hyperClassNames.json keeps the names of the classes only it defined, so a class
// copied from Hyper (its docs, an old screen) cannot come back without a rule.
const hyperClassNames = new Set<string>(JSON.parse(hyperClassNamesJson));
const ourCss = import.meta.glob<string>('/src/**/*.css', { query: '?raw', import: 'default', eager: true });
const code = import.meta.glob<string>(['/src/**/*.{ts,tsx}', '!/src/**/*.test.{ts,tsx}', '!/src/**/generated/**'], {
  query: '?raw', import: 'default', eager: true,
});

/** The class names a stylesheet has rules for (comments, strings and url() left out). */
function classesOf(source: string): Set<string> {
  const css = source
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/url\([^)]*\)/g, '')
    .replace(/"[^"]*"|'[^']*'/g, '');
  return new Set([...css.matchAll(/\.(-?[_a-zA-Z][\w-]*)/g)].map((match) => match[1]));
}

/** Every word of every string and template in the app's code: a class can only come from one of them. */
function wordsInCode(): Map<string, string> {
  const words = new Map<string, string>();
  for (const [file, source] of Object.entries(code)) {
    const kind = file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
    const visit = (node: ts.Node) => {
      if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) || ts.isTemplateHead(node)
        || ts.isTemplateMiddle(node) || ts.isTemplateTail(node)) {
        for (const word of node.text.split(/\s+/)) {
          if (/^-?[_a-z][\w-]*$/i.test(word) && !words.has(word)) words.set(word, file);
        }
      }
      ts.forEachChild(node, visit);
    };
    visit(ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, kind));
  }
  return words;
}

/**
 * Words of the code that happen to name a Hyper class (Prism tokens, states of its plugins), never used as a class:
 * a type name, a key, an attribute value. A class added to this list needs a rule instead.
 */
const NOT_CLASSES = new Set([
  'available', 'bottom', 'code', 'content', 'dark', 'day', 'function', 'logo', 'new', 'number', 'off', 'pointer',
  'string', 'token', 'url',
]);

describe('classes the app takes from Hyper', () => {
  it('reads the code, the stylesheets and the names it checks', () => {
    expect(Object.keys(code).length).toBeGreaterThan(100);
    expect(Object.keys(ourCss)).toContain('/src/assets/css/components.css');
    expect(classesOf(ourCss['/src/assets/css/components.css'])).toContain('btn-soft-primary');
    expect(hyperClassNames.size).toBe(930);
    expect(hyperClassNames).toContain('btn-soft-primary');
  });

  it('have a rule of our own for each one the code uses', () => {
    const ours = new Set(Object.values(ourCss).flatMap((source) => [...classesOf(source)]));

    const missing = [...wordsInCode()]
      .filter(([word]) => hyperClassNames.has(word) && !ours.has(word) && !NOT_CLASSES.has(word))
      .map(([word, file]) => `${word} (${file})`);

    expect(missing).toEqual([]);
  });

  it('keeps no word in its list of non-classes that the code no longer uses', () => {
    const words = wordsInCode();
    expect([...NOT_CLASSES].filter((word) => !words.has(word))).toEqual([]);
  });
});
