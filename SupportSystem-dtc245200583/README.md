# Customer Support Ticket System

MVP implementation of **Đề tài 07 — Hệ thống hỗ trợ yêu cầu khách hàng** using .NET 8, PostgreSQL, RabbitMQ and SignalR.

## Included

- `Support.Api`: ticket REST endpoints, development OpenAPI, JWT authentication outside Development, demo authentication in Development, SignalR hub, PostgreSQL persistence and transactional outbox publisher.
- `Support.AssignmentWorker`: consumes `TicketCreated.v1`, assigns the configured default team, records history and writes `TicketAssigned.v1` to the outbox. Consumer processing is idempotent.
- `Support.Contracts`: shared status rules and event contracts.
- `docs/01-yeu-cau-erd-api.md`: requirements, roles, ERD and REST contract for weeks 1–3.
- `Support.Api/Database/001_initial_schema.sql`: versioned, transactional initial schema migration.
- RabbitMQ topic exchange and durable assignment/notification queues.
- Docker Compose for local PostgreSQL, RabbitMQ, API and worker.

## Run locally

Requires Docker Desktop with Compose. From this directory:

```sh
docker compose up --build
```

API: `http://localhost:8080`; OpenAPI document in Development: `http://localhost:8080/openapi/v1.json`. RabbitMQ management: `http://localhost:15672` (`guest` / `guest`). The local database credentials in Compose are for development only.

The solution file is `CustomerSupport.sln` and all projects target `net8.0`. With the .NET 8 SDK installed, restore/build from this directory with `dotnet build CustomerSupport.sln`. To run without Docker, start PostgreSQL and RabbitMQ first, then run `dotnet run --project Support.Api` and `dotnet run --project Support.AssignmentWorker` in separate terminals.

Use the latest .NET 8 servicing runtime available on the machine. Microsoft lists .NET 8 support as ending on November 10, 2026, so plan an upgrade to a supported .NET LTS version when the machine allows it.

Development requests use a demo identity. The default caller is a Customer with ID `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`. Send `X-Demo-User: <uuid>` to select a user and `X-Demo-Role: Agent` or `Admin` to exercise staff endpoints. This handler is enabled only when `ASPNETCORE_ENVIRONMENT=Development`; never use it in a deployed environment. Production requires `Authentication__Authority` and `Authentication__Audience` for an external JWT issuer.

Create a ticket:

```sh
curl -X POST http://localhost:8080/api/v1/tickets \
  -H "Content-Type: application/json" \
  -H "X-Demo-User: aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" \
  -d '{"subject":"Cannot sign in","description":"The verification code is rejected.","priority":"Normal"}'
```

List tickets with `GET /api/v1/tickets`; read one with `GET /api/v1/tickets/{id}`. An Admin can assign a ticket using `PATCH /api/v1/tickets/{id}/assignment` with `teamId`, optional `assigneeId`, `expectedVersion`, and optional `reason`; the assignee must be an active member of that team. An Admin can update any ticket status with `PATCH /api/v1/tickets/{id}/status`; an Agent can update tickets assigned to that Agent. Supported transitions are New → Assigned → InProgress → Resolved.

## Important implementation notes

- The API applies the embedded SQL migration at startup and tracks it in `SchemaMigrations`. Add a new numbered migration for later schema changes; avoid editing a migration after it has shipped.
- The initial assignment rule categorizes tickets as `General` and assigns the configured default team (`Assignment__DefaultTeamId`). Team membership and least-open-ticket balancing are follow-up work.
- The default SignalR notification currently targets the requester user group. Add verified team membership and assignee group subscriptions when the identity/team directory is connected.
- RabbitMQ and database credentials are intentionally simple local values in Compose. Use managed secrets, TLS, backups, monitoring, and production-grade broker/database configuration before deployment.
- This repository is an MVP foundation, not a production deployment package. Configure retention, privacy policy, rate limiting, and operational alerts for the target environment.
