# ADR-003: Event-Driven Asynchronous Processing

**Status:** Accepted  
**Date:** 2026-09-13  
**Deciders:** Principal Architect

## Context
Financial crime compliance involves many processes that do not need to be strictly synchronous with the user's critical path. For example, risk scoring a user or screening their name against watchlists can happen asynchronously after the initial onboarding step.

## Decision
We will adopt an **Event-Driven Architecture** for inter-module communication and asynchronous processing.
- Modules will publish domain events when significant state changes occur.
- Other modules will subscribe to these events to trigger their own workflows (e.g., `UserOnboarded` triggers `RunKycScreening`).
- For the initial monolith, we will use an in-memory message broker (like MediatR), but design the handlers to be easily adaptable to an external broker (like RabbitMQ or Kafka) in the future.

## Consequences
- **Positive:** Decouples modules, improving maintainability and resilience.
- **Positive:** Improves response times for synchronous APIs.
- **Negative:** Makes tracing flows harder (requires distributed tracing/correlation IDs).
- **Negative:** Eventual consistency must be handled in the UI and business logic.
