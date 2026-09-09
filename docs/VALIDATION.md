# Validation results

Validation date: 8 September 2026.

## Passed

| Check | Result |
| --- | --- |
| Database integration executable build | Succeeded against the production data, entity, service, DTO, hub, and selected controller sources |
| EF-created MariaDB schema | All 37 integration checks passed |
| SQL-created MariaDB schema | All 37 integration checks passed |
| Pomelo reverse engineering | Generated all five entities and AppDbContext from MariaDB |
| Repeated Pomelo reverse engineering | Generated model was unchanged; custom partial files were preserved |
| WPF client Release build | Succeeded with Windows targeting enabled; two existing warnings |
| Runtime raw SQL/provider-remnant scan | No Npgsql, raw SQL command execution, old initializer, or legacy migration files remain in server source |

Database checks ran against MariaDB 11.8.3, using the actual Pomelo 9.0.0 provider and EF Core 9.0.17. The SDK was .NET 10.0.400. The database was real, not EF InMemory or SQLite.

The 37 checks cover registration and generated IDs, password login, phone leading zeroes, Unicode search and storage, UTC date materialization, last-seen bulk update, case-insensitive uniqueness, private and group creation, group roles and permissions, membership changes, message loading and visibility, contacts, startup preserving hidden conversations, repeat initialization, user deletion with cascading foreign keys, retention of unrelated data, and transaction rollback.

## Build and runtime limits

The full server build stopped at the existing `SixLabors.ImageSharp` 4.1.1 package's license check. Its diagnostic requests `SixLaborsLicenseKey`, `SixLaborsLicenseFile`, or `sixlabors.lic`. No license was supplied, and this requirement was not disabled.

Provide the required license through your normal build configuration, then build the server, for example:

```powershell
dotnet build NovaChat.Server -c Release -p:SixLaborsLicenseFile=C:/private/sixlabors.lic
```

The database test executable links the actual persistence/service sources directly. It excludes the application host and the three controllers that reference image processing: `ChatController`, `UserController`, and `ChatMediaController`. Its success is not a full server build or HTTP endpoint test.

WPF compiled on Linux with `EnableWindowsTargeting=true`; the desktop UI was not launched. Existing warnings were CS8619 in live refresh and CS0414 for an unused avatar-hook field. The linked server-source build also reports existing nullable warnings in admin private-chat projections.

The Bash scaffolding script was exercised against MariaDB. The PowerShell script and native Visual Studio Package Manager Console command were reviewed but not executed in a Windows/PowerShell environment.

No source database import, deployed application, browser UI, media upload, or multi-instance concurrency test was performed.

## Repeat the checks

Use a separate, empty MariaDB database whose name starts with `novachat_test_`. The checks write test users, conversations, and messages. They refuse a populated database and do not delete the database afterward.

Windows PowerShell:

```powershell
$env:NOVACHAT_TEST_CONNECTION = 'Server=localhost;Port=3306;Database=novachat_test_run1;User ID=YOUR_TEST_DB_USER;Password=YOUR_TEST_DB_PASSWORD;'
$env:NOVACHAT_TEST_SERVER_VERSION = '11.8.3'
dotnet run --project tests/NovaChat.DatabaseChecks -c Release
```

Linux/macOS:

```bash
export NOVACHAT_TEST_CONNECTION='Server=localhost;Port=3306;Database=novachat_test_run1;User ID=YOUR_TEST_DB_USER;Password=YOUR_TEST_DB_PASSWORD;'
export NOVACHAT_TEST_SERVER_VERSION='11.8.3'
dotnet run --project tests/NovaChat.DatabaseChecks -c Release
```

The test account must be able to create the fresh schema, or you can pre-create its tables with `database/mariadb-schema.sql`. Use a new empty database name for each run.

To build the WPF project from a non-Windows SDK environment:

```text
dotnet build NovaChat.Client -c Release -p:EnableWindowsTargeting=true
```
