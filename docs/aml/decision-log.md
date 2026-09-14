# AML Requirement Decision Log

The following decisions must be resolved with the compliance and product teams before the scenarios can be activated in any environment above Development.

| Decision ID | Scenario | Question / Open Item | Status | Owner |
|-------------|----------|-----------------------|--------|-------|
| DEC-STR-001 | AML-001 | What is the `ReportingThreshold` amount? | Open | Compliance |
| DEC-STR-002 | AML-001 | What constitutes a "short period" (`TimeWindow`) for structuring? | Open | Compliance |
| DEC-PASS-001| AML-002 | What is the acceptable variance percentage for pass-through amounts? | Open | Compliance |
| DEC-JUR-001 | AML-003 | Which authority list defines `HighRiskCountryList` (e.g., FATF)? | Open | Compliance |
| DEC-CRY-001 | AML-004 | What is the `CryptoThreshold` for VASP interactions? | Open | Compliance |
| DEC-NEW-001 | AML-005 | How many days defines a `NewAccountWindow`? | Open | Compliance |
| DEC-BEH-001 | AML-006 | What `Multiplier` determines a significant deviation from historical average? | Open | Compliance |
| DEC-DORM-001| AML-007 | How many days of inactivity defines `DormancyThreshold`? | Open | Compliance |
| DEC-RND-001 | AML-008 | What is the `ThresholdCount` for round dollar transactions? | Open | Compliance |
| DEC-DEV-001 | AML-009 | How many `DistinctAccounts` are allowed per IP before alerting? | Open | Risk |
| DEC-GEN-001 | General | Do we alert on failed transactions or only settled ones? | Open | Compliance |
| DEC-GEN-002 | General | How long should historical data be retained for lookbacks? (e.g., 5 years) | Open | Legal |
| DEC-CASE-001| Cases | Who is the primary fallback owner for unassigned alerts? | Open | Operations |
| DEC-CASE-002| Cases | SLA for reviewing high-priority alerts (hours/days)? | Open | Operations |
| DEC-CASE-003| Cases | Can alerts auto-close if subsequent activity lowers risk score? | Open | Compliance |
