# ADR-002: PostgreSQL as Primary Data Store

**Status:** Accepted  
**Date:** 2026-09-13  
**Deciders:** Principal Architect

## Context
The platform needs a robust, ACID-compliant relational database for structured data such as user identities, transaction records, and compliance rules. It also needs the flexibility to handle semi-structured data for audit logs and external integrations.

## Decision
We will use **PostgreSQL** as the primary relational database for the platform.
- We will leverage its JSONB capabilities for extensible data models (e.g., varying KYC provider payloads).
- Each module in the modular monolith will have its own logical schema within the database to maintain data isolation.

## Consequences
- **Positive:** Excellent performance, reliability, and rich feature set.
- **Positive:** JSONB support reduces the immediate need for a separate NoSQL document store.
- **Negative:** Schema isolation requires disciplined migration management to prevent coupling.
