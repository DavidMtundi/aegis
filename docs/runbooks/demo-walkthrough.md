# Aegis supervisor demo walkthrough

**Audience:** product / compliance stakeholder  
**Time:** ~10 minutes  
**Goal:** Prove ingest → configurable detection → explainable alert → case disposition → audit.

## Prerequisites

1. Postgres up (`docker compose up -d` in `aegis`).
2. API in **Development** on `http://127.0.0.1:5092` (restart after pulling so all three scenario seeds load):

   ```bash
   cd aegis
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Aegis.Api
   ```

3. Console:

   ```bash
   cd aegis-console
   cp -n .env.example .env.local
   npm run dev
   # use -p 3010 if :3000 is busy
   ```

## 1. Seed demo data

```bash
cd aegis
chmod +x scripts/demo-seed.sh
./scripts/demo-seed.sh
```

Copy the printed **tenant slug**, **email**, **password**, and **alert ids**.

What the script creates:

- Fresh tenant + Admin user  
- Customer (KE) + mobile wallet account  
- Five sub-threshold credits (structuring → `STRUCTURING_001` alert)  
- One credit with `counterpartyCountry=KP` (→ `HIGH_RISK_GEOGRAPHY_001` when amount ≥ 10k)

## 2. Console path

1. Open `/login` → enter slug, email, password → Sign in.  
2. **Alerts** — open the structuring alert (rule name contains “Structuring”).  
3. Confirm evidence: rule version, feature values vs thresholds, linked transaction ids.  
4. **Create case** → lands on case detail.  
5. Add a short note (optional).  
6. **Close case** — disposition `FALSE_POSITIVE` (or `SUSPICIOUS_ACTIVITY`), conclusion required.  
7. **Audit** (nav) — expect `TRANSACTION_INGESTED`, `ALERT_CREATED`, `CASE_CREATED`, `CASE_CLOSED` (and note update if used).  
8. Optional: open the geography alert and show a second scenario without a second product surface.

## 3. Optional API checks

With the bearer token from login (or re-login via curl):

- `GET /api/v1/rules` — three active seeds: `STRUCTURING_001`, `RAPID_MOVEMENT_001`, `HIGH_RISK_GEOGRAPHY_001`  
- `GET /api/v1/audit-events` — append-only trail  

## Failure tips

| Symptom | Fix |
|---------|-----|
| Bootstrap 404 | API not Development / `Aegis:AllowDevBootstrap` false |
| Health fail | Wrong port; check launchSettings / `ASPNETCORE_URLS` |
| No alerts after seed | Check API logs; ensure migrations applied |
| Console wrong app on :3000 | Start console on `:3010` and open that URL |

## Non-claims

Do not present this as production-ready KYC, screening, regulatory filing, or live FATF jurisdiction management. The geography list is a **demo seed** editable later via rule draft/activate.
