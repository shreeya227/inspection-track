# InspectionTrack

**Tracking loss-prevention recommendations from site inspection to closure.**

Built with C#, ASP.NET Core, Entity Framework Core, Azure SQL, Azure Functions, Key Vault, Application Insights, and Azure DevOps.

---

## The problem

When a risk engineer inspects a commercial property, the output is a set of recommendations: install sprinklers in the rack storage area, replace the corroded fire pump controller, start a hot work permit program. Each one reduces the chance of a fire, flood, or other loss.

The inspection is the easy part. What happens afterwards is where risk actually goes unmanaged:

- Recommendations sit in PDF reports and email threads, so nobody has a single view of what is still open.
- A critical recommendation can quietly go a year without action, and nobody notices until the next inspection or, worse, a claim.
- When a client says something was fixed, there is often no record of who confirmed it or what the evidence was.
- Insurers and consultants cannot easily answer "how many critical items are overdue across this client's sites?"

InspectionTrack gives every recommendation an owner-visible status, a due date based on its severity, an audit trail of every change, and an automatic daily check that flags anything past due.

## What it does

- **Sites and recommendations.** Record inspected properties and the recommendations issued for each.
- **Severity-based due dates.** Critical items are due in 30 days, High in 90, Medium in 180, Low in 365, unless a specific date is set.
- **Enforced workflow.** A recommendation must be started before it can be completed, completion requires evidence, and declining requires a reason. Completed is final.
- **Full audit trail.** Every status change records who made it, when, and why.
- **Daily overdue sweep.** An Azure Function runs every morning and flags recommendations that have passed their due date.
- **Dashboard.** Active and overdue counts, open critical items, estimated remediation cost outstanding, and the most overdue items across all sites.

## Architecture

```
                     ┌──────────────────────────────┐
  Browser  ───────►  │  InspectionTrack.Api          │   Azure App Service
  (dashboard)        │  ASP.NET Core minimal APIs    │
                     │  + static HTML/JS front end   │
                     └──────────────┬───────────────┘
                                    │
                     ┌──────────────▼───────────────┐
                     │  InspectionTrack.Core          │   Shared library
                     │  Domain models, business rules │
                     │  EF Core DbContext, services   │
                     └──────────────┬───────────────┘
                                    │
          ┌─────────────────────────┼─────────────────────────┐
          │                         │                         │
┌─────────▼─────────┐     ┌─────────▼─────────┐     ┌─────────▼─────────┐
│ Azure SQL         │     │ Azure Function     │     │ Key Vault         │
│ (SQLite locally)  │     │ Daily overdue sweep│     │ Connection string │
└───────────────────┘     └───────────────────┘     └───────────────────┘

        Application Insights collects logs and telemetry from both the API and the Function.
```

The business rules live in `Core` with no dependency on HTTP or hosting, so the API and the Function share one implementation and the rules can be tested in isolation.

## Tech stack

| Area | Used here |
|---|---|
| Language and framework | C# 12, .NET 8, ASP.NET Core minimal APIs |
| Data | Entity Framework Core 8, Azure SQL (SQLite for local development) |
| Background work | Azure Functions, isolated worker, timer trigger |
| Secrets | Azure Key Vault references in App Service settings |
| Monitoring | Application Insights, health check endpoint |
| Front end | HTML, CSS, JavaScript (no framework, all output escaped) |
| Testing | xUnit, in-memory SQLite, `FakeTimeProvider` for time-dependent logic |
| CI/CD | Azure DevOps pipeline and GitHub Actions |

## Running it locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Run the test suite
dotnet test

# Start the API and dashboard (seeds demo data in Development)
cd src/InspectionTrack.Api
dotnet run --urls http://localhost:5080
```

Then open **http://localhost:5080**.

The demo data includes three fictional sites with a mix of priorities and ages, so several recommendations start out overdue. Try moving one to *In progress* and then *Completed* to see the workflow and audit trail.

To trigger the overdue sweep manually:

```bash
curl -X POST http://localhost:5080/api/admin/sweep
```

To run the Azure Function locally, copy `src/InspectionTrack.Functions/local.settings.json.example` to `local.settings.json` and start it with the Azure Functions Core Tools (`func start`).

## API

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/sites` | Sites with active and overdue counts |
| POST | `/api/sites` | Create a site |
| GET | `/api/sites/{id}/recommendations` | Recommendations for a site, overdue first |
| POST | `/api/sites/{id}/recommendations` | Issue a recommendation |
| POST | `/api/recommendations/{id}/status` | Move a recommendation through the workflow |
| GET | `/api/recommendations/{id}/history` | Audit trail |
| GET | `/api/dashboard` | Portfolio summary |
| POST | `/api/admin/sweep` | Run the overdue check now |
| GET | `/health` | Health check including database connectivity |

Example:

```bash
curl -X POST http://localhost:5080/api/recommendations/1/status \
  -H "Content-Type: application/json" \
  -d '{"status":"InProgress","updatedBy":"J. Rivera"}'
```

## Deploying to Azure

An outline using the Azure CLI. Replace the placeholder names with globally unique values.

```bash
RG=rg-inspectiontrack
LOC=eastus

az group create -n $RG -l $LOC

# Azure SQL
az sql server create -g $RG -n <sql-server> -l $LOC -u <admin-user> -p <admin-password>
az sql server firewall-rule create -g $RG -s <sql-server> -n AllowAzure \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
az sql db create -g $RG -s <sql-server> -n inspectiontrack --service-objective Basic

# Key Vault holds the connection string, never app settings or source control
az keyvault create -g $RG -n <key-vault> -l $LOC
az keyvault secret set --vault-name <key-vault> -n SqlConnection --value "<azure-sql-connection-string>"

# Application Insights
az monitor app-insights component create -g $RG -a ai-inspectiontrack -l $LOC

# App Service for the API, with a managed identity to read Key Vault
az appservice plan create -g $RG -n plan-inspectiontrack --sku B1 --is-linux
az webapp create -g $RG -p plan-inspectiontrack -n <api-app> --runtime "DOTNETCORE:8.0"
PRINCIPAL=$(az webapp identity assign -g $RG -n <api-app> --query principalId -o tsv)
az role assignment create --role "Key Vault Secrets User" --assignee $PRINCIPAL \
  --scope $(az keyvault show -n <key-vault> --query id -o tsv)

az webapp config appsettings set -g $RG -n <api-app> --settings \
  Database__Provider=SqlServer \
  "ConnectionStrings__Default=@Microsoft.KeyVault(VaultName=<key-vault>;SecretName=SqlConnection)" \
  APPLICATIONINSIGHTS_CONNECTION_STRING="<app-insights-connection-string>"

# Function App for the daily sweep
az storage account create -g $RG -n <storage-account> -l $LOC --sku Standard_LRS
az functionapp create -g $RG -n <function-app> --storage-account <storage-account> \
  --consumption-plan-location $LOC --runtime dotnet-isolated --runtime-version 8 \
  --functions-version 4 --os-type Linux
```

The Function App gets the same managed identity, role assignment, and settings as the API. Deployment itself runs from the Azure DevOps pipeline in `azure-pipelines.yml`, which builds, tests, and publishes artifacts for both.

## Local to cloud

The same code runs against SQLite on a laptop and Azure SQL in the cloud. The switch is configuration only, via `Database:Provider` and the connection string, which mirrors the re-platforming step in a typical on-premises to Azure migration: keep the application logic unchanged, move the data tier to a managed service, and move secrets out of configuration files into Key Vault.

## Design decisions

- **Rules separate from infrastructure.** Due dates, allowed transitions, and overdue logic are pure functions in `RecommendationRules`. They are easy to test, and easy for a non-developer to review, which matters when the rules encode business policy.
- **Enums stored as strings.** Makes the database readable in SQL tools and safe against reordering enum members.
- **Explicit response shapes.** The API returns purpose-built records rather than entities, so it never leaks internal fields or serialises circular references.
- **Tests use real SQLite, not the EF InMemory provider.** InMemory skips relational behaviour such as constraints, which can hide real bugs.
- **Time is injected.** `TimeProvider` makes it possible to test "this becomes overdue in 31 days" without waiting 31 days.
- **Front end escapes everything.** Recommendation text is user input and is HTML-escaped before rendering.
- **Retry on transient SQL failures.** Enabled for Azure SQL, where brief connectivity blips are expected.

## What I would add next

- Authentication with Microsoft Entra ID, separating consultant and client roles.
- EF Core migrations in place of `EnsureCreated`, for safe schema changes in production.
- File attachments in Blob Storage for completion evidence such as photos and contractor sign-offs.
- Email or Teams notifications when a recommendation becomes overdue.
- Infrastructure as code with Bicep instead of CLI commands.

---

*Portfolio project. Sites and clients in the demo data are fictional.*
