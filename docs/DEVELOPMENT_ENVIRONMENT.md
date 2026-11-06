# EduOS — reproducible development environment

**Branch:** `Ashraful` only. **Stack:** .NET SDK 10, EF Core 10 / SQL Server 2022, Node.js 22. One modular monolith, one EduOSDbContext, no production database connection in the standard setup.

## Getting a working environment

GitHub Codespaces or VS Code Dev Containers uses [`.devcontainer/devcontainer.json`](../.devcontainer/devcontainer.json) to provision the SDK, Node, and Docker. Open [EduOS on GitHub](https://github.com/cseashrafulislam/EduOS/tree/Ashraful), select **Code → Codespaces → Create codespace on Ashraful**, or clone `Ashraful` and select **Dev Containers: Reopen in Container** in VS Code. These commands run within the new development container (not in the old ChatGPT task sandbox):

```bash
dotnet --version
node --version
dotnet restore EduOS.slnx
dotnet build EduOS.slnx -c Release --no-restore
node --test EduOS.Tests/JavaScript/*.test.cjs
dotnet test EduOS.slnx -c Release --no-build
```

The container's post-create script installs the pinned global `dotnet-ef` 10.0.12 tool and project dependencies. The first restore requires internet access to NuGet. Do not paste production credentials or import a real institution database.

## Local disposable SQL Server

Docker-in-Docker is included; SQL Server is **opt in** (not needed for source review or pure unit tests). From the project root:

```bash
bash .devcontainer/sqlserver.sh start
bash .devcontainer/sqlserver.sh status
bash .devcontainer/sqlserver.sh migrate
bash .devcontainer/sqlserver.sh exec dotnet test EduOS.Tests/EduOS.Tests.csproj -c Release
bash .devcontainer/sqlserver.sh stop
```

The helper generates a strong random SQL SA password at `~/.config/eduos/dev-sql-password` with mode `0600`, stores it outside Git, and maps SQL only to `127.0.0.1:14333`. It creates the isolated `EduOS_Development` database and leaves normal application configuration untouched. Migration is an explicit opt-in; it sets `DatabaseInitialization__Enabled=false` and `DatabaseInitialization__ApplyMigrations=false`. Docker persists local DB data in the `eduos-local-sql-data` volume; clean/reset only after explicit backup/review, since removing the volume loses local development data. Do not use this helper against production.

For interactive application testing, start SQL then execute `bash .devcontainer/sqlserver.sh exec dotnet run --project EduOS.App --launch-profile http`. Codespaces port `5146` must remain **private**; never forward SQL, auth, Admin UI, application secrets or production credentials publicly.

## Safe integration and automation workflow

`Development Environment` GitHub Actions runs automatically whenever the devcontainer/toolchain files change on `Ashraful`, and can be started with **Actions → Development Environment → Run workflow → Ashraful**. The job provisions .NET 10, Node 22 and Docker independently of the ChatGPT task execution sandbox, then validates config, builds the solution and runs JS contract tests. Existing [CI](../.github/workflows/ci.yml) and [SQL Server Migration Smoke](../.github/workflows/sqlserver-migration.yml) remain authoritative for full regression, SQL migration repeatability and disposable SQL-backed acceptance.

**Task agents:** When a scheduled task's sandbox has no `dotnet` or cannot resolve `github.com`, use its connected GitHub repository actions and existing CI/workflow results to inspect source and test exact commit SHA; do not pretend the task sandbox became a Codespace or claim local tests. Use GitHub-hosted runners for build verification, but do not push speculative finance, inventory, tenant/security or migration changes without source-contract review and relevant tests. Preserve branch `Ashraful`, avoid competing pushes, and never force-push. The devcontainer enables a developer to run full tooling; it does **not** grant tasks direct terminal access to a running Codespace.

## Troubleshooting

- **No dotnet SDK:** Rebuild/reopen using the supplied devcontainer; outside Codespaces use .NET 10 SDK.
- **DNS / git fails inside task sandbox:** not repairable by a repository file; use connected GitHub operations or open Codespaces.
- **SQL Server not available:** check Docker daemon; `bash .devcontainer/sqlserver.sh start` starts the local-only instance. Do not assume a running Codespace is free of cloud compute charges.
- **EF tool mismatch:** use `dotnet tool update --global dotnet-ef --version 10.0.12` in the development container; do not add a local tool manifest because the existing GitHub CI installs global EF tools.
- **SQL tests:** existing suites may require additional named `EDUOS_SQL_*_TEST_CONNECTION` variables; use their existing test/CI contracts rather than assuming all tests read `ConnectionStrings__DefaultConnection`.

## Actual dev container image validation

The [Dev Container Image Smoke](../.github/workflows/devcontainer-image-smoke.yml) workflow builds the **actual devcontainer image** (including the Node and Docker-in-Docker features) using the pinned official `devcontainers/ci` action, executes the `postCreateCommand`, and verifies .NET, Node, npm, Docker and EF CLI versions inside the container. No container image is published and no SQL Server is started by this job. This check complements (but does not replace) the existing exact-commit full-solution build and disposable SQL Server acceptance. No persistent Codespace is created by CI; its owner must create a Codespace explicitly to get a live interactive terminal.
