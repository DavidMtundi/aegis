# ADR-006: T+1 Batch Processing for AML Scenarios

**Status:** Accepted  
**Date:** 2026-09-13  
**Deciders:** Principal Architect

## Context
Monitoring transactions for money laundering patterns often requires looking at aggregated behavior over days, weeks, or months (e.g., velocity rules, structuring). Real-time evaluation of all these scenarios on every transaction is computationally expensive and mostly unnecessary.

## Decision
We will implement **T+1 Batch Processing** as the default mechanism for complex AML scenarios.
- Transactions from day T are aggregated and analyzed on day T+1 (typically overnight).
- Only specific, high-risk scenarios (like immediate sanctions screening) will be evaluated in real-time.

## Consequences
- **Positive:** Significantly reduces database load and allows for efficient bulk aggregation queries.
- **Negative:** Alerts are delayed by up to 24 hours.
- **Negative:** Requires scheduling infrastructure and robust retry mechanisms for batch jobs.
