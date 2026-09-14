# ADR-004: JSON Rule Definition Format for AML Rules

**Status:** Accepted  
**Date:** 2026-09-13  
**Deciders:** Principal Architect

## Context
We need a way to define AML (Anti-Money Laundering) transaction monitoring rules. Options include full-blown rule engines (DROOLS), expression languages (CEL, FEEL), or a custom JSON-based format.

## Decision
We will use a **Custom JSON Format** for rule definitions instead of adopting a complex external engine.
- Rules will be stored as JSON objects defining conditions, parameters, and thresholds.
- A custom evaluation engine within the AML module will parse and execute these JSON rules against transaction aggregates.

## Reasons
- Full rule engines like DROOLS introduce significant overhead and a steep learning curve.
- Expression languages like CEL are powerful but can allow too much flexibility, leading to non-deterministic execution times or security risks if not carefully sandboxed.
- A structured JSON format ensures we tightly control the allowed operators and aggregations, keeping execution safe and performant.

## Consequences
- **Positive:** Simple to parse, store, and version.
- **Positive:** Easy to build a UI builder around structured JSON.
- **Negative:** We have to build and maintain the evaluation engine ourselves.
