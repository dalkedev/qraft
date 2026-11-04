import { existsSync, readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const marker = path.join(root, '.dev/runner.json');
if (!existsSync(marker)) {
  console.log('Brak działającego launchera Qraft. Broker możesz zatrzymać: npm run infra:stop');
} else {
  const state = JSON.parse(readFileSync(marker, 'utf8'));
  let command = '';
  try { command = execFileSync('ps', ['-p', String(state.pid), '-o', 'args='], { encoding: 'utf8' }); } catch { /* stale marker */ }
  if (state.root !== root || !command.includes('dev.mjs')) throw new Error('PID nie należy do launchera Qraft; nie zatrzymuję procesu.');
  process.kill(state.pid, 'SIGTERM');
  console.log('Zatrzymuję launcher Qraft. Dane pozostają zapisane.');
}
