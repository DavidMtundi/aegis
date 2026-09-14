# ADR-001: Modular Monolith as Initial Architecture

**Status:** Accepted  
**Date:** 2026-09-13  
**Deciders:** Principal Architect

## Context
Aegis is starting as a new financial-crime platform. The team is relatively small, and we need to move fast to validate business requirements and reach MVP. However, we anticipate that as the platform grows, different bounded contexts (e.g., KYC, AML, Case Management) will scale differently and might be maintained by independent teams. A microservices architecture upfront would introduce unnecessary operational complexity, distributed transaction problems, and cognitive load for the current team size.

## Decision
We will start building Aegis as a **Modular Monolith** with strong module boundaries.
- All modules will reside in a single repository and run in a single process.
- Modules will only communicate via well-defined interfaces or an in-memory event bus.
- Cross-module database joins are forbidden; each module encapsulates its own data.

## Consequences
- **Positive:** Easier deployment, simpler debugging, faster initial development speed.
- **Positive:** Preserves the ability to extract modules into independent microservices later when needed.
- **Negative:** Requires strict discipline from developers to not violate module boundaries (e.g., no direct access to other modules' databases).
