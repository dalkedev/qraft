# Qraft development

- Read README.md and docs/development/ARCHITEKTURA.md before changing service boundaries.
- The domain stays independent of EF Core, RabbitMQ and transport DTOs.
- Contracts are shared by both producers and consumers. Keep schema validation strict.
- Preserve MessageId/ResultId correlation, inbox idempotency and transactional outboxes.
- Each service owns its SQLite database. Never delete data or RabbitMQ volumes to hide a bug.
- Run tests relevant to a change. For transport changes also run smoke with the real broker.
- Use Node22 and the SDK pinned by global.json. Run .NET builds with -m:1 in this workspace.
- Keep the current implementation boundary explicit: intake and evaluation work; the full manual E1/E2 decision flow is still to be implemented.
