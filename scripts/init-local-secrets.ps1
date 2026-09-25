# Initializes local-development user-secrets for ATAFurniture.Server.
#
# These are THROWAWAY local values that point at the Docker MSSQL + Azurite services from
# docker-compose.yml. Never use them in production. Run once after cloning:
#
#   ./scripts/init-local-secrets.ps1
#
# The Azure AD B2C (AzureAd:*) section is intentionally left unset: in Development the app
# falls back to an auto sign-in dev user (see Startup.cs / Auth/DevAuthHandler.cs).

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\ATAFurniture.Server"

dotnet user-secrets init --project $project | Out-Null

dotnet user-secrets set "SharkAspNetConnectionString" "Server=localhost,14330;Database=Kroiko;User Id=sa;Password=Your_strong_Passw0rd!;TrustServerCertificate=True;MultipleActiveResultSets=true" --project $project
dotnet user-secrets set "AzureStorageConnectionString" "UseDevelopmentStorage=true" --project $project
dotnet user-secrets set "StorageContainerName" "files" --project $project
dotnet user-secrets set "EmailSettings:Name" "Local Dev" --project $project
dotnet user-secrets set "EmailSettings:Email" "dev@example.com" --project $project
dotnet user-secrets set "EmailSettings:EmailTemplateId" "1" --project $project

Write-Host ""
Write-Host "Local user-secrets set for ATAFurniture.Server." -ForegroundColor Green
Write-Host "Optional: set a real Brevo key to test the email path:" -ForegroundColor Yellow
Write-Host '  dotnet user-secrets set "EmailSettings:ApiKey" "<your-brevo-key>" --project ATAFurniture.Server'
