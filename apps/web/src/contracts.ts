// The browser checks responses at the network boundary. These types mirror
// src/Qraft.Contracts/Messages.cs; no demo records are mixed into live data.
export type ProcessingStatus = 'Processed' | 'Rejected'
export type QualityOutcome = 'Conformant' | 'Nonconformant'
export interface ValidationIssue { code: string; field: string; message: string }
export interface CriterionEvaluation {
  criterion: string
  unit: string | null
  numericValue: number | null
  booleanValue: boolean | null
  minimum: number | null
  maximum: number | null
  isConformant: boolean
}
export interface SampleEvaluation {
  code: string
  isConformant: boolean
  criteria: CriterionEvaluation[]
}
export interface Inspection {
  schemaVersion: number
  resultId: string
  messageId: string
  simulationId: string
  batchId: string
  batchCode: string
  productName: string
  stage: string
  attempt: number
  status: ProcessingStatus
  outcome: QualityOutcome | null
  nonconformityCount: number
  processedAt: string
  errors: ValidationIssue[]
  samples: SampleEvaluation[]
}
export interface Scenario {
  code: string
  name: string
  description: string
  expectedStatus: ProcessingStatus
  expectedOutcome: QualityOutcome | null
}
export interface RunRecord {
  messageId: string
  batchId: string
  batchCode: string
  stage: string
  scenario: string
  publishStatus: 'Pending' | 'Published'
  publishAttempts: number
  publishedAt: string | null
  lastPublishError: string | null
  receivedAt: string | null
  status: ProcessingStatus | null
  outcome: QualityOutcome | null
  resultId: string | null
  payloadJson: string
  resultJson: string | null
  replayCount: number
}
export interface SimulationRun {
  id: string
  scenario: string
  count: number
  createdAt: string
  stats: {
    total: number; pending: number; published: number; processed: number
    rejected: number; conformant: number; nonconformant: number
  }
  records: RunRecord[]
}

type ObjectValue = Record<string, unknown>
function fail(path: string, expectation: string): never {
  throw new Error(`Nieprawidłowa odpowiedź API: ${path} (${expectation}).`)
}
function object(value: unknown, path: string): ObjectValue {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return fail(path, 'obiekt')
  return value as ObjectValue
}
function string(value: unknown, path: string): string {
  if (typeof value !== 'string') return fail(path, 'tekst')
  return value
}
function number(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return fail(path, 'liczba')
  return value
}
function boolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') return fail(path, 'wartość logiczna')
  return value
}
function nullable<T>(value: unknown, parser: (input: unknown, path: string) => T, path: string): T | null {
  return value === null ? null : parser(value, path)
}
function array<T>(value: unknown, parser: (input: unknown, path: string) => T, path: string): T[] {
  if (!Array.isArray(value)) return fail(path, 'lista')
  return value.map((entry, index) => parser(entry, `${path}[${index}]`))
}
function status(value: unknown, path: string): ProcessingStatus {
  if (value !== 'Processed' && value !== 'Rejected') return fail(path, 'nieznany status komunikatu')
  return value
}
function outcome(value: unknown, path: string): QualityOutcome {
  if (value !== 'Conformant' && value !== 'Nonconformant') return fail(path, 'nieznana ocena jakości')
  return value
}
function criterion(value: unknown, path: string): CriterionEvaluation {
  const o = object(value, path)
  return {
    criterion: string(o.criterion, `${path}.criterion`),
    unit: nullable(o.unit, string, `${path}.unit`),
    numericValue: nullable(o.numericValue, number, `${path}.numericValue`),
    booleanValue: nullable(o.booleanValue, boolean, `${path}.booleanValue`),
    minimum: nullable(o.minimum, number, `${path}.minimum`),
    maximum: nullable(o.maximum, number, `${path}.maximum`),
    isConformant: boolean(o.isConformant, `${path}.isConformant`),
  }
}
function sample(value: unknown, path: string): SampleEvaluation {
  const o = object(value, path)
  return { code: string(o.code, `${path}.code`), isConformant: boolean(o.isConformant, `${path}.isConformant`), criteria: array(o.criteria, criterion, `${path}.criteria`) }
}
function issue(value: unknown, path: string): ValidationIssue {
  const o = object(value, path)
  return { code: string(o.code, `${path}.code`), field: string(o.field, `${path}.field`), message: string(o.message, `${path}.message`) }
}
export function parseInspection(value: unknown, path = 'badanie'): Inspection {
  const o = object(value, path)
  if (o.schemaVersion !== 1) return fail(`${path}.schemaVersion`, 'obsługiwana wersja: 1')
  return {
    schemaVersion: 1,
    resultId: string(o.resultId, `${path}.resultId`),
    messageId: string(o.messageId, `${path}.messageId`),
    simulationId: string(o.simulationId, `${path}.simulationId`),
    batchId: string(o.batchId, `${path}.batchId`),
    batchCode: string(o.batchCode, `${path}.batchCode`),
    productName: string(o.productName, `${path}.productName`),
    stage: string(o.stage, `${path}.stage`),
    attempt: number(o.attempt, `${path}.attempt`),
    status: status(o.status, `${path}.status`),
    outcome: nullable(o.outcome, outcome, `${path}.outcome`),
    nonconformityCount: number(o.nonconformityCount, `${path}.nonconformityCount`),
    processedAt: string(o.processedAt, `${path}.processedAt`),
    errors: array(o.errors, issue, `${path}.errors`),
    samples: array(o.samples, sample, `${path}.samples`),
  }
}
export const parseInspections = (value: unknown) => array(value, parseInspection, 'badania')
export function parseScenarios(value: unknown): Scenario[] {
  return array(value, (input, path) => {
    const o = object(input, path)
    return {
      code: string(o.code, `${path}.code`), name: string(o.name, `${path}.name`),
      description: string(o.description, `${path}.description`),
      expectedStatus: status(o.expectedStatus, `${path}.expectedStatus`),
      expectedOutcome: nullable(o.expectedOutcome, outcome, `${path}.expectedOutcome`),
    }
  }, 'scenariusze')
}
export function parseRunRecord(value: unknown, path = 'komunikat'): RunRecord {
  const o = object(value, path)
  if (o.publishStatus !== 'Pending' && o.publishStatus !== 'Published') return fail(`${path}.publishStatus`, 'nieznany status publikacji')
  return {
    messageId: string(o.messageId, `${path}.messageId`), batchId: string(o.batchId, `${path}.batchId`),
    batchCode: string(o.batchCode, `${path}.batchCode`), stage: string(o.stage, `${path}.stage`),
    scenario: string(o.scenario, `${path}.scenario`), publishStatus: o.publishStatus,
    publishAttempts: number(o.publishAttempts, `${path}.publishAttempts`),
    publishedAt: nullable(o.publishedAt, string, `${path}.publishedAt`),
    lastPublishError: nullable(o.lastPublishError, string, `${path}.lastPublishError`),
    receivedAt: nullable(o.receivedAt, string, `${path}.receivedAt`),
    status: nullable(o.status, status, `${path}.status`),
    outcome: nullable(o.outcome, outcome, `${path}.outcome`),
    resultId: nullable(o.resultId, string, `${path}.resultId`),
    payloadJson: string(o.payloadJson, `${path}.payloadJson`),
    resultJson: nullable(o.resultJson, string, `${path}.resultJson`),
    replayCount: number(o.replayCount, `${path}.replayCount`),
  }
}
export function parseRun(value: unknown, path = 'symulacja'): SimulationRun {
  const o = object(value, path)
  const s = object(o.stats, `${path}.stats`)
  return {
    id: string(o.id, `${path}.id`), scenario: string(o.scenario, `${path}.scenario`),
    count: number(o.count, `${path}.count`), createdAt: string(o.createdAt, `${path}.createdAt`),
    stats: {
      total: number(s.total, `${path}.stats.total`), pending: number(s.pending, `${path}.stats.pending`),
      published: number(s.published, `${path}.stats.published`), processed: number(s.processed, `${path}.stats.processed`),
      rejected: number(s.rejected, `${path}.stats.rejected`), conformant: number(s.conformant, `${path}.stats.conformant`),
      nonconformant: number(s.nonconformant, `${path}.stats.nonconformant`),
    },
    records: array(o.records, parseRunRecord, `${path}.records`),
  }
}
export const parseRuns = (value: unknown) => array(value, parseRun, 'symulacje')

export function processingLabel(value: ProcessingStatus): string {
  return value === 'Processed' ? 'Przetworzony' : 'Odrzucony komunikat'
}
export function qualityLabel(value: QualityOutcome | null): string {
  return value === 'Conformant' ? 'Zgodna' : value === 'Nonconformant' ? 'Niezgodna' : 'Bez oceny'
}
export function runLabel(run: SimulationRun): string {
  if (run.stats.processed + run.stats.rejected === run.stats.total) return 'Zakończona'
  if (run.stats.pending > 0) return 'Oczekuje na publikację'
  return 'Oczekuje na wynik Qraft'
}
