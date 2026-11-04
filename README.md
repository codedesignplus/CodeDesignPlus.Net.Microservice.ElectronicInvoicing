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
| Issuer | The tenant that issues the document before the tax authority. |
| Electronic document | An invoice, credit note or debit note sent to the tax authority through a provider. |
| Reference | Opaque text of the caller (for example `orders:order:{id}`) that comes back in the events, so each caller can tell which documents are its own. |
| Technology provider | The company that signs and sends the documents to the tax authority on behalf of the issuer. |

## Domain Model

### ElectronicDocumentAggregate

| Field | Type | Description |
| --- | --- | --- |
| `Id` | `Guid` | Given by the caller, derived from its own source: repeating the request does not issue twice. |
| `Tenant` | `Guid` | The issuer. |
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

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `ElectronicDocument/{id}` | A document of the tenant of the user. |
| `POST` | `ElectronicDocument` | Creates a document (`id`, `reference`). |

### gRPC (`ElectronicInvoicing`)

| RPC | Description |
| --- | --- |
| `GetDocument` | A document of the tenant of the caller. |

## Configuration and secrets

The repository is public: it only carries local example values (`root`, `pass`, `localhost`). Real values come from
Vault (`security-codedesignplus-keyvalue/ms-electronicinvoicing`), among them the master token of the provider under
`Aliaddo:Master:Token`, whose key is declared empty in `appsettings.json`. `tools/vault/config-vault.sh` reads it from
the `ALIADDO_MASTER_TOKEN` environment variable for a local Vault.

## Running locally

```bash
dotnet build CodeDesignPlus.Net.Microservice.ElectronicInvoicing.sln
dotnet test CodeDesignPlus.Net.Microservice.ElectronicInvoicing.sln
```

The repository tests under `tests/integration` start a disposable Mongo container and need Docker.
