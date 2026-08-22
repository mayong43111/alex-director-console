# Container build and Azure deployment

## Build and run

Build the Azure Linux target image:

```powershell
docker build --platform linux/amd64 --tag alex-director-console:local .
```

Run locally with persistent SQLite and Data Protection files:

```powershell
docker volume create alex-director-console-data
docker run --rm --publish 8080:8080 `
  --mount source=alex-director-console-data,target=/app/App_Data `
  alex-director-console:local
```

The image serves the Vite frontend and `/api` from one ASP.NET Core process on port 8080. The Dockerfile maps the .NET runtime identifier from Docker's target architecture, so both `linux/amd64` and `linux/arm64` builds are supported.

## Production deployment

The production deployment uses Azure Container Apps Consumption because the subscription has no App Service worker quota in Japan East.

Run the complete validated release from the repository root:

```powershell
.\deploy-production.ps1 -CommitMessage "feat: describe the release"
```

The script runs API tests and the frontend build, commits all current source changes, pushes the current branch, builds a commit-tagged Linux AMD64 image in ACR, updates the Container App, and verifies the deployed image and HTTPS endpoint. Use `-SkipValidation` only when validation has already completed for the exact working tree.

- URL: `https://ca-alex-director-66595.happysmoke-d5662775.japaneast.azurecontainerapps.io/`
- Container App: `ca-alex-director-66595`
- Environment: `cae-alex-director-jpe`
- Image: `alexdirector66595.azurecr.io/alex-director-console:20260822-aca`
- Scale: one replica minimum and maximum
- Identity: `id-alex-director-app`
- Database: `sqlalexdirector66595.database.windows.net`, database `alex-director`
- Database SKU: General Purpose Serverless Gen5, 2 vCores, 0.5 minimum vCore, 60-minute auto-pause, subscription free limit enabled
- SQLite backup: `stalexdirector66595/sqlite-backups/initial/alex-director-v2-20260822.db`
- Data Protection key blob: `stalexdirector66595/sqlite-backups/dataprotection/keys.xml`

Azure SQL and Blob Storage are reachable through Private Endpoints in `vm-qwen-lora-a100-jpeVNET`. Public network access is disabled on both services. The application uses its managed identity for Azure SQL, Blob Storage, and ACR; no SQL or storage credentials are stored in source control.

The runtime settings are:

```text
Database__Provider=SqlServer
ConnectionStrings__V2Database=secretref:sql-connection
Azure__ManagedIdentityClientId=18f1fcb5-8de1-4139-90fa-0a9660cfa595
DataProtection__BlobUri=https://stalexdirector66595.blob.core.windows.net/sqlite-backups/dataprotection/keys.xml
ASPNETCORE_ENVIRONMENT=Production
```

## Authentication

Azure Container Apps built-in authentication protects the complete site and API. The application itself does not implement user authentication.

- Provider: Microsoft Entra ID
- Registration: `Alex Director Console`
- Client ID: `2053a34a-c49a-434e-a9c4-6fa1ce37e77b`
- Registration audience: `AzureADMultipleOrgs`
- Token issuer: `f74af430-12a3-4377-b0bb-20cc68a19822` (only this tenant is accepted by Container Apps authentication)
- Unauthenticated action: redirect to the Microsoft login page
- HTTPS: required; insecure ingress disabled
- Token store: disabled

The client secret expires after one year from 2026-08-22 and must be rotated before expiry. Update the Entra application credential and the Container Apps Microsoft provider secret together.

The original Foundry API key was protected by a Windows DPAPI-backed key ring and cannot be decrypted by the Linux deployment. Re-enter that key once in Settings > Server Connections; subsequent values use the Blob-backed cross-platform key ring.

## Database migration

Local development and tests continue to use SQLite. Production selects SQL Server with `Database__Provider=SqlServer`. The initial production import copied 27 tables and 3,201 rows from a SQLite online backup, then re-enabled SQL constraints and verified every table's source and target row count.

Do not mount a live SQLite database on Blob Storage or Azure Files. Blob Storage is used for immutable SQLite backup snapshots and Data Protection keys, not as a SQLite filesystem.

## Prior App Service cost analysis

Analysis date: 2026-08-22. Target subscription/resource placement:

- Subscription: `yongma-1`
- Resource group: `RG-QWEN-LORA-JPE`
- Region: `japaneast`
- ComfyUI VM: `vm-comfyui-a100-spot-jpe`

Pay-as-you-go prices below came from the Azure Retail Prices API for Japan East. Monthly estimates use 730 hours and exclude tax, support, outbound data transfer, monitoring ingestion, and other attached services.

| Linux plan | vCPU | Memory | Plan storage | USD/hour | USD/month |
| --- | ---: | ---: | ---: | ---: | ---: |
| B1 | 1 | 1.75 GB | 10 GB | 0.019 | 13.87 |
| B2 | 2 | 3.5 GB | 10 GB | 0.037 | 27.01 |
| B3 | 4 | 7 GB | 10 GB | 0.073 | 53.29 |
| P0v3 | 1 | 4 GB | 250 GB | 0.094 | 68.62 |
| S1 | 1 | 1.75 GB | 50 GB | 0.112 | 81.76 |
| P1v3 | 2 | 8 GB | 250 GB | 0.188 | 137.24 |

Azure Container Registry Basic in Japan East is USD 0.1666/day, approximately USD 5.00/month. VNet integration itself has no extra charge beyond the App Service plan.

### Recommendation

Use one **P0v3** instance for the initial production deployment. Its 4 GB memory and 250 GB plan storage are a better fit for ASP.NET, the background production worker, SQLite, Data Protection keys, and media-heavy records. B1 is acceptable only for a low-traffic pilot with closely monitored storage and memory. S1 costs more than P0v3 in Japan East while providing less memory.

Expected baseline is approximately **USD 73.62/month** for P0v3 plus ACR Basic, before logs, bandwidth, backups, tax, and discounts.

## ComfyUI private connectivity

Being in the same resource group does not provide network connectivity. The VM uses VNet `vm-qwen-lora-a100-jpeVNET`, subnet `vm-qwen-lora-a100-jpeSubnet` (`10.0.0.0/24`), and private IP `10.0.0.4`. The VNet address space is `10.0.0.0/16`.

For private ComfyUI access:

1. Expose ComfyUI through a private VM listener or authenticated reverse proxy. Its current loopback-only listener cannot be reached from Container Apps.
2. Restrict the VM NSG to allow the Container Apps infrastructure subnet `10.0.4.0/23` only on the chosen private service port.
3. Configure the application ComfyUI base URL to use the VM private endpoint.
4. Keep public ComfyUI ingress disabled.
