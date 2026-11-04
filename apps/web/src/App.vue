<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { checkHealth, errorMessage, requestJson, type ServiceHealth } from './api'
import {
  parseInspection, parseInspections, parseRun, parseRunRecord, parseRuns, parseScenarios,
  processingLabel, qualityLabel, runLabel,
  type CriterionEvaluation, type Inspection, type RunRecord, type Scenario, type SimulationRun,
} from './contracts'

const inspections = ref<Inspection[]>([])
const scenarios = ref<Scenario[]>([])
const runs = ref<SimulationRun[]>([])
const selectedId = ref<string | null>(null)
const detail = ref<Inspection | null>(null)
const detailLoading = ref(false)
const detailError = ref('')
const inspectionError = ref('')
const simulatorError = ref('')
const scenarioError = ref('')
const refreshing = ref(false)
const initialLoading = ref(true)
const paused = ref(false)
const lastUpdate = ref<Date | null>(null)
const scenario = ref('')
const count = ref(1)
const submitting = ref(false)
const actionError = ref('')
const notice = ref('')
const expandedRun = ref<string | null>(null)
const replaying = ref(new Set<string>())
const search = ref('')
const filter = ref('all')
const detailElement = ref<HTMLElement | null>(null)
const checking: ServiceHealth = { state: 'checking', label: 'Sprawdzanie', details: 'Oczekiwanie na odpowiedź serwisu.' }
const apiHealth = ref<ServiceHealth>({ ...checking })
const simulatorHealth = ref<ServiceHealth>({ ...checking })
const numberFormat = new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 6 })
const dateFormat = new Intl.DateTimeFormat('pl-PL', { dateStyle: 'short', timeStyle: 'medium' })
const selectedScenario = computed(() => scenarios.value.find((item) => item.code === scenario.value))
const selected = computed(() => detail.value?.messageId === selectedId.value ? detail.value : inspections.value.find((item) => item.messageId === selectedId.value) ?? null)
const visibleInspections = computed(() => inspections.value.filter((item) => {
  const term = search.value.trim().toLocaleLowerCase('pl')
  const matchesSearch = `${item.batchCode} ${item.productName} ${item.messageId}`.toLocaleLowerCase('pl').includes(term)
  const matchesFilter = filter.value === 'all' || (filter.value === 'rejected' ? item.status === 'Rejected' : item.outcome === 'Nonconformant')
  return matchesSearch && matchesFilter
}))
const processedCount = computed(() => inspections.value.filter((item) => item.status === 'Processed').length)
const rejectedCount = computed(() => inspections.value.filter((item) => item.status === 'Rejected').length)
const nonconformantCount = computed(() => inspections.value.filter((item) => item.outcome === 'Nonconformant').length)

let disposed = false
let timer: ReturnType<typeof setTimeout> | undefined
let pollController: AbortController | null = null
let detailController: AbortController | null = null
let scenariosLoaded = false
const mutations = new Set<AbortController>()

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : dateFormat.format(date)
}
function shortId(value: string): string { return value.slice(0, 8) }
function criterionName(value: string): string {
  return ({ MoisturePercent: 'Wilgotność', DensityKgM3: 'Gęstość', ColorConformant: 'Barwa', moisture: 'Wilgotność', density: 'Gęstość', color: 'Barwa' } as Record<string, string>)[value] ?? value
}
function measurement(criterion: CriterionEvaluation): string {
  if (criterion.booleanValue !== null) return criterion.booleanValue ? 'Tak' : 'Nie'
  return criterion.numericValue === null ? 'Brak wartości' : `${numberFormat.format(criterion.numericValue)}${criterion.unit ? ` ${criterion.unit}` : ''}`
}
function limits(criterion: CriterionEvaluation): string {
  if (criterion.booleanValue !== null) return 'Zgodność ze wzorcem'
  const minimum = criterion.minimum === null ? null : numberFormat.format(criterion.minimum)
  const maximum = criterion.maximum === null ? null : numberFormat.format(criterion.maximum)
  if (minimum !== null && maximum !== null) return `${minimum} – ${maximum}${criterion.unit ? ` ${criterion.unit}` : ''}`
  return maximum !== null ? `≤ ${maximum}${criterion.unit ? ` ${criterion.unit}` : ''}` : minimum !== null ? `≥ ${minimum}${criterion.unit ? ` ${criterion.unit}` : ''}` : 'Brak granic w odpowiedzi'
}
function recordLabel(record: RunRecord): string {
  if (record.status !== null) return processingLabel(record.status)
  return record.publishStatus === 'Pending' ? 'Czeka na publikację' : 'Czeka na wynik'
}

async function refresh(): Promise<void> {
  if (refreshing.value || disposed) return
  refreshing.value = true
  const controller = new AbortController()
  pollController = controller
  const signal = controller.signal
  await Promise.allSettled([
    (async () => {
      try {
        const response = await requestJson('/api/inspections', parseInspections, signal)
        if (disposed) return
        inspections.value = response
        inspectionError.value = ''
        lastUpdate.value = new Date()
        if (selectedId.value === null && response.length > 0) selectedId.value = response[0]!.messageId
        const current = response.find((item) => item.messageId === selectedId.value)
        if (current) detail.value = current
      } catch (error) { if (!signal.aborted) inspectionError.value = errorMessage(error) }
    })(),
    (async () => {
      try {
        const response = await requestJson('/simulator-api/runs', parseRuns, signal)
        if (disposed) return
        runs.value = response
        simulatorError.value = ''
      } catch (error) { if (!signal.aborted) simulatorError.value = errorMessage(error) }
    })(),
    (async () => {
      if (scenariosLoaded) return
      try {
        const response = await requestJson('/simulator-api/scenarios', parseScenarios, signal)
        if (disposed) return
        scenarios.value = response
        scenariosLoaded = true
        scenarioError.value = ''
        if (!scenario.value) scenario.value = response[0]?.code ?? ''
      } catch (error) { if (!signal.aborted) scenarioError.value = errorMessage(error) }
    })(),
    (async () => { apiHealth.value = await checkHealth('/qraft-health', signal) })(),
    (async () => { simulatorHealth.value = await checkHealth('/simulator-health', signal) })(),
  ])
  if (!disposed) { refreshing.value = false; initialLoading.value = false }
  if (pollController === controller) pollController = null
}

async function poll(): Promise<void> {
  if (disposed) return
  if (!paused.value && document.visibilityState !== 'hidden') await refresh()
  if (!disposed) timer = setTimeout(() => void poll(), 2_000)
}
watch(selectedId, async (id) => {
  detailController?.abort()
  detail.value = null
  detailError.value = ''
  if (!id) return
  const controller = new AbortController()
  detailController = controller
  detailLoading.value = true
  try {
    const response = await requestJson(`/api/inspections/${encodeURIComponent(id)}`, parseInspection, controller.signal)
    if (!controller.signal.aborted) detail.value = response
  } catch (error) { if (!controller.signal.aborted) detailError.value = errorMessage(error) }
  finally { if (!controller.signal.aborted) detailLoading.value = false }
})
async function selectInspection(id: string): Promise<void> {
  selectedId.value = id
  if (window.matchMedia('(max-width: 1100px)').matches) {
    await nextTick()
    detailElement.value?.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' })
  }
}
async function startSimulation(): Promise<void> {
  if (submitting.value) return
  actionError.value = ''
  notice.value = ''
  if (!selectedScenario.value || !Number.isInteger(count.value) || count.value < 1 || count.value > 20) {
    actionError.value = 'Wybierz scenariusz i liczbę komunikatów od 1 do 20.'
    return
  }
  submitting.value = true
  const controller = new AbortController()
  mutations.add(controller)
  try {
    const run = await requestJson('/simulator-api/runs', parseRun, controller.signal, { method: 'POST', body: { scenario: scenario.value, count: count.value } })
    if (disposed) return
    runs.value = [run, ...runs.value.filter((item) => item.id !== run.id)]
    expandedRun.value = run.id
    notice.value = `Utworzono symulację ${shortId(run.id)} z ${run.count} komunikatami. Status publikacji jest widoczny poniżej.`
  } catch (error) {
    if (!controller.signal.aborted) actionError.value = `${errorMessage(error)} Przed ponowieniem sprawdź listę symulacji.`
  } finally { mutations.delete(controller); if (!disposed) submitting.value = false }
}
async function replay(messageId: string): Promise<void> {
  if (replaying.value.has(messageId)) return
  replaying.value = new Set([...replaying.value, messageId])
  actionError.value = ''
  notice.value = ''
  const controller = new AbortController()
  mutations.add(controller)
  try {
    await requestJson(`/simulator-api/messages/${encodeURIComponent(messageId)}/replay`, parseRunRecord, controller.signal, { method: 'POST' })
    if (disposed) return
    notice.value = `Zlecono ponowne wysłanie komunikatu ${shortId(messageId)} z tym samym messageId. Sprawdź wynik i licznik powtórzeń.`
    await refresh()
  } catch (error) { if (!controller.signal.aborted) actionError.value = errorMessage(error) }
  finally { mutations.delete(controller); replaying.value = new Set([...replaying.value].filter((id) => id !== messageId)) }
}

onMounted(() => void poll())
onBeforeUnmount(() => {
  disposed = true
  clearTimeout(timer)
  pollController?.abort()
  detailController?.abort()
  mutations.forEach((controller) => controller.abort())
})
</script>

<template>
  <a class="skip-link" href="#results">Przejdź do wyników</a>
  <header class="topbar">
    <a class="brand" href="#"><span class="brand-mark" aria-hidden="true">Q</span><span>Qraft <span class="brand-light">Lab</span></span></a>
    <nav aria-label="Nawigacja"><a href="#results">Badania</a><a href="#runs">Symulacje</a></nav>
    <div class="services">
      <details v-for="service in [{ name: 'Qraft API', health: apiHealth }, { name: 'Symulator', health: simulatorHealth }]" :key="service.name" class="service">
        <summary><span class="dot" :class="service.health.state"></span>{{ service.name }} <span class="service-label">{{ service.health.label }}</span></summary>
        <div class="health-detail"><strong>{{ service.name }}</strong><pre>{{ service.health.details }}</pre></div>
      </details>
    </div>
  </header>

  <main>
    <div class="intro">
      <div><p class="environment">Lokalne laboratorium</p><h1>Od komunikatu<br class="mobile-break" /> do oceny jakości.</h1><p>Wygeneruj pomiary. Obserwuj przetwarzanie. Sprawdź każdą próbkę.</p></div>
      <div class="refresh-controls">
        <button class="button secondary" type="button" :disabled="refreshing" @click="refresh">{{ refreshing ? 'Odświeżanie…' : 'Odśwież dane' }}</button>
        <button class="poll-control" type="button" :aria-pressed="paused" @click="paused = !paused"><span class="dot" :class="paused ? 'checking' : 'ready'"></span>{{ paused ? 'Wznów odświeżanie' : 'Wstrzymaj odświeżanie' }}</button>
        <span class="refresh-time">{{ lastUpdate ? `Wyniki: ${lastUpdate.toLocaleTimeString('pl-PL')}` : 'Oczekiwanie na dane' }}<span v-if="!paused"> · co 2 s</span></span>
      </div>
    </div>

    <div v-if="notice" class="notice success" role="status"><span>{{ notice }}</span><button type="button" aria-label="Zamknij komunikat" @click="notice = ''">×</button></div>
    <div v-if="actionError" class="notice error" role="alert"><span>{{ actionError }}</span><button type="button" aria-label="Zamknij błąd" @click="actionError = ''">×</button></div>

    <div class="workspace">
      <aside class="generator panel">
        <div class="section-heading"><span class="section-glyph" aria-hidden="true">↗</span><h2>Generator pomiarów</h2></div>
        <p class="muted">Scenariusz tworzy komunikaty wejściowe dla Qraft.</p>
        <form @submit.prevent="startSimulation">
          <label for="scenario">Scenariusz</label>
          <select id="scenario" v-model="scenario" :disabled="!scenarios.length || submitting" required>
            <option v-if="!scenarios.length" value="">{{ initialLoading ? 'Wczytywanie…' : 'Brak scenariuszy' }}</option>
            <option v-for="item in scenarios" :key="item.code" :value="item.code">{{ item.name }}</option>
          </select>
          <p v-if="scenarioError" class="field-error">{{ scenarioError }}</p>
          <div v-if="selectedScenario" class="scenario-description"><p>{{ selectedScenario.description }}</p><p class="expected-result">Oczekiwany komunikat: <strong>{{ processingLabel(selectedScenario.expectedStatus) }}</strong><template v-if="selectedScenario.expectedOutcome">. Jakość: <strong>{{ qualityLabel(selectedScenario.expectedOutcome).toLocaleLowerCase('pl') }}</strong></template>.</p></div>
          <label for="message-count">Liczba komunikatów <span class="optional">1–20</span></label>
          <input id="message-count" v-model.number="count" type="number" min="1" max="20" step="1" required :disabled="submitting" />
          <button class="button primary start-button" type="submit" :disabled="submitting || !selectedScenario">{{ submitting ? 'Tworzenie symulacji…' : 'Uruchom symulację' }}<span aria-hidden="true">▶</span></button>
        </form>
        <div class="process-path" aria-label="Droga komunikatu"><span>Symulator</span><span aria-hidden="true">↓</span><span>RabbitMQ</span><span aria-hidden="true">↓</span><strong>Qraft</strong><span aria-hidden="true">↓</span><span>Wynik badania</span></div>
        <p class="small muted">Gotowość usług sprawdzisz w nagłówku. Liczniki pokazują dane zwrócone przez API.</p>
      </aside>

      <section id="results" class="results panel" aria-labelledby="results-title" :aria-busy="initialLoading">
        <div class="section-heading results-heading"><h2 id="results-title">Wyniki Qraft</h2><span class="count">{{ inspections.length }}</span></div>
        <div class="result-totals"><span><strong>{{ processedCount }}</strong> przetworzonych</span><span><strong>{{ nonconformantCount }}</strong> niezgodnych jakościowo</span><span><strong>{{ rejectedCount }}</strong> odrzuconych komunikatów</span></div>
        <div class="filters"><label class="search-label"><span class="sr-only">Szukaj partii lub komunikatu</span><input v-model="search" type="search" placeholder="Szukaj partii lub komunikatu…" /></label><label><span class="sr-only">Filtr wyników</span><select v-model="filter"><option value="all">Wszystkie wyniki</option><option value="nonconformant">Jakość niezgodna</option><option value="rejected">Odrzucone komunikaty</option></select></label></div>
        <p v-if="inspectionError" class="inline-error" role="alert">{{ inspectionError }}<span v-if="inspections.length"> Widoczne dane pochodzą z ostatniego udanego odczytu.</span></p>
        <div v-if="initialLoading" class="empty-state"><span class="loading-indicator"></span><p>Wczytywanie wyników z Qraft…</p></div>
        <div v-else-if="!inspections.length && !inspectionError" class="empty-state"><span class="empty-symbol" aria-hidden="true">⌁</span><h3>Tu pojawi się pierwszy wynik</h3><p>Uruchom scenariusz. Po przetworzeniu komunikatu zobaczysz ocenę próbek.</p></div>
        <div v-else-if="!visibleInspections.length && inspections.length" class="empty-state"><h3>Brak wyników dla tych filtrów</h3><button class="text-button" type="button" @click="search = ''; filter = 'all'">Wyczyść filtry</button></div>
        <div v-else-if="visibleInspections.length" class="inspection-list">
          <div class="list-labels" aria-hidden="true"><span>Partia / etap</span><span>Komunikat</span><span>Jakość</span></div>
          <button v-for="item in visibleInspections" :key="item.messageId" class="inspection-row" :class="{ selected: selectedId === item.messageId }" type="button" :aria-pressed="selectedId === item.messageId" @click="selectInspection(item.messageId)">
            <span class="batch-cell"><strong>{{ item.batchCode || 'Bez kodu partii' }}</strong><span>{{ item.stage || 'Etap nieznany' }} · próba {{ item.attempt }}</span><small>{{ formatDate(item.processedAt) }}</small></span>
            <span class="status-cell"><span class="badge" :class="item.status === 'Rejected' ? 'rejected' : 'neutral'">{{ processingLabel(item.status) }}</span><small>{{ shortId(item.messageId) }}</small></span>
            <span class="quality-cell"><span class="quality-dot" :class="item.outcome === 'Conformant' ? 'good' : item.outcome === 'Nonconformant' ? 'bad' : 'none'"></span><strong>{{ qualityLabel(item.outcome) }}</strong><small v-if="item.nonconformityCount">{{ item.nonconformityCount }} niezgodności</small></span>
          </button>
        </div>
        <p class="list-note">Odrzucony komunikat zawiera błędy danych. Niezgodna jakość oznacza poprawne dane i pomiary poza kryteriami.</p>
      </section>

      <section ref="detailElement" class="detail panel" aria-labelledby="detail-title" :aria-busy="detailLoading">
        <div class="section-heading"><h2 id="detail-title">Szczegóły badania</h2><span v-if="detailLoading" class="loading-indicator" aria-label="Wczytywanie szczegółów"></span></div>
        <p v-if="detailError" class="inline-error" role="alert">{{ detailError }}</p>
        <div v-if="!selected" class="empty-state detail-empty"><span class="empty-symbol" aria-hidden="true">⊞</span><p>Wybierz wynik, aby zobaczyć pomiary i odpowiedź Qraft.</p></div>
        <template v-else>
          <div class="detail-summary"><h3>{{ selected.batchCode || 'Partia bez kodu' }}</h3><p>{{ selected.productName || 'Brak nazwy produktu' }}</p><div class="detail-badges"><span class="badge neutral">{{ selected.stage || 'Brak etapu' }} · próba {{ selected.attempt }}</span><span class="badge" :class="selected.outcome === 'Conformant' ? 'conformant' : selected.outcome === 'Nonconformant' ? 'nonconformant' : 'rejected'">{{ selected.status === 'Rejected' ? 'Brak oceny jakości' : qualityLabel(selected.outcome) }}</span></div></div>
          <div v-if="selected.status === 'Rejected'" class="rejection-explanation"><strong>Qraft odrzucił komunikat</strong><p>Najpierw popraw dane wejściowe. Ten wynik nie określa jakości wyprodukowanego materiału.</p></div>
          <div v-if="selected.errors.length" class="validation-errors"><h3>Błędy walidacji <span>{{ selected.errors.length }}</span></h3><ul><li v-for="(error, index) in selected.errors" :key="`${error.code}-${error.field}-${index}`"><strong>{{ error.message }}</strong><code>{{ error.field }} · {{ error.code }}</code></li></ul></div>
          <div v-for="sample in selected.samples" :key="sample.code" class="sample-block"><div class="sample-heading"><h3>Próbka {{ sample.code }}</h3><span :class="sample.isConformant ? 'text-good' : 'text-bad'">{{ sample.isConformant ? 'Zgodna' : 'Niezgodna' }}</span></div><dl class="measurements"><div v-for="criterion in sample.criteria" :key="criterion.criterion" :class="{ failed: !criterion.isConformant }"><dt>{{ criterionName(criterion.criterion) }}<small>{{ limits(criterion) }}</small></dt><dd><strong>{{ measurement(criterion) }}</strong><span :class="criterion.isConformant ? 'text-good' : 'text-bad'">{{ criterion.isConformant ? 'Zgodny' : 'Niezgodny' }}</span></dd></div></dl></div>
          <div class="message-identity"><span>Komunikat źródłowy</span><code>{{ selected.messageId }}</code><span>Przetworzono {{ formatDate(selected.processedAt) }}</span></div>
          <details class="json-view"><summary>Pełna odpowiedź JSON</summary><pre>{{ JSON.stringify(selected, null, 2) }}</pre></details>
        </template>
      </section>
    </div>

    <section id="runs" class="runs panel" aria-labelledby="runs-title">
      <div class="runs-header"><div><h2 id="runs-title">Przebiegi symulatora</h2><p class="muted">Publikacja, odpowiedzi Qraft i ponowne wysłanie tego samego komunikatu.</p></div><span class="count">{{ runs.length }}</span></div>
      <p v-if="simulatorError" class="inline-error" role="alert">{{ simulatorError }}<span v-if="runs.length"> Widoczne są ostatnio odczytane przebiegi.</span></p>
      <div v-if="!runs.length && !initialLoading && !simulatorError" class="empty-state runs-empty"><p>Nie ma jeszcze symulacji. Wybierz scenariusz w generatorze i uruchom pierwszy przebieg.</p></div>
      <p v-if="initialLoading" class="muted runs-empty">Wczytywanie przebiegów…</p>
      <div v-for="run in runs" :key="run.id" class="run">
        <button class="run-heading" type="button" :aria-expanded="expandedRun === run.id" @click="expandedRun = expandedRun === run.id ? null : run.id"><span class="run-expand" aria-hidden="true">{{ expandedRun === run.id ? '−' : '+' }}</span><span class="run-name"><strong>{{ scenarios.find((item) => item.code === run.scenario)?.name ?? run.scenario }}</strong><small>{{ formatDate(run.createdAt) }} · {{ shortId(run.id) }}</small></span><span class="run-progress"><strong>{{ run.stats.processed + run.stats.rejected }} / {{ run.stats.total }}</strong><span>wyników</span></span><span class="badge" :class="run.stats.processed + run.stats.rejected === run.stats.total ? 'conformant' : 'pending'">{{ runLabel(run) }}</span></button>
        <div v-if="expandedRun === run.id" class="run-body"><div class="run-stats"><span>Publikacja: {{ run.stats.published }}/{{ run.stats.total }}</span><span>Oczekuje na publikację: {{ run.stats.pending }}</span><span>Jakość niezgodna: {{ run.stats.nonconformant }}</span><span>Odrzucone komunikaty: {{ run.stats.rejected }}</span></div><div v-for="record in run.records" :key="record.messageId" class="run-record"><div class="record-main"><div><strong>{{ record.batchCode }}</strong><code>{{ record.messageId }}</code></div><div class="record-state"><span>{{ recordLabel(record) }}</span><small v-if="record.outcome">Jakość: {{ qualityLabel(record.outcome).toLocaleLowerCase('pl') }}</small><small>Powtórzenia: {{ record.replayCount }}</small></div><button class="button secondary replay-button" type="button" :disabled="replaying.has(record.messageId) || record.publishStatus === 'Pending'" @click="replay(record.messageId)">{{ replaying.has(record.messageId) ? 'Zlecanie…' : 'Wyślij ponownie' }}</button></div><p v-if="record.lastPublishError" class="field-error">{{ record.lastPublishError }}</p><details class="source-json"><summary>Komunikat źródłowy JSON</summary><pre>{{ record.payloadJson }}</pre></details></div></div>
      </div>
    </section>
    <footer>Qraft Lab <span>Środowisko do nauki przetwarzania pomiarów i komunikacji przez kolejkę.</span></footer>
  </main>
</template>
