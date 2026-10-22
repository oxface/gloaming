# Exports producer API

An API-only host for the existing state-stored Exports module. It registers Exports' native
DbContext and outbox enqueue operation, with no dispatch/intake/processing workers and no
broker connection. Accepted submissions save business state and outgoing work in the same
explicit EF transaction. This is a local, unauthenticated sample command surface; a deployed
API must supply its own authentication/admission and operational endpoints.

Run [explicit setup](../MessagingWorkerDemo/README.md) first, then:

```bash
export ConnectionStrings__Exports='Host=localhost;Database=exports_demo;Username=demo;Password=demo'
export ASPNETCORE_URLS='http://127.0.0.1:5080'
dotnet run --project src/Gloaming.Messaging/samples/MessagingProducerDemo/MessagingProducerDemo.csproj
```

Create a draft and submit it using its observed version:

```bash
curl -i http://127.0.0.1:5080/exports \
  -H 'Content-Type: application/json' \
  -d '{"id":"c2f79e58-17d2-4b8c-aad8-0119b5bde639","pages":12}'
curl -i http://127.0.0.1:5080/exports/c2f79e58-17d2-4b8c-aad8-0119b5bde639/submit \
  -H 'Content-Type: application/json' -d '{"expectedVersion":1}'
```

Creation returns 201 with version 1; eligible submission returns 202. Missing drafts return
404, version conflicts 409 and ineligible submissions 422. Zero-page drafts demonstrate a
state-dependent rejection. GET `/health/live` is process liveness, not dependency readiness.
Startup does not apply migrations or seed data. For this sample, retrying creation with an
existing ID is not an idempotent API contract; existing module tests prove the command's
concurrency and transactional behavior.

After the submission commits, this API may stop. The [independent worker roles](../MessagingWorkerDemo/README.md)
deliver and process its retained command. The receiver knows the integration envelope and
wire alias/version, rather than accessing Exports' DbContext or business DTOs.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../docs/observability.md).
Program.cs calls the existing editable [ServiceDefaults](../../../../samples/Wholesale/ServiceDefaults/README.md)
and adds its messaging source/meter through the optional AddGloamingMessaging extensions,
along with its producer resource. No broker/worker dependencies
are introduced by sharing that host configuration.
