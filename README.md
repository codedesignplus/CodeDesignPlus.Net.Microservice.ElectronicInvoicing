# Electronic Invoicing Microservice

## Overview

The Electronic Invoicing microservice issues electronic documents (invoices, credit notes and debit notes) on behalf of
an issuer through a technology provider authorized by the tax authority. It knows about **issuers, buyers, documents
and opaque references**; it does not know which product or module asked for a document.

> **Status:** skeleton. The service builds, runs its three entry points and carries a minimal
> `ElectronicDocumentAggregate` as a placeholder. Issuers, numbering ranges, the provider strategy and the status
> jobs arrive with the first phase.

## Ubiquitous Language

| Term | Definition |
| --- | --- |
| Issuer | Who issues the document before the tax authority: a tenant, or the platform's issuer. |
| Platform issuer | The company that runs the platform and invoices what it sells (for example, a license sold to a tenant, which is the buyer). It is not a tenant: it is a scope of its own, kept under the tenant id reserved by this service, `00000000-e1ec-0000-0000-000000000001` (`PlatformScope`), which no tenant can have or name. |
| Electronic document | An invoice, credit note or debit note sent to the tax authority through a provider. |
| Reference | Opaque text of the caller (for example `orders:order:{id}`) that comes back in the events, so each caller can tell which documents are its own. |
| Technology provider | The company that signs and sends the documents to the tax authority on behalf of the issuer. |

## Domain Model

### ElectronicDocumentAggregate

| Field | Type | Description |
| --- | --- | --- |
| `Id` | `Guid` | Given by the caller, derived from its own source: repeating the request does not issue twice. |
| `Tenant` | `Guid` | The tenant of the issuer; the reserved id of `PlatformScope` for the platform's issuer. |
| `Reference` | `string` (up to 200) | The caller's opaque reference. |

## Event Catalog

### Events Produced

| Event | When |
| --- | --- |
| `ElectronicDocumentCreatedDomainEvent` | A document was created. |

### Events Consumed

| Event | Source | What it does |
| --- | --- | --- |
| `TenantPurgedDomainEvent` | ms-tenants | Deletes every document of the purged tenant. |

## API Reference

### REST (`/ms-electronicinvoicing/api/`)

Each group exists twice with the same actions and bodies: the tenant's controller works with the issuer of the tenant
of the session, and its `Platform` twin works with the platform's issuer, without depending on the tenant of the
session. The controller fixes the scope; the body cannot change it. Who may call each one is decided by the RBAC by
controller (the platform's ones, for whoever runs the platform); this service does not check roles.

| Method | Tenant route | Platform route | Description |
| --- | --- | --- | --- |
| `GET` | `Issuer` | `PlatformIssuer` | The issuer with its capabilities and its quota; 204 when it has none. |
| `POST` | `Issuer` | `PlatformIssuer` | Registers the issuer (fiscal data and provider). |
| `PUT` | `Issuer` | `PlatformIssuer` | Replaces its fiscal data. |
| `PUT` | `Issuer/provider` | `PlatformIssuer/provider` | Changes the provider before the issuer exists in one. |
| `POST` | `Issuer/mandate` | `PlatformIssuer/mandate` | Records the accepted mandate. |
| `PUT` | `Issuer/credentials` | `PlatformIssuer/credentials` | Connects the credentials of a provider with an account per issuer. |
| `POST` | `Issuer/provision` | `PlatformIssuer/provision` | Creates the issuer in its provider. |
| `POST` | `Issuer/testing` | `PlatformIssuer/testing` | Starts the enablement tests with the test set. |
| `POST` | `Issuer/enable` | `PlatformIssuer/enable` | Enables it for an environment. |
| `POST` | `Issuer/suspend` | `PlatformIssuer/suspend` | Suspends it. |
| `GET` | `NumberingRange` | `PlatformNumberingRange` | Its numbering ranges. |
| `POST` | `NumberingRange` | `PlatformNumberingRange` | Registers a range by hand. |
| `POST` | `NumberingRange/sync` | `PlatformNumberingRange/sync` | Brings a range from the provider. |
| `GET` | `ElectronicDocument` | `PlatformElectronicDocument` | Its documents, by criteria. |
| `GET` | `ElectronicDocument/{id}` | `PlatformElectronicDocument/{id}` | A document. |
| `POST` | `ElectronicDocument/{id}/retry` | `PlatformElectronicDocument/{id}/retry` | Brings a waiting document forward. |

`IssuerDto` and `ElectronicDocumentDto` carry `platformIssuer` (`true` for the platform's issuer). The provider
webhooks arrive at `ProviderEvents/{provider}` (anonymous, checked by the adapter).

### gRPC (`ElectronicInvoicing`)

Anonymous and internal to the cluster. The request alone decides the issuer; the tenant of the caller's context is
never read:

| The request carries | Issuer |
| --- | --- |
| A tenant, `platform_issuer = false` | The issuer of that tenant. |
| No tenant (with or without `platform_issuer = true`) | The platform's issuer. |
| A tenant and `platform_issuer = true` | Refused: `INVALID_ARGUMENT` (error 218). |
| The reserved id of the platform as a tenant | Refused: `INVALID_ARGUMENT` (error 219). |

| RPC | Description |
| --- | --- |
| `IssueDocument` | Issues a document (`issuer_tenant`, `platform_issuer`). For example, ms-licenses invoices a license it sold with `platform_issuer = true`, no `issuer_tenant`, and the tenant that bought it in `content.buyer`. |
| `GetDocument` | A document (`tenant`, `platform_issuer`); `tenant` comes back empty and `platform_issuer = true` for the platform's. |
| `RetryDocument` | Corrects a rejected document or looks at a waiting one (`tenant`, `platform_issuer`). |
| `GetIssuer` | The issuer, its capabilities and its quota (`tenant`, `platform_issuer`). |

The events of a document of the platform's issuer carry the reserved id as their `Tenant`.

## Configuration and secrets

The repository is public: it only carries local example values (`root`, `pass`, `localhost`). Real values come from
Vault (`security-codedesignplus-keyvalue/ms-electronicinvoicing`), among them the tokens of the providers under
`Aliaddo:Master:Token` and `Alanube:Master:Token` and the secret of the webhooks of Alanube under
`Alanube:Webhooks:Secret`, whose keys are declared empty in `appsettings.json`. `tools/vault/config-vault.sh` reads them
from the `ALIADDO_MASTER_TOKEN`, `ALANUBE_CO_TOKEN` and `ALANUBE_WEBHOOK_SECRET` environment variables for a local
Vault. In `Development`, `Alanube:Master:TokenKey` points to `ALANUBE_CO_TOKEN`, so the token of the sandbox is read
from the environment.

The platform's issuer is not configured: there is no tenant to set. Its quota is
`ElectronicInvoicing:PlatformIssuer:ReservedDocuments`, out of the provider's plan, and it never reads a license.

### Providers

Every adapter is registered; `ElectronicInvoicing:Routing` says which ones each country allows, and the first one is
the default of a new issuer (each issuer keeps the code of its provider).

| Code | Provider | Notes |
| --- | --- | --- |
| `Aliaddo` | Aliaddo Nitro | Main account with child organizations; this service numbers. |
| `Alanube` | Alanube (API of Alegra as electronic provider) | Main company with associated companies; this service numbers; synchronous emission with an `Idempotency-Key`; test sets sent by Alanube; webhooks trusted by a secret header. The base URL chooses the environment: sandbox or production. |
| `Simulated` | None | Answers by configuration, for development. |

## Running locally

```bash
dotnet build CodeDesignPlus.Net.Microservice.ElectronicInvoicing.sln
dotnet test CodeDesignPlus.Net.Microservice.ElectronicInvoicing.sln
```

The repository tests under `tests/integration` start a disposable Mongo container and need Docker. The tests of the
sandbox of Alanube (`AlanubeSandboxTest`) run only when `ALANUBE_CO_TOKEN` is set, and are skipped otherwise.
