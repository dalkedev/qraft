import { describe, expect, it } from 'vitest'
import { parseInspection, parseRun, parseRunRecord, processingLabel, qualityLabel, runLabel } from './contracts'

const validInspection = {
  schemaVersion: 1,
  resultId: '00000000-0000-0000-0000-000000000001',
  messageId: '00000000-0000-0000-0000-000000000002',
  simulationId: '00000000-0000-0000-0000-000000000003',
  batchId: '00000000-0000-0000-0000-000000000004',
  batchCode: 'PP-TEST-001', productName: 'PP Demo', stage: 'E1', attempt: 1,
  status: 'Processed', outcome: 'Nonconformant', nonconformityCount: 1,
  processedAt: '2026-10-24T16:00:00Z', errors: [],
  samples: [{ code: 'P01', isConformant: false, criteria: [{ criterion: 'MoisturePercent', unit: '%', numericValue: .28, booleanValue: null, minimum: 0, maximum: .2, isConformant: false }] }],
}
const pendingRecord = {
  messageId: validInspection.messageId, batchId: validInspection.batchId,
  batchCode: validInspection.batchCode, stage: 'E1', scenario: 'conformant',
  publishStatus: 'Pending', publishAttempts: 1, publishedAt: null,
  lastPublishError: 'Broker unavailable', receivedAt: null, status: null, outcome: null,
  resultId: null, payloadJson: '{}', resultJson: null, replayCount: 0,
}

describe('API boundary and independent processing / quality labels', () => {
  it('keeps valid nonconformant measurements distinct from rejected messages', () => {
    const inspection = parseInspection(validInspection)
    expect(processingLabel(inspection.status)).toBe('Przetworzony')
    expect(qualityLabel(inspection.outcome)).toBe('Niezgodna')
    expect(inspection.samples[0]?.criteria[0]?.numericValue).toBe(.28)
  })
  it('shows no quality decision for a rejected malformed message', () => {
    const inspection = parseInspection({ ...validInspection, status: 'Rejected', outcome: null, samples: [], errors: [{ code: 'MissingSamples', field: 'samples', message: 'Brak próbek' }] })
    expect(processingLabel(inspection.status)).toBe('Odrzucony komunikat')
    expect(qualityLabel(inspection.outcome)).toBe('Bez oceny')
  })
  it.each(['Received', 'Success', '', null])('rejects unsupported processing status %s', (status) => {
    expect(() => parseInspection({ ...validInspection, status })).toThrow('nieznany status komunikatu')
  })
  it('rejects incomplete sample details instead of replacing them with success', () => {
    expect(() => parseInspection({ ...validInspection, samples: [{ code: 'P01', isConformant: true }] })).toThrow('samples[0].criteria')
  })
  it('rejects a future schema version explicitly', () => {
    expect(() => parseInspection({ ...validInspection, schemaVersion: 2 })).toThrow('schemaVersion')
  })
  it('retains broker failures and pending result status from the simulator', () => {
    const record = parseRunRecord(pendingRecord)
    expect(record.status).toBeNull()
    expect(record.publishStatus).toBe('Pending')
    expect(record.lastPublishError).toBe('Broker unavailable')
  })
  it('does not call a merely published run finished', () => {
    const run = parseRun({ id: 'run', scenario: 'conformant', count: 1, createdAt: '2026-10-24T16:00:00Z', stats: { total: 1, pending: 0, published: 1, processed: 0, rejected: 0, conformant: 0, nonconformant: 0 }, records: [{ ...pendingRecord, publishStatus: 'Published', lastPublishError: null }] })
    expect(runLabel(run)).toBe('Oczekuje na wynik Qraft')
  })
})
