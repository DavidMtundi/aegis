# ADR-005: Immutable Rule Versioning

**Status:** Accepted  
**Date:** 2026-09-13  
**Deciders:** Principal Architect

## Context
Compliance regulations require strict audit trails. When an alert is generated, we must be able to prove exactly which version of a rule triggered it, along with its precise thresholds at that point in time.

## Decision
All AML rules and risk scoring models will use **Immutable Versioning**.
- Once a rule version is published/activated, it cannot be modified.
- Any change to a rule (even a simple threshold tweak) results in a new version (e.g., v1.0 -> v1.1).
- Past executions and alerts will always reference the specific rule version ID.

## Consequences
- **Positive:** Guarantees auditability and reproducibility of past alerts.
- **Negative:** Requires more storage for rule definitions.
- **Negative:** UI must be designed to show rule history and handle active/inactive states clearly.
