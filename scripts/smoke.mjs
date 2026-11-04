import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';

const qraft = process.env.QRAFT_API_URL ?? 'http://127.0.0.1:5280';
const simulator = process.env.QRAFT_SIMULATOR_URL ?? 'http://127.0.0.1:5281';

async function request(base, route, body) {
  const response = await fetch(base + route, {
    method: body === undefined ? 'GET' : 'POST',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(5000)
  });
  if (!response.ok) throw new Error(`${response.status} ${route}: ${await response.text()}`);
  return response.json();
}

async function completed(runId) {
  for (let attempt = 0; attempt < 120; attempt++) {
    const run = await request(simulator, `/api/runs/${runId}`);
    if (run.records.every(record => record.status !== null)) return run;
    await delay(250);
  }
  throw new Error(`Brak odpowiedzi dla przebiegu ${runId}. Sprawdź worker i kolejki.`);
}

await request(qraft, '/health/ready');
await request(simulator, '/health/ready');
const scenarios = [
  ['good', 'Processed', 'Conformant'],
  ['nonconformant', 'Processed', 'Nonconformant'],
  ['boundary', 'Processed', 'Conformant'],
  ['invalid-payload', 'Rejected', null],
  ['malformed-json', 'Rejected', null]
];
let replayRecord;
for (const [scenario, status, outcome] of scenarios) {
  const created = await request(simulator, '/api/runs', { scenario, count: 1 });
  const run = await completed(created.id);
  const source = run.records[0];
  const result = await request(qraft, `/api/inspections/${source.messageId}`);
  assert.equal(source.status, status);
  assert.equal(source.outcome, outcome);
  assert.equal(result.status, status);
  assert.equal(result.outcome, outcome);
  assert.equal(source.resultId, result.resultId);
  assert.equal(result.simulationId, created.id);
  assert.equal(result.messageId, source.messageId);
  if (status === 'Processed') {
    assert.equal(result.samples.length, 3);
    assert.ok(result.samples.every(sample => sample.criteria.length === 3));
    assert.equal(result.nonconformityCount, outcome === 'Conformant' ? 0 : 1);
  } else assert.ok(result.errors.length > 0);
  if (scenario === 'good') replayRecord = { source, result, runId: created.id };
  console.log(`PASS ${scenario}: ${status}/${outcome ?? 'brak oceny jakości'}`);
}

const before = await request(qraft, '/api/summary');
await request(simulator, `/api/messages/${replayRecord.source.messageId}/replay`, {});
let replayed;
for (let attempt = 0; attempt < 80; attempt++) {
  replayed = await request(simulator, `/api/runs/${replayRecord.runId}`);
  if (replayed.records[0].replayCount >= 1 && replayed.records[0].publishStatus === 'Published') break;
  await delay(250);
}
assert.ok(replayed.records[0].replayCount >= 1);
assert.equal(replayed.records[0].publishStatus, 'Published');
await delay(1000);
const after = await request(qraft, '/api/summary');
const original = await request(qraft, `/api/inspections/${replayRecord.source.messageId}`);
assert.equal(after.received, before.received);
assert.equal(original.resultId, replayRecord.result.resultId);
assert.equal(replayed.records[0].resultId, replayRecord.result.resultId);
console.log('PASS replay: jeden zapis i ten sam ResultId w obu bazach');
console.log('Przepływ symulator → RabbitMQ → Qraft → RabbitMQ → symulator działa.');
