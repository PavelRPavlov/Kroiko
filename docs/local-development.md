# Local development (no cloud secrets)

> How to run **ATAFurniture.Server** entirely on your machine — no Azure AD B2C tenant,
> no cloud SQL, no Azure Storage account, no Syncfusion licence — using Docker for the
> backing services and Development-only bypasses for auth and the Syncfusion licence.

## What this gets you (and what it doesn't)

✅ You can boot the app and click through **upload → parse → review/edit → generate →
download** for Lonira, Suliver and MegaTrading, against a local SQL Server and a local
blob store.

⚠️ It does **not** validate the parts that genuinely need the real accounts:
- **Azure AD B2C sign-in** is *bypassed* (a fixed dev user is auto-signed-in), so it does
  **not** exercise the real `Microsoft.Identity.Web` flow.
- **SQL** is a fresh local database (schema from EF migrations), not the production data.
- **Blob storage** is [Azurite](https://github.com/Azure/Azurite), not a real Azure account.
- **Email** needs a real Brevo/SendinBlue key; without one the email path won't send.

For the parse/generate correctness core there is a faster, durable check that needs none
of this: `dotnet test` (see `ATAFurniture.Server.Tests/GenerationSmokeTests.cs`).

The full production smoke test (real B2C + SQL + blob + email) still needs the real
secrets — recover them from the deployed App Service configuration. The complete list of
keys the app reads is in the phase notes below and in `docs/implementation/01-dotnet-10-upgrade.md`.

## Prerequisites

- .NET 10 SDK (pinned by `global.json`)
- Docker Desktop
- Optional: Azure CLI **or** Azure Storage Explorer (only to create the blob container)

## Steps

### 1. Start the backing services

```bash
docker compose up -d
```

This starts SQL Server (`localhost,14330`, sa password `Your_strong_Passw0rd!`) and Azurite
(blob on `localhost:10000`). The non-default SQL port `14330` lets it coexist with any other
local SQL Server on `1433`. First run pulls the images (~1.5 GB for SQL Server).

### 2. Blob container — nothing to do

The app creates the `files` blob container on demand (`CreateIfNotExistsAsync`), so no manual
step is needed. Azurite runs with `--skipApiVersionCheck` (in `docker-compose.yml`) because the
Azure SDK requests a newer Storage API version than the emulator recognises — real Azure Storage
supports it, so the check is only skipped locally.

### 3. Set the local user-secrets

```powershell
./scripts/init-local-secrets.ps1
```

This points the app at the Docker services. `SyncfusionLicenseKey` and the `AzureAd:*`
section are intentionally left unset — that's what triggers the Development bypasses.

### 4. Trust the ASP.NET dev HTTPS certificate (first time only)

```bash
dotnet dev-certs https --trust
```

### 5. Run

```bash
dotnet run --project ATAFurniture.Server
```

On first launch, in Development the app auto-applies EF migrations to the local database
and auto-signs-in the dev user (which creates a `User` row with 10 credits). Open the URL
it prints and exercise the flow.

## How the Development-only bypasses work

Both are gated to `Development` and can never take effect in production:

| Bypass | Where | Trigger | Effect |
|---|---|---|---|
| Syncfusion licence | `Program.cs` | `Development` **and** `SyncfusionLicenseKey` empty | Skips licence registration (a Syncfusion dialog may appear). Production still throws if the key is missing. |
| Azure AD B2C auth | `Startup.cs` + `Auth/DevAuthHandler.cs` | `Development` **and** `AzureAd:ClientId` empty | Auto-signs-in a fixed dev user whose claims mirror B2C (`oid`/`name`/`emails`/`extension_*`). |

Override the synthetic dev user via a `DevAuth` section if needed:

```bash
dotnet user-secrets set "DevAuth:CompanyName" "Acme" --project ATAFurniture.Server
```

## Switching back to the real backend

Set the real secrets and the bypasses turn themselves off automatically:

- Set a real `AzureAd:ClientId` (+ the rest of the `AzureAd` section) → real B2C sign-in.
- Set `SyncfusionLicenseKey` → real licence.
- Point `SharkAspNetConnectionString` / `AzureStorageConnectionString` / `EmailSettings:*`
  at the real resources.

## Tear down

```bash
docker compose down       # keep data
docker compose down -v    # also delete the SQL + Azurite volumes
```
