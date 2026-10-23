import { readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const testRoot = new URL('./', import.meta.url);
const projectRoot = fileURLToPath(new URL('../../', import.meta.url));
const suites = readdirSync(testRoot).filter(name => /\.test\.(ts|mjs)$/.test(name)).sort();
let failed = 0;
for (const name of suites) {
  const result = spawnSync(process.execPath, ['--experimental-strip-types', fileURLToPath(new URL(name, testRoot))], {
    cwd: projectRoot, encoding: 'utf8', timeout: 60_000,
  });
  const output = (result.stdout || '') + (result.stderr || '');
  // Some historical suites logged exceptions without setting a failing exit code.
  const passed = !result.error && result.status === 0 && !/\[FAIL\]|\[TEST_EXCEPTION\]/.test(output);
  console.log(`${passed ? 'PASS' : 'FAIL'} ${name}`);
  if (!passed) {
    failed++;
    if (result.error) console.error(result.error.message);
    console.error(output);
  }
}
console.log(`${suites.length - failed}/${suites.length} suites passed`);
process.exitCode = failed ? 1 : 0;
