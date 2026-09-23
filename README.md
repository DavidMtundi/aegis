# Aegis Financial Crime Compliance Platform

> **Aegis is a policy engine, not a hardcoded AML application.**

Aegis is a multi-tenant financial-crime compliance platform for banks, SACCOs, fintechs and other financial institutions.

## What this repository contains

This is the core Aegis platform — a modular monolith that will eventually decompose into services as scale requires. The marketing landing page lives separately in `aegis-web`.

## Quick start

```bash
# Prerequisites: .NET 9 SDK, Docker, PostgreSQL
dotnet restore
docker-compose up -d   # starts PostgreSQL + Redis
dotnet ef database update --project src/Aegis.Infrastructure --startup-project src/Aegis.Api
# Jwt signing key is not committed; launchSettings sets Jwt__SigningKey for local Development.
dotnet run --project src/Aegis.Api
```

### Vertical-slice E2E tests

With compose Postgres running (default `localhost:5432`, user/password `aegis` / `aegis_dev_password`):

```bash
dotnet ef database update --project src/Aegis.Infrastructure --startup-project src/Aegis.Api
dotnet test tests/EndToEnd/Aegis.Tests.EndToEnd.csproj
```

Optional: point at another DB with `AEGIS_TEST_CONNECTION` (tests auto-create `aegis_test` when using the default).

Scenarios cover structuring positive/negative/boundary, ingest idempotency, cross-tenant isolation, and validation.

## Repository layout

```
aegis/
├── src/
│   ├── Aegis.Api/                  # ASP.NET Core host, controllers, middleware
│   ├── Aegis.Modules.Identity/     # Authentication, users, roles, permissions
│   ├── Aegis.Modules.Kyc/          # Know Your Customer
│   ├── Aegis.Modules.Kyb/          # Know Your Business / UBO
│   ├── Aegis.Modules.Screening/    # Sanctions, PEP, adverse media
│   ├── Aegis.Modules.Risk/         # Customer and entity risk scoring
│   ├── Aegis.Modules.Transactions/ # Ingestion, normalization, validation
│   ├── Aegis.Modules.Features/     # Feature / behaviour engine
│   ├── Aegis.Modules.Aml/          # AML rules engine, scenarios, executions
│   ├── Aegis.Modules.Alerts/       # Alert generation, correlation, queue
│   ├── Aegis.Modules.Cases/        # Case management, investigations
│   ├── Aegis.Modules.Network/      # Graph / relationship analysis
│   ├── Aegis.Modules.Reporting/    # Compliance and audit reports
│   ├── Aegis.Modules.Audit/        # Append-only audit log
│   ├── Aegis.Infrastructure/       # EF Core, repositories, messaging, storage
│   └── Aegis.Shared/               # Domain primitives, contracts, events
│
├── tests/
│   ├── Unit/                       # Pure unit tests per module
│   ├── Integration/                # Repository and service integration tests
│   ├── Architecture/               # NetArchTest module boundary rules
│   ├── Contract/                   # API contract tests
│   └── EndToEnd/                   # Full pipeline scenario tests
│
├── docs/
│   ├── architecture/               # Architecture diagrams and context
│   ├── decisions/                  # Architecture Decision Records (ADRs)
│   ├── aml/                        # AML scenario specifications
│   ├── api/                        # OpenAPI / API documentation
│   └── runbooks/                   # Operational runbooks
│
├── migrations/                     # EF Core database migrations
├── docker-compose.yml
├── .github/
│   └── workflows/
│       └── ci.yml
└── README.md
```

## Architecture principles

1. **AML rules are never hardcoded.** Thresholds live in rule configurations, managed by compliance officers.
2. **Every alert is explainable.** Evidence is stored alongside every detection signal.
3. **Every compliance decision is auditable.** WHO, WHAT, WHEN, WHY, BEFORE, AFTER.
4. **Tenant isolation is enforced at the data-access boundary.** Not in UI logic.
5. **Modules communicate through contracts.** No arbitrary cross-module DB queries.
6. **AI proposes. Compliance approves. Aegis enforces.**

See `docs/decisions/` for Architecture Decision Records and `docs/aml/` for AML scenario specifications.

## Engineering handoff

See `aegis_engineering_handoff.md` in the project artifact directory for the full principal architecture and engineering handoff document.
