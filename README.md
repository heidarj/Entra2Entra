# Entra2Entra

**Goal:** Build a minimal **C#/.NET 10** ASP.NET Core API that acts as a **SCIM 2.0 server** (receive `POST /Users`, `PATCH /Users/{id}`, `DELETE /Users/{id}` from Microsoft Entra) and **pushes those changes to a destination Entra tenant** using **API-driven inbound provisioning `bulkUpload`**. Use **EF Core InMemory** for persistence (MVP), **dotnet user-secrets** for config, and prefer **Microsoft Graph .NET SDK** *if it supports `bulkUpload`*; otherwise use a typed **HttpClient**.

**Authoritative refs (for you, Codex):**

* `POST /servicePrincipals/{spId}/synchronization/jobs/{jobId}/bulkUpload` (v1.0) — max **50 operations/request**, **40 req/sec**. ([Microsoft Learn][1])

---

### High-level flow

1. Entra (source) sends SCIM requests to our API.
2. We normalize → enqueue in EF (InMemory) → batch (≤50) → call Graph **`/bulkUpload`** on the destination sync job.
3. Return 2xx to Entra quickly (target ≤10–15s). Handle Graph 429/5xx with backoff.

### Tech & packages

* .NET 10, ASP.NET Core Minimal API.
* **EF Core InMemory** for a simple queue + audit (no external DB).
* **Microsoft.Graph SDK**: **use if it has a typed `bulkUpload`**; else craft REST with `HttpClient` (strongly-typed request/response).
* **Microsoft.Identity.Web / Azure.Identity** (client-credentials to destination tenant).

### Config (use **dotnet user-secrets**, not env vars)

* `Graph:TenantId`, `Graph:ClientId`, `Graph:ClientSecret`
* `Graph:ServicePrincipalId` (the enterprise app hosting the sync job)
* `Graph:SyncJobId`
* `Scim:SharedSecret`
* `Provisioning:Batches:MaxOperations=50`, `Provisioning:Batches:FlushSeconds=5`
* Add `IOptions` validation and fail-fast on missing secrets.

### EF Core (InMemory) data shapes (MVP)

* `ProvisionRecord`: `Id`, `Type` = Create|Update|Delete, `PayloadJson`, `CorrelationId`, `CreatedUtc`, `Attempts`, `Status`
* `AuditLog`: `Id`, `Direction` = Inbound|Outbound, `Endpoint`, `StatusCode`, `BodyHash`, `CreatedUtc`
* Keep DbContext small; add an index on `Status, CreatedUtc`.

### Endpoints (MVP)

* `GET /scim/v2/ServiceProviderConfig`, `GET /scim/v2/Schemas`, `GET /scim/v2/Users` (basic)
* `POST /scim/v2/Users` → add **Create** record
* `PATCH /scim/v2/Users/{id}` → parse RFC 7644 ops (`add|replace|remove`) → add **Update/Disable** record
* `DELETE /scim/v2/Users/{id}` → add **Delete** record
* `POST /admin/flush` → immediate batch flush (test hook)

### SCIM → bulkUpload mapping (MVP)

* From SCIM **User**:

  * `userName` → dest `userPrincipalName`
  * `name.givenName` → `givenName`
  * `name.familyName` → `surname`
  * `emails[primary]` → `mail`
  * `active=false` → disable/delete per mapping
* Build operations array (≤50). Each operation uses SCIM Bulk schema as required by `bulkUpload` (“schemas”: `urn:ietf:params:scim:api:messages:2.0:BulkRequest`, `method: "POST"`, `path: "/Users"`, `bulkId`, `data` per MS docs). ([Microsoft Learn][2])

### Batching & delivery

* Background hosted service reads pending `ProvisionRecord`s, groups by type/constraints, and posts **`bulkUpload`**.
* Respect **40 rps** cap; exponential backoff on 429/5xx; partial-failure handling (retry only failed items). ([Microsoft Learn][3])

### AuthN/Z

* SCIM inbound: shared secret header (MVP).
* Destination Graph: app registration in **destination** tenant with required app perms to provisioning APIs (per MS docs).
* Validate JWT/bearer support later.

### Graph SDK vs HTTP

* **First, attempt** with Microsoft Graph .NET SDK; if no typed request builder for `bulkUpload`, fall back to **HttpClient** with strongly-typed models and request factory. (PowerShell SDK has bulk cmdlets; .NET typed support may lag—use REST if needed.) ([Microsoft Learn][4])

### Idempotency & errors

* Duplicate create → treat as success (do not corrupt queue).
* Delete of missing user → treat as success (return 204 to Entra).
* Always log correlationId; redact PII in logs.

### Tests

* Unit tests: SCIM PATCH parser, mapping, batching (cut at 50), retry/backoff policy.
* Integration test (DRY mode): simulate `POST /Users`, verify composed `bulkUpload` body.

---

## Clear Future Vision (post-MVP roadmap)

1. **Storage & scale**

* Move from InMemory ⇒ **Cosmos DB** with hierarchical PK: `/sourceTenantId/destinationTenantId`.
* Outbox + **Azure Queue/Service Bus** for reliable delivery and replay.

2. **Multi-tenant orchestration**

* Multiple sources & destinations; per-tenant credentials in **Key Vault**; connection isolation; per-tenant throttling.

3. **Time-bound & on-demand access**

* Add `expiresAt` on mappings; **Timer-triggered** deprovision; optional grace period.
* On-demand “provision now/revoke now” admin API.

4. **Roles & groups**

* Assign **directory roles / PIM** in destination; group membership and app role assignments.

5. **Security & compliance**

* JWT for SCIM inbound; mTLS option; full audit trails; PII-safe structured logging.

6. **Resilience & ops**

* Dead-letter queue, poison message handling, replay tooling; dashboards (App Insights/OpenTelemetry).

7. **Protocol surface**

* Full SCIM discovery; `/Groups` support; `ETag`/If-Match; richer filtering on `GET /Users`.

8. **UX**

* Admin portal for connections, mappings, runs, errors; dry-run/diff previews.

9. **Performance**

* Parallelized batching with fairness across tenants; smart coalescing of rapid updates.

---

**Acceptance criteria**

* `dotnet run` exposes SCIM endpoints, accepts Entra provisioning calls, persists records in EF InMemory, and emits a correct `bulkUpload` body (or calls Graph if secrets provided).
* Config via **dotnet user-secrets** only; startup validates and prints missing keys clearly.
* Tests green; formatter/lint clean; README documents Entra provisioning setup + user-secrets.

---

[1]: https://learn.microsoft.com/en-us/graph/api/synchronization-synchronizationjob-post-bulkupload?view=graph-rest-1.0&utm_source=chatgpt.com "Perform bulkUpload - Microsoft Graph v1.0"
[2]: https://learn.microsoft.com/en-us/graph/api/resources/synchronization-bulkupload?view=graph-rest-1.0&utm_source=chatgpt.com "bulkUpload resource type - Microsoft Graph v1.0"
[3]: https://learn.microsoft.com/en-us/answers/questions/2263784/bulk-provisioning-limits-and-pagination-api-driven?utm_source=chatgpt.com "Bulk provisioning limits and pagination - API driven ..."
[4]: https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.applications/get-mgserviceprincipalsynchronizationjobbulkupload?view=graph-powershell-1.0&utm_source=chatgpt.com "Get-MgServicePrincipalSynchronizationJobBulkUpload"
