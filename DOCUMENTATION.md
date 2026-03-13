# Merchant Reliability Platform (MRP)

## Overview

MRP is an event-driven payment reliability platform for **Paynow Zimbabwe**. It monitors merchant integrations, reconciles transactions across Paynow, merchants, and banks, predicts settlement risks, detects anomalies, and automatically recovers failed payments through 3 service pipelines orchestrated by domain events.

---

## Architecture

**Pattern:** Clean Architecture with Domain-Driven Design, Event-Driven Pipelines

```
MRP.Domain              Core business entities, enums, interfaces, events
MRP.Application         DTOs and application-level contracts
MRP.Infrastructure      EF Core persistence, Paynow SDK wrapper, MediatR event bus
MRP.Agents              3 pipeline services (Ingestion, Intelligence, Recovery)
                        + MediatR event handlers for pipeline coordination
MRP.Api                 REST API with 7 controllers and 25+ endpoints
MRP.Dashboard           Blazor Server UI with real-time monitoring
MRP.Shared              Shared utilities and constants
```

**Tech Stack:** .NET 10, PostgreSQL 16, ASP.NET Core, Blazor Server, MediatR, Entity Framework Core, Docker

### Design Principles

- **No polling agents** — all work is triggered by domain events or API calls
- **Synchronous pipelines** — controllers call services directly, no task queue
- **MediatR pub/sub** — events flow between pipelines via `EventNotification<T> : INotification`
- **Scoped services** — all 3 pipelines are registered as scoped DI services
- **Async recovery decoupling** — anomaly recovery is decoupled from MediatR via a bounded `Channel<T>`, drained by a `BackgroundService` worker
- **Resilience-first** — Polly retry + circuit breaker on all external (Paynow) calls
- **Dynamic strategy learning** — recovery strategies ranked by historical success rates with feedback loops
- **Backpressure-aware** — bounded channels with configurable capacity and drop-oldest overflow policy

---

## Domain Model

### Entities (8)

| Entity | Purpose |
|---|---|
| **Merchant** | Core merchant profile with reliability score (0-100), tier, activity tracking |
| **MerchantIntegration** | Paynow credentials, callback URLs, health metrics, failure counters |
| **Transaction** | Payment records from Paynow, merchant, or bank sources |
| **Anomaly** | Detected issues (missing records, amount mismatches, etc.) with severity, linked to merchant + transaction + report |
| **ReconciliationReport** | Period-based reports with match counts, volumes, and anomaly collection |
| **RecoveryAttempt** | Tracks resolution attempts with strategy, outcome, confidence score, and decision reason |
| **Settlement** | Per-transaction settlement risk prediction with confidence, predicted time, and risk factors |
| **MerchantProfile** | Merchant traffic patterns, retry/duplicate/callback rates, behaviour risk score |

### Enums (6)

| Enum | Values |
|---|---|
| **TransactionStatus** | Pending, Paid, AwaitingDelivery, Delivered, Failed, Cancelled, Refunded, Disputed |
| **SourceType** | Paynow, Merchant, Bank |
| **PaymentMethod** | EcoCash, OneMoney, Telecash, InnBucks, BankTransfer, BankCard, ZimSwitch |
| **MerchantTier** | Standard, Professional, Enterprise |
| **AnomalyType** | MissingPaynowRecord, MissingMerchantRecord, MissingBankRecord, StatusMismatch, AmountDiscrepancy, DuplicateTransaction, SettlementDelay, VelocityAnomaly, CallbackFailure |
| **RecoveryStrategy** | AutoRetry, MerchantNotification, ManualEscalation, PaynowDispute, RecordReconstruction, BankVerification |

### Domain Events (9)

| Event | Published When |
|---|---|
| `TransactionReceived` | Transaction enters the system via webhook or batch import |
| `BankSettlementReceived` | Bank settlement data is ingested |
| `MerchantCreated` | Merchant integration validation completes |
| `AnomalyDetected` | Reconciliation or behaviour analysis finds an issue |
| `ReconciliationCompleted` | Reconciliation run finishes |
| `MerchantRiskUpdated` | Merchant behaviour risk score changes |
| `SettlementRiskDetected` | Settlement prediction identifies high-risk transaction (>=70%) |
| `RecoveryCompleted` | Recovery attempt finishes (success or failure) |

---

## Pipeline Services

### 1. Ingestion Service (`IIngestionService`)

Handles all data entry into the platform.

**Methods:**
- `IngestPaynowWebhookAsync` — Processes Paynow payment callbacks, creates transactions, publishes `TransactionReceived`
- `IngestMerchantBatchAsync` — Batch imports merchant-reported transactions, publishes `TransactionReceived` per transaction
- `ValidateMerchantIntegrationAsync` — Validates merchant Paynow integration:
  1. Checks credentials exist (Integration ID + Key)
  2. Validates callback URLs (HTTPS, reachable via HTTP GET with 10s timeout)
  3. Performs test payment initiation
  4. Calculates health score (start 100, -15 per issue, -20 unreachable callback, -25 failed payment, clamped 0-100)
  5. Updates `merchant.ReliabilityScore` and publishes `MerchantCreated`

### 2. Intelligence Engine (`IIntelligenceEngine`)

Consolidates reconciliation, behaviour analysis, and settlement prediction.

**Methods:**

#### `ReconcileBatchAsync(merchantIds, periodStart, periodEnd, maxParallelism)`
Runs reconciliation in parallel across multiple merchants with bounded concurrency:
- Uses `SemaphoreSlim` to limit parallel operations (clamped 1–10)
- Error isolation: one merchant failure doesn't abort the batch
- Returns only successful reports (failed merchants logged and skipped)

#### `ReconcileAsync(merchantId, periodStart, periodEnd)`
1. Fetches transactions from all 3 sources for merchant/period
2. **ReconciliationEngine** matches by reference across sources:
   - Missing Paynow record -> High severity
   - Missing Merchant record -> Medium severity
   - Missing Bank record (>48h from paid) -> High severity
   - Amount discrepancy >$10 -> High, else Medium
   - Status mismatch -> Medium
   - Settlement delay >48h -> Medium
3. **AnomalyDetector** runs additional checks:
   - Velocity: >10 txns/min or >100 txns/hour
   - Duplicates: same ref + amount within 5 minutes
4. Persists `ReconciliationReport` with anomalies
5. Publishes `AnomalyDetected` per issue and `ReconciliationCompleted`

#### `AnalyseMerchantBehaviourAsync(merchantId)`
Analyses last hour of merchant transactions against 24h baseline:

| Alert Type | Threshold | Description |
|---|---|---|
| VelocitySpike | 3x normal traffic | Current tx/hr exceeds 3x the 24h average |
| HighRetryRate | >15% | Too many retried payment references |
| DuplicateTransactions | >5% | Suspected duplicate payment submissions |
| CallbackInstability | >10% | Callback endpoint failure rate |

**Risk score components:** Velocity spike (up to 35), Retry rate (up to 25), Duplicate rate (up to 20), Callback failure (up to 20). Clamped 0-100.

**Reliability impact:** When behaviour risk > 70, merchant reliability is reduced by up to 5 points.

Upserts `MerchantProfile` and publishes `MerchantRiskUpdated`.

#### `PredictSettlementRiskAsync(transactionId)`
Predicts settlement risk for paid-but-unsettled transactions.

**Risk calculation:**
- Merchant reliability inversely weighted (30%)
- Payment method risk (EcoCash: 5, BankTransfer: 20)
- Overdue penalty: +3 per hour past expected settlement time (max 30)
- Weekend: +10, off-hours: +5
- High-value (>$500): +8, (>$1000): +15
- Clamped to 0-100

**Settlement time prediction:**
| Payment Method | Base Hours |
|---|---|
| EcoCash | 4h |
| OneMoney | 6h |
| Telecash | 8h |
| InnBucks | 6h |
| BankTransfer | 24h |
| BankCard | 12h |
| ZimSwitch | 24h |

Adjusted by reliability factor: `baseHours * (1 + (100 - reliability) / 200)`

**Confidence scoring:** Base: 50 + (reliability * 0.4), mobile money bonus: +6 to +10, clamped 0-100.

Persists `Settlement` and publishes `SettlementRiskDetected` for risk >= 70%.

### 3. Recovery Engine (`IRecoveryEngine`)

Decides and executes recovery strategies for anomalies in a single pass.

**Method:** `RecoverAsync(anomalyId)`

**Strategy selection** (by anomaly type, with fallback escalation):

| Anomaly Type | Primary Strategy | Fallback |
|---|---|---|
| MissingPaynowRecord | PaynowDispute | RecordReconstruction |
| MissingMerchantRecord | MerchantNotification | RecordReconstruction |
| MissingBankRecord | BankVerification | PaynowDispute |
| StatusMismatch | AutoRetry | MerchantNotification |
| AmountDiscrepancy | PaynowDispute | ManualEscalation |
| DuplicateTransaction | AutoRetry | MerchantNotification |
| SettlementDelay | BankVerification | PaynowDispute |
| VelocityAnomaly | MerchantNotification | ManualEscalation |
| CallbackFailure | AutoRetry | MerchantNotification |

**Intelligence features:**
- **Dynamic strategy ranking** — queries last 90 days of recovery attempts grouped by strategy; reorders available strategies by historical success rate when ≥3 samples exist
- Filters out strategies that already failed for the anomaly
- Falls back to `ManualEscalation` when all automated strategies are exhausted
- Confidence scoring based on merchant reliability, anomaly severity, attempt count, and historical success rate (±10 points)
- Decision reason logged with full context, includes historical rate when available

On success, marks anomaly as resolved. Publishes `RecoveryCompleted`.

### Recovery Channel & Worker

Recovery is **fully decoupled** from MediatR event handling via a bounded `System.Threading.Channels.Channel<T>`:

- **`RecoveryChannel`** — singleton bounded channel (capacity 500, `DropOldest` overflow, single-reader/multi-writer)
- **`RecoveryWorker`** — `BackgroundService` that drains the channel with `SemaphoreSlim`-bounded concurrency (max 3 parallel recoveries)
- **`AnomalyDetectedHandler`** writes to the channel instead of calling `RecoveryEngine` directly — prevents blocking the MediatR dispatch thread

```
AnomalyDetected event
  -> AnomalyDetectedHandler (MediatR)
  -> RecoveryChannel.Writer.TryWrite(workItem)  [non-blocking]
     |
     v (background)
RecoveryWorker reads from channel
  -> RecoveryEngine.RecoverAsync()  [up to 3 concurrent]
```

Only **high** and **critical** severity anomalies are auto-enqueued. If the channel is full, the item is dropped with a warning log.

---

## Event-Driven Flow

### MediatR Event Handlers

| Handler | Trigger | Action |
|---|---|---|
| `TransactionReceivedHandler` | `TransactionReceived` | Triggers settlement risk prediction + merchant behaviour analysis (fire-and-forget in new scope) |
| `AnomalyDetectedHandler` | `AnomalyDetected` | Enqueues high/critical anomalies into `RecoveryChannel` (non-blocking) |

### Pipeline Coordination

```
POST /api/webhooks/paynow
  -> IngestionService.IngestPaynowWebhookAsync()
  -> Publishes TransactionReceived
     |
     v
TransactionReceivedHandler (MediatR)
  -> IntelligenceEngine.PredictSettlementRiskAsync()
  -> IntelligenceEngine.AnalyseMerchantBehaviourAsync()
     |
     v (if anomalies found during reconciliation)
AnomalyDetectedHandler (MediatR)
  -> RecoveryChannel.Writer.TryWrite(workItem) [non-blocking, high/critical only]
     |
     v (background worker)
RecoveryWorker drains channel
  -> RecoveryEngine.RecoverAsync() [up to 3 concurrent]
  -> Publishes RecoveryCompleted
```

### Manual Triggers

```
POST /api/reconciliation/trigger
  -> IntelligenceEngine.ReconcileAsync() [synchronous, returns report]

POST /api/reconciliation/trigger/batch
  -> IntelligenceEngine.ReconcileBatchAsync() [parallel, bounded concurrency]

POST /api/recovery/initiate
  -> RecoveryEngine.RecoverAsync() [synchronous, returns attempt]

POST /api/intelligence/behaviour/analyse
  -> IntelligenceEngine.AnalyseMerchantBehaviourAsync()
```

---

## Interfaces

### Repository Contracts

| Interface | Key Methods |
|---|---|
| `IMerchantRepository` | GetByIdAsync, GetAllAsync (paginated), AddAsync, UpdateAsync |
| `ITransactionRepository` | GetByMerchantAsync (date range), GetPendingAsync, AddAsync, AddRangeAsync |
| `IReconciliationRepository` | GetByMerchantAsync, GetByIdAsync, AddAsync |
| `IRecoveryRepository` | GetUnresolvedAnomaliesAsync, GetAnomalyByIdAsync, AddAttemptAsync, GetAttemptsByAnomalyAsync, AddAnomalyAsync, UpdateAnomalyAsync, GetStrategySuccessRatesAsync |
| `ISettlementRepository` | AddAsync, GetByMerchantAsync, GetHighRiskAsync, UpdateAsync |
| `IMerchantProfileRepository` | GetByMerchantAsync, AddAsync, UpdateAsync, GetHighRiskAsync |

### Service Contracts

| Interface | Purpose |
|---|---|
| `IIngestionService` | Transaction ingestion and merchant onboarding validation |
| `IIntelligenceEngine` | Reconciliation (single + batch parallel), behaviour analysis, settlement prediction |
| `IRecoveryEngine` | Anomaly recovery with dynamic strategy selection based on historical success rates |
| `IPaynowGateway` | PollTransactionAsync, InitiatePaymentAsync, InitiateMobilePaymentAsync |
| `IEventBus` | PublishAsync for domain event dispatch |

---

## Infrastructure

### Database

- **Provider:** PostgreSQL 16 via Npgsql
- **ORM:** Entity Framework Core with FluentAPI configurations
- **Schema:** `mrp`
- **Tables:** merchants, merchant_integrations, transactions, anomalies, reconciliation_reports, recovery_attempts, settlements, merchant_profiles

**Key Indexes:**
- `transactions`: by PaynowReference, MerchantReference, (MerchantId, CreatedAt), (Source, Status)
- `anomalies`: by (IsResolved, Severity), MerchantId
- `settlements`: by (MerchantId, CreatedAt), RiskScore
- `merchant_profiles`: by MerchantId (unique), BehaviourRiskScore

### Paynow Gateway

Wraps the `Webdev.Payments.Paynow` SDK (v1.2.0):
- `PollTransactionAsync` — checks payment status via `WasPaid` flag
- `InitiatePaymentAsync` — browser-based payments with email
- `InitiateMobilePaymentAsync` — mobile money (EcoCash, OneMoney, InnBucks, Telecash)
- Constructor: `Paynow(integrationId, integrationKey, resultUrl)`

**Resilience (Polly):**
All three gateway methods are wrapped in a combined retry + circuit breaker pipeline:

| Policy | Configuration | Details |
|---|---|---|
| **Retry** | 3 attempts, exponential backoff | 1s → 2s → 4s delays, logs each retry with attempt number |
| **Circuit Breaker** | 5 failures → open 60s | Opens after 5 consecutive failures in 30s window, half-open probe after 60s |

- `InvalidOperationException` is excluded from both policies (business logic errors should not trigger retries)
- Pipeline order: retry wraps circuit breaker (retry → CB → call), so retries respect the open circuit

### Event Bus

MediatR-based in-process pub/sub. Events are wrapped in `EventNotification<T> : INotification` for dispatch. Handlers run in background tasks (via `Task.Run` with new DI scopes) to avoid blocking the MediatR dispatch thread and circular publish loops.

---

## API Endpoints

### Dashboard (`/api/dashboard`)

| Method | Path | Description |
|---|---|---|
| GET | `/metrics` | System KPIs: reliability score, transaction counts, recovery rate, active merchants |
| GET | `/pipelines/status` | Pipeline health: unresolved anomalies, high-risk settlements/merchants, recovery success rate |

### Merchants (`/api/merchants`)

| Method | Path | Description |
|---|---|---|
| GET | `/` | List merchants (paginated: `?page=1&pageSize=20`) |
| GET | `/{id}` | Get merchant details with integration |
| POST | `/` | Create merchant + trigger onboarding validation |
| GET | `/{id}/health` | Merchant health summary with callback reliability |

### Transactions (`/api/transactions`)

| Method | Path | Description |
|---|---|---|
| GET | `/` | Query by merchant + date range (default: last 30 days) |
| GET | `/{id}` | Get transaction details |
| POST | `/ingest` | Batch ingest from merchant source |

### Reconciliation (`/api/reconciliation`)

| Method | Path | Description |
|---|---|---|
| GET | `/reports?merchantId=X` | Get reports for merchant |
| GET | `/reports/{id}` | Get report with anomalies |
| POST | `/trigger` | Run reconciliation synchronously (default: last 24h), returns report |
| POST | `/trigger/batch` | Run parallel reconciliation across multiple merchants (configurable `maxParallelism`, default 4) |

### Recovery (`/api/recovery`)

| Method | Path | Description |
|---|---|---|
| GET | `/queue` | Unresolved anomalies (ordered by severity + date) |
| GET | `/attempts/{anomalyId}` | Recovery attempts with confidence scores and decision reasons |
| GET | `/stats` | Queue statistics by severity |
| POST | `/initiate` | Execute recovery synchronously, returns attempt result |

### Intelligence (`/api/intelligence`)

| Method | Path | Description |
|---|---|---|
| GET | `/settlement/predictions` | List settlement predictions (filterable by `?merchantId=X`) |
| GET | `/settlement/high-risk` | High-risk unsettled transactions (filterable by `?threshold=70`) |
| GET | `/settlement/summary` | Risk summary: total predictions, high-risk count, avg risk, prediction accuracy |
| POST | `/settlement/analyse` | Trigger settlement analysis for a merchant |
| GET | `/behaviour/{merchantId}` | Merchant behaviour profile with traffic patterns and active alerts |
| GET | `/behaviour/high-risk` | High-risk merchant profiles (filterable by `?threshold=50`) |
| POST | `/behaviour/analyse` | Trigger behaviour analysis for a merchant |
| GET | `/recovery/stats` | Recovery success rate, avg confidence |
| POST | `/recovery/decide` | Execute intelligent recovery, returns attempt with decision |

### Webhooks (`/api/webhooks`)

| Method | Path | Description |
|---|---|---|
| POST | `/paynow` | Paynow callback (form data: reference, amount, status, pollurl, paynowreference, hash, merchantId) |

---

## Dashboard (Blazor Server)

### Pages

- **Overview** — Hero metrics (reliability %, volume, merchants, anomalies), transaction volume chart, recent anomalies list, reliability gauge (SVG), recovery timeline

### Components

- **MainLayout** — App shell with sidebar navigation (Overview, Merchants, Transactions, Reconciliation, Recovery, Intelligence, Settings)
- **MetricCard** — Reusable KPI card with trend indicator

---

## Testing

### Unit Tests (`tests/MRP.Tests.Unit`)

**AnomalyDetectorTests:**
- Velocity spike detection (>10/min threshold)
- Normal traffic passes clean
- Duplicate detection within 5-minute window
- Different amounts with same reference not flagged

**ReconciliationEngineTests:**
- Full 3-source match returns balanced report
- Missing merchant record detected
- Amount discrepancy detected
- Status mismatch detected
- Empty transaction set returns empty report
- Multi-transaction match rate calculation

### Test Fixtures

`TransactionFixtures` provides factory methods:
- `CreatePaynow(ref, amount, status, method, merchantId, createdAt)`
- `CreateMerchant(ref, amount, status, merchantId)`
- `CreateBank(ref, amount, merchantId, settledAt)`
- `CreateBatch(source, count, merchantId, avgAmount)`

### Integration Tests (`tests/MRP.Tests.Integration`)

Placeholder for API integration tests using:
- `Microsoft.AspNetCore.Mvc.Testing`
- `Testcontainers.PostgreSql`

---

## Deployment

### Docker Compose

3 services on `mrp-net` bridge network:

| Service | Image | Port | Depends On |
|---|---|---|---|
| postgres | postgres:16-alpine | 5432 | — |
| api | Dockerfile (MRP.Api) | 8080 | postgres (healthy) |
| dashboard | Dockerfile (MRP.Dashboard) | 8081 | api (healthy) |

### Dockerfile

Multi-stage build:
1. **Build:** .NET SDK, restore + publish in Release
2. **Runtime:** .NET ASP.NET runtime (alpine), non-root user `mrp`, health check via wget

### Environment Variables

| Variable | Required | Default |
|---|---|---|
| `DB_PASSWORD` | Yes | `MrpDev2024!` (dev only) |
| `PAYNOW_INTEGRATION_ID` | Yes | — |
| `PAYNOW_INTEGRATION_KEY` | Yes | — |
| `PAYNOW_RESULT_URL` | No | `http://localhost:8080/api/webhooks/paynow` |
| `ASPNETCORE_ENVIRONMENT` | No | `Development` |

### Seed Data

4 demo merchants pre-loaded via `scripts/seed-data.sql`:

| Merchant | Tier | Reliability | Status |
|---|---|---|---|
| Harare Fresh Market | Standard | 92.5% | Active |
| TechZim Solutions | Professional | 78.0% | Active |
| Bulawayo Transport Co | Enterprise | 55.3% | Active |
| Mutare Online Store | Standard | 50.0% | Inactive |

---

## Project Statistics

| Metric | Count |
|---|---|
| Projects | 9 (7 src + 2 test) |
| Entities | 8 |
| Enums | 6 |
| Service Interfaces | 5 |
| Repository Interfaces | 6 |
| Controllers | 7 |
| API Endpoints | 26+ |
| Pipeline Services | 3 |
| Background Workers | 1 (RecoveryWorker) |
| MediatR Handlers | 2 |
| DTOs | 16 |
| Domain Events | 8 |
| Unit Test Classes | 2 |
| Unit Tests | 10 |
| Payment Methods | 7 |
| Anomaly Types | 9 |
| Recovery Strategies | 6 |
