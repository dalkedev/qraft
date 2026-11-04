import { setTimeout as delay } from 'node:timers/promises';
for (let attempt = 0; attempt < 120; attempt++) {
  const states = await Promise.all([5173, 5280, 5281].map(async port => {
    const suffix = port === 5173 ? '' : '/health/ready';
    return fetch(`http://127.0.0.1:${port}${suffix}`, { signal: AbortSignal.timeout(1000) })
      .then(response => response.ok).catch(() => false);
  }));
  if (states.every(Boolean)) process.exit(0);
  await delay(500);
}
throw new Error('Qraft nie osiągnął gotowości. Sprawdź logi launchera.');
