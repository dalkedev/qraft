import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const compose = ['compose', '--project-directory', root, '--file', path.join(root, 'infra/compose.yaml')];
const simulator = 'http://127.0.0.1:5281';
const post = (route, body) => fetch(simulator + route, {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
});
let run;
try {
  execFileSync('docker', [...compose, 'stop', 'rabbitmq'], { stdio: 'inherit' });
  const response = await post('/api/runs', { scenario: 'good', count: 1 });
  assert.equal(response.status, 201);
  run = await response.json();
  await delay(1500);
  const queued = await fetch(`${simulator}/api/runs/${run.id}`).then(response => response.json());
  assert.equal(queued.records[0].status, null);
  assert.equal(queued.records[0].publishStatus, 'Pending');
  console.log('PASS broker offline: źródło i outbox zapisane, wynik nie jest udawany');
} finally {
  execFileSync('docker', [...compose, 'up', '-d', '--wait', 'rabbitmq'], { stdio: 'inherit' });
}
let finished;
for (let attempt = 0; attempt < 180; attempt++) {
  finished = await fetch(`${simulator}/api/runs/${run.id}`).then(response => response.json());
  if (finished.records[0].status !== null) break;
  await delay(500);
}
assert.equal(finished.records[0].status, 'Processed');
assert.equal(finished.records[0].outcome, 'Conformant');
const saved = await fetch(`http://127.0.0.1:5280/api/inspections/${finished.records[0].messageId}`).then(response => response.json());
assert.equal(saved.resultId, finished.records[0].resultId);
console.log('PASS broker recovery: wiadomość dotarła i wynik zapisano w obu bazach');
