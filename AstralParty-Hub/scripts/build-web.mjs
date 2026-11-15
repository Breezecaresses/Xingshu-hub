import { execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const distDir = path.join(root, 'dist');
const serverWebHub = path.resolve(root, '../../星引擎插件/XingShu-Server/xingshu_server/web_hub');

console.log('[build-web] Running TypeScript check + Vite build (VITE_BASE=/xingshu-hub/)...');
execSync('npx tsc && npx vite build', {
  cwd: root,
  stdio: 'inherit',
  env: {
    ...process.env,
    VITE_BASE: process.env.VITE_BASE || '/xingshu-hub/',
  },
});

if (!fs.existsSync(path.join(distDir, 'index.html'))) {
  throw new Error(`Missing built index.html at ${distDir}`);
}

console.log(`[build-web] Deploying dist/ -> ${serverWebHub}`);
fs.rmSync(serverWebHub, { recursive: true, force: true });
fs.mkdirSync(serverWebHub, { recursive: true });
fs.cpSync(distDir, serverWebHub, { recursive: true });
console.log('[build-web] Done.');
