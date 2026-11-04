import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, existsSync, readFileSync, writeFileSync, unlinkSync, createWriteStream } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import net from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
process.chdir(root);
process.env.PATH = `${path.dirname(process.execPath)}${path.delimiter}${process.env.PATH ?? ''}`;
if (existsSync(path.join(root, '.env'))) process.loadEnvFile(path.join(root, '.env'));
const noWatch = process.argv.includes('--no-watch');
const skipBroker = process.argv.includes('--skip-broker');
const marker = path.join(root, '.dev/runner.json');
const children = [];
const composeArgs = ['compose', '--project-directory', root, '--file', path.join(root, 'infra/compose.yaml')];
let stopping = false;
let brokerOwned = false;
let wake;
const interrupted = new Promise(resolve => { wake = resolve; });
process.on('SIGINT', () => wake());
process.on('SIGTERM', () => wake());

function alive(pid) { try { process.kill(pid, 0); return true; } catch { return false; } }

function run(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd: root, stdio: 'inherit' });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolve() : reject(new Error(`${command} zakończył się kodem ${code}`)));
  });
}

async function freePort(port) {
  await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', () => reject(new Error(`Port ${port} jest zajęty. Nie zatrzymuję obcego procesu.`)));
    server.listen(port, '127.0.0.1', () => server.close(resolve));
  });
}

function service(name, command, args, extra = {}) {
  const log = createWriteStream(path.join(root, '.dev/logs', `${name}.log`), { flags: 'a' });
  const child = spawn(command, args, {
    cwd: root, detached: true, stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, ...extra }
  });
  child.stdout.on('data', data => { log.write(data); process.stdout.write(`[${name}] ${data}`); });
  child.stderr.on('data', data => { log.write(data); process.stderr.write(`[${name}] ${data}`); });
  child.on('error', error => { console.error(`[${name}] ${error.message}`); process.exitCode = 1; wake(); });
  child.on('exit', code => {
    log.end();
    if (!stopping) { console.error(`${name} zatrzymany (${code}).`); process.exitCode = 1; wake(); }
  });
  children.push(child);
  return child;
}

async function ready(url, child) {
  for (let attempt = 0; attempt < 120; attempt++) {
    if (child.exitCode !== null) throw new Error(`Proces zakończył się przed uruchomieniem ${url}`);
    const response = await fetch(url, { signal: AbortSignal.timeout(1000) }).catch(() => null);
    if (response?.ok) return;
    await delay(500);
  }
  throw new Error(`Brak gotowości: ${url}. Sprawdź .dev/logs.`);
}

async function stop() {
  stopping = true;
  for (const child of children) {
    try { process.kill(-child.pid, 'SIGTERM'); } catch { /* already stopped */ }
  }
  await delay(800);
  for (const child of children) {
    try { process.kill(-child.pid, 'SIGKILL'); } catch { /* already stopped */ }
  }
  if (existsSync(marker) && JSON.parse(readFileSync(marker, 'utf8')).pid === process.pid) unlinkSync(marker);
  if (brokerOwned) await run('docker', [...composeArgs, 'stop']).catch(error => console.error(error.message));
}

try {
  mkdirSync(path.join(root, '.dev/logs'), { recursive: true });
  mkdirSync(path.join(root, 'data'), { recursive: true });
  if (existsSync(marker)) {
    const previous = JSON.parse(readFileSync(marker, 'utf8'));
    if (alive(previous.pid)) throw new Error(`Qraft już działa (PID ${previous.pid}). Zatrzymaj przez npm run stop.`);
  }
  for (const port of [5280, 5281, 5173]) await freePort(port);
  const version = execFileSync('node', ['--version'], { encoding: 'utf8' });
  if (!version.startsWith('v22.')) throw new Error('Użyj Node.js 22: fnm use 22.');
  writeFileSync(marker, JSON.stringify({ pid: process.pid, root, startedAt: new Date().toISOString() }));
  if (!skipBroker) {
    brokerOwned = true;
    await run('docker', [...composeArgs, 'up', '-d', '--wait']);
  }
  await run('dotnet', ['build', 'Qraft.slnx', '--no-restore', '-m:1']);
  const shared = {
    DOTNET_ENVIRONMENT: 'Development', ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER: '1', DOTNET_WATCH_SUPPRESS_EMOJIS: '1',
    DOTNET_WATCH_RESTART_ON_RUDE_EDIT: '1',
    RabbitMq__Port: process.env.QRAFT_AMQP_PORT ?? '5677',
    RabbitMq__UserName: process.env.QRAFT_RABBITMQ_USER ?? 'qraft',
    RabbitMq__Password: process.env.QRAFT_RABBITMQ_PASSWORD ?? 'qraft-local-dev'
  };
  function dotnet(name, extra) {
    const project = `src/Qraft.${name}/Qraft.${name}.csproj`;
    const args = noWatch
      ? ['run', '--project', project, '--no-build', '--no-launch-profile']
      : ['watch', '--project', project, '--non-interactive', 'run', '--no-launch-profile', '--no-restore', '--property:BuildInParallel=false'];
    return service(name.toLowerCase(), 'dotnet', args, { ...shared, ...extra });
  }
  const api = dotnet('Api', { ASPNETCORE_URLS: 'http://127.0.0.1:5280', Data__DatabasePath: path.join(root, 'data/qraft.db') });
  await ready('http://127.0.0.1:5280/health/ready', api);
  const simulator = dotnet('Simulator', { ASPNETCORE_URLS: 'http://127.0.0.1:5281', Data__DatabasePath: path.join(root, 'data/simulator.db') });
  await ready('http://127.0.0.1:5281/health/ready', simulator);
  dotnet('Worker', { Data__DatabasePath: path.join(root, 'data/qraft.db') });
  const web = service('web', 'npm', ['--prefix', 'apps/web', 'run', 'dev', '--', '--host', '127.0.0.1', '--port', '5173', '--strictPort']);
  await ready('http://127.0.0.1:5173', web);
  console.log('\nQraft: http://127.0.0.1:5173 | API: :5280 | Symulator: :5281 | RabbitMQ: :15677\nCtrl+C zatrzymuje usługi, zachowując obie bazy i wolumen kolejki.\n');
  await interrupted;
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  await stop();
}
