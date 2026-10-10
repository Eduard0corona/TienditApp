// The visual check of a change (README.md):
//   npm run visual:baseline [-- <ref>]   builds <ref> (origin/master by default) apart and takes its screenshots
//   npm run visual:compare [-- <args>]   builds this tree and compares every scene with them (zero pixels apart)
// Anything after the ref goes to Playwright, e.g. `npm run visual:compare -- --grep Vender`.
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, symlinkSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const client = fileURLToPath(new URL('..', import.meta.url));
const repo = path.resolve(client, '..');
const work = path.join(client, 'visual', '.work');
const [mode, ...rest] = process.argv.slice(2);

const run = (command, args, options = {}) => execFileSync(command, args, { stdio: 'inherit', ...options });

function scenes(dist, update, extra) {
  run('npx', ['playwright', 'test', '--config', 'visual/playwright.config.ts', `--update-snapshots=${update}`, ...extra], {
    cwd: client,
    env: { ...process.env, VISUAL_DIST: dist },
  });
}

if (mode === 'baseline') {
  const ref = rest[0] && !rest[0].startsWith('-') ? rest.shift() : 'origin/master';
  const base = path.join(work, 'base');
  if (existsSync(base)) run('git', ['worktree', 'remove', '--force', base], { cwd: repo });
  run('git', ['worktree', 'add', '--detach', '--force', base, ref], { cwd: repo });
  const baseClient = path.join(base, 'client');
  // The same lockfile installs the same packages: the baseline reuses these instead of installing them again.
  const lock = (dir) => readFileSync(path.join(dir, 'package-lock.json'), 'utf8');
  if (lock(baseClient) === lock(client)) symlinkSync(path.join(client, 'node_modules'), path.join(baseClient, 'node_modules'), 'dir');
  else run('npm', ['ci', '--no-audit', '--no-fund'], { cwd: baseClient });
  run('npm', ['run', 'build'], { cwd: baseClient });
  scenes(path.join(baseClient, 'dist'), 'all', rest);
  console.log(`\nLínea base de ${ref} lista en visual/.snapshots. Ahora: npm run visual:compare`);
} else if (mode === 'compare') {
  run('npm', ['run', 'build'], { cwd: client });
  try {
    scenes(path.join(client, 'dist'), 'none', rest);
    console.log('\nSin diferencias contra la línea base.');
  } catch {
    console.error('\nHay escenas distintas de la línea base: npx playwright show-report visual/.work/report');
    process.exitCode = 1;
  }
} else {
  console.error('Uso: node visual/run.mjs baseline [ref] | compare [args de Playwright]');
  process.exitCode = 1;
}
