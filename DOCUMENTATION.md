# Merchant Reliability Platform (MRP)

## Overview

MRP is an AI-powered transaction monitoring, reconciliation, and recovery system for **Paynow Zimbabwe**. It automates merchant onboarding, detects payment anomalies across three data sources (Paynow, merchant records, bank settlements), and recovers failed transactions using intelligent background agents.

---

## Architecture

**Pattern:** Clean Architecture with Domain-Driven Design

```
MRP.Domain              Core business entities, enums, interfaces, events
MRP.Application         DTOs and application-level contracts
MRP.Infrastructure      EF Core persistence, Paynow SDK wrapper, MediatR event bus
MRP.Agents              3 background services (Onboarding, Intelligence, Recovery)
MRP.Api                 REST API with 6 controllers and 15+ endpoints
MRP.Dashboard           Blazor Server UI with real-time monitoring
MRP.Shared              Shared utilities and constants
```

**Tech Stack:** .NET 10, PostgreSQL 16, ASP.NET Core, Blazor Server, MediatR, Entity Framework Core, Docker

---

## Domain Model

### Entities (9)

| Entity | Purpose |
|---|---|
| **Merchant** | Core merchant profile with reliability score (0-100), tier, activity tracking |
| **MerchantIntegration** | Paynow credentials, callback URLs, health metrics, failure counters |
| **Transaction** | Payment records from Paynow, merchant, or bank sources |
| **TransactionMatch** | Links transactions across sources for reconciliation |
| **Anomaly** | Detected issues (missing records, amount mismatches, etc.) with severity |
| **ReconciliationReport** | Period-based reports with match counts and volumes |
| **RecoveryAttempt** | Tracks resolution attempts with strategy and outcome |
| **AgentTask** | Background task queue with priority scheduling |
| **AgentResult** | Agent execution results and performance metrics |

### Enums (8)

| Enum | Values |
|---|---|
| **TransactionStatus** | Pending, Paid, AwaitingDelivery, Delivered, Failed, Cancelled, Refunded, Disputed |
| **SourceType** | Paynow, Merchant, Bank |
| **PaymentMethod** | EcoCash, OneMoney, Telecash, InnBucks, BankTransfer, BankCard, ZimSwitch |
| **MerchantTier** | Standard, Professional, Enterprise |
| **AnomalyType** | MissingPaynowRecord, MissingMerchantRecord, MissingBankRecord, StatusMismatch, AmountDiscrepancy, DuplicateTransaction, SettlementDelay, VelocityAnomaly, CallbackFailure |
| **RecoveryStrategy** | AutoRetry, MerchantNotification, ManualEscalation, PaynowDispute |
| **AgentType** | Onboarding, TransactionIntelligence, Recovery |
| **AgentState** | Stopped, Starting, Running, Paused, Stopping, Faulted |

### Domain Events (6)

| Event | Published When |
|---|---|
| `TransactionIngested` | Transaction enters the system via webhook or API |
| `AnomalyDetected` | Reconciliation finds an issue |
| `ReconciliationCompleted` | Reconciliation run finishes |
| `RecoveryInitiated` | Recovery process begins for an anomaly |
| `RecoveryCompleted` | Recovery attempt finishes |
| `MerchantOnboarded` | Merchant integration validation completes |

---

## Interfaces

### Repository Contracts

| Interface | Key Methods |
|---|---|
| `IMerchantRepository` | GetByIdAsync, GetAllAsync (paginated), AddAsync, UpdateAsync |
| `ITransactionRepository` | GetByMerchantAsync (date range), GetPendingAsync, AddAsync, AddRangeAsync |
| `IReconciliationRepository` | GetByMerchantAsync, AddAsync |
| `IRecoveryRepository` | GetUnresolvedAnomaliesAsync, AddAttemptAsync, GetAttemptsByAnomalyAsync |
| `IAgentTaskRepository` | DequeueAsync (by type + priority), AddAsync, UpdateAsync, SaveResultAsync |

### Service Contracts

| Interface | Purpose |
|---|---|
| `IAgent` | Agent contract: Name, Type, ExecuteAsync, HealthCheckAsync |
| `IPaynowGateway` | PollTransactionAsync, InitiatePaymentAsync, InitiateMobilePaymentAsync |
| `IEventBus` | PublishAsync for domain event dispatch |

---

## Infrastructure

### Database

- **Provider:** PostgreSQL 16 via Npgsql
- **ORM:** Entity Framework Core with FluentAPI configurations
- **Schema:** `mrp`
- **Tables:** merchants, merchant_integrations, transactions, transaction_matches, anomalies, reconciliation_reports, recovery_attempts, agent_tasks, agent_results

**Key Indexes:**
- `transactions`: by PaynowReference, MerchantReference, (MerchantId, CreatedAt), (Source, Status)
- `anomalies`: by (IsResolved, Severity)
- `agent_tasks`: by (AgentType, Status, Priority)

### Paynow Gateway

Wraps the `Webdev.Payments.Paynow` SDK (v1.2.0):
- `PollTransactionAsync` — checks payment status via `WasPaid` flag
- `InitiatePaymentAsync` — browser-based payments with email
- `InitiateMobilePaymentAsync` — mobile money (EcoCash, OneMoney, InnBucks, Telecash)
- Constructor: `Paynow(integrationId, integrationKey, resultUrl)`

### Event Bus

MediatR-based in-process pub/sub. Events are wrapped in `EventNotification<T> : INotification` for dispatch.

---

## Agents

Three background services polling a task queue at configurable intervals.

### 1. Onboarding Agent (every 30 min)

Validates merchant Paynow integrations:

1. Checks credentials exist (Integration ID + Key)
2. Validates callback URLs (HTTPS, reachable via HTTP GET with 10s timeout)
3. Performs test payment initiation
4. Calculates health score:
   - Start at 100
   - Missing credentials: -15 each
   - Unreachable callback: -20
   - Failed payment test: -25
   - Clamped to 0-100
5. Updates `merchant.ReliabilityScore` and publishes `MerchantOnboarded`

### 2. Transaction Intelligence Agent (every 15 min)

Core reconciliation engine:

1. Fetches transactions from all 3 sources for merchant/period
2. **ReconciliationEngine** matches by reference across sources:
   - Missing Paynow record → High severity
   - Missing Merchant record → Medium severity
   - Missing Bank record (>48h from paid) → High severity
   - Amount discrepancy >$10 → High, else Medium
   - Status mismatch → Medium
   - Settlement delay >48h → Medium
3. **AnomalyDetector** runs additional checks:
   - Velocity: >10 txns/min or >100 txns/hour
   - Duplicates: same ref + amount within 5 minutes
4. Persists `ReconciliationReport` with matches and anomalies
5. Publishes `ReconciliationCompleted` and per-anomaly `AnomalyDetected`

### 3. Recovery Agent (every 5 min)

Resolves unresolved anomalies (max 3 attempts):

| Anomaly Type | Strategy |
|---|---|
| Missing Paynow/Merchant Record | AutoRetry (re-poll Paynow) |
| Callback Failure | AutoRetry |
| Status Mismatch | MerchantNotification |
| Amount Discrepancy | ManualEscalation |
| Missing Bank Record (>48h) | PaynowDispute |
| Settlement Delay | PaynowDispute |
| Default | MerchantNotification |

Publishes `RecoveryInitiated` and `RecoveryCompleted` events.

---

## API Endpoints

### Dashboard (`/api/dashboard`)

| Method | Path | Description |
|---|---|---|
| GET | `/metrics` | System KPIs: reliability score, transaction counts, recovery rate, active merchants |
| GET | `/agents/status` | Per-agent: pending tasks, completed count, success rate, last run |

### Merchants (`/api/merchants`)

| Method | Path | Description |
|---|---|---|
| GET | `/` | List merchants (paginated: `?page=1&pageSize=20`) |
| GET | `/{id}` | Get merchant details with integration |
| POST | `/` | Create merchant + queue onboarding validation |
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
| GET | `/reports/{id}` | Get report with matches and anomalies |
| POST | `/trigger` | Queue reconciliation (default: last 24h) |

### Recovery (`/api/recovery`)

| Method | Path | Description |
|---|---|---|
| GET | `/queue` | Unresolved anomalies (ordered by severity + date) |
| GET | `/attempts/{anomalyId}` | Recovery attempts for an anomaly |
| GET | `/stats` | Queue statistics by severity |
| POST | `/initiate` | Queue recovery for an anomaly |

### Webhooks (`/api/webhooks`)

| Method | Path | Description |
|---|---|---|
| POST | `/paynow` | Paynow callback (form data: reference, amount, status, pollurl, paynowreference, hash) |

---

## Dashboard (Blazor Server)

### Pages

- **Overview** — Hero metrics (reliability %, volume, merchants, anomalies), transaction volume chart, recent anomalies list, reliability gauge (SVG), recovery timeline

### Components

- **MainLayout** — App shell with sidebar navigation (Overview, Merchants, Transactions, Reconciliation, Recovery, Agents, Settings)
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

## Core Workflows

### Merchant Onboarding
```
POST /api/merchants → Create Merchant + Integration
  → Queue AgentTask (Onboarding)
  → OnboardingAgent validates credentials, callback, test payment
  → Update ReliabilityScore
  → Publish MerchantOnboarded
```

### Transaction Reconciliation
```
POST /api/webhooks/paynow → Create Transaction (Source: Paynow)
  → Publish TransactionIngested
  → TransactionIntelligenceAgent (periodic)
    → Fetch all sources for merchant/period
    → ReconciliationEngine.Reconcile()
    → AnomalyDetector (velocity + duplicates)
    → Persist ReconciliationReport
    → Publish AnomalyDetected per issue
```

### Anomaly Recovery
```
RecoveryAgent polls unresolved anomalies
  → Determine strategy by type
  → Execute (retry / notify / escalate / dispute)
  → Persist RecoveryAttempt
  → Publish RecoveryCompleted
  → Max 3 retries per anomaly
```

---

## Project Statistics

| Metric | Count |
|---|---|
| Projects | 9 (7 src + 2 test) |
| Entities | 9 |
| Enums | 8 |
| Interfaces | 8 |
| Controllers | 6 |
| API Endpoints | 15+ |
| Background Agents | 3 |
| Repositories | 5 |
| DTOs | 12 |
| Domain Events | 6 |
| Unit Test Classes | 2 |
| Unit Tests | 10 |
| Payment Methods | 7 |
| Anomaly Types | 9 |
| Recovery Strategies | 4 |
