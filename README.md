# LegacyRecordsCaseWareFileUsers

A Windows console utility that reports the staff assigned to CaseWare engagement files. Given a set
of files (by UNC path or by database ID), it opens each CaseWare file, reads the members of its
`FILE` security group, maps them to staff, removes CaseWare support users, and writes the result to
an Excel spreadsheet grouped by file.

## What it does

A run is structured into four phases so external systems (SQL Server, Active Directory, CaseWare
Working Papers) are touched the minimum number of times regardless of batch size:

1. **Resolve file paths.** Integer inputs are looked up in the file-management database in a single
   bounded query (`WHERE Id IN (…)`); non-integer inputs are treated as UNC paths directly.
2. **Read FILE-group identifiers (parallel, CaseWare only).** Each file is copied into an isolated,
   per-file local workspace, opened with `ICaseWareIntegrationService.OpenCaseWareFile`, has its
   protection ensured, has its `FILE` security group read (retrying on transient CaseWare faults), and
   is closed. Files are processed concurrently up to `Processing:MaxDegreeOfParallelism`. No database
   or Active Directory work happens in this phase.
3. **Resolve staff (single bounded query).** Once Phase 2 has surfaced every CaseWare user identifier
   that actually appears across the run, one bounded staff query (`WHERE CaseWareUserIdentifier IN
   (…)`) fetches just those staff records. If no identifiers were seen, the database is not touched
   at all.
4. **Map and emit.** Identifiers are mapped to staff in memory, the configured Active Directory
   support-team membership is loaded **once** (the result is cached for the whole run), and support
   users are removed from each file's list. A single spreadsheet is then written, grouped by file,
   listing each retained user's **full name**, **office**, and **position**, along with any errors
   encountered.

The spreadsheet preserves input order even when Phase 2 is parallel. Processing is resilient: if a
file fails, its error is recorded and the remaining files still run; if an individual user cannot be
mapped to a staff record, that user-level error is recorded and the remaining users still run. Press
`Ctrl+C` to cancel a run cleanly (no spreadsheet is produced; the process exits with code `2`).

### How it scales

Per run, the utility issues **at most two SQL queries** (file-paths and staff) and **at most one
Active Directory query** (the support-team group), regardless of how many files are in the batch.
The memory footprint is bounded by what actually appears in the input — only the file IDs in the
batch, only the staff identifiers seen across all files, and only the members of the support-team
group — never the size of the underlying tables.

Phase 2 is the wall-clock-dominant phase. Lowering `Processing:MaxDegreeOfParallelism` to `1`
serializes CaseWare access if concurrent COM sessions misbehave on a particular host.

For a single picture of the same flow with every external boundary the run crosses (SQL, network
shares, the local workspace, CaseWare COM, Active Directory, the output file, Application Insights),
see [WORKFLOW.md](WORKFLOW.md).

## Prerequisites

- Windows (the utility targets `net10.0-windows` and runs as a 64-bit process).
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- **CaseWare Working Papers** installed on the host — `PlanteMoran.CaseWare` is an x64 COM
  integration and activates Working Papers at runtime.
- Network access to the CaseWare File Management SQL Server database (see
  [Configuration](#configuration)) and to the Active Directory domain used for support-user removal.
- Access to the Plante Moran VSTS NuGet feed to restore the `PlanteMoran.CaseWare` package.

## Usage

```text
LegacyRecordsCaseWareFileUsers --input-files <path> [--output <path>]
```

| Option                | Description                                                                                                          |
| --------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `-i`, `--input-files` | Path to a text file with one input per line; each line is a UNC path to a `.ac_` file or an integer file ID.         |
| `-o`, `--output`      | Full path of the `.xlsx` spreadsheet to write.                                                                       |

Both options are optional and fall back to configuration:

- When `--input-files` is omitted, the input file is read from `Input:FilePath`; if that is empty,
  inputs are read from the `Input:Files` inline array. (CLI > `Input:FilePath` > `Input:Files`.)
- When `--output` is omitted, the path is taken from `Output:FilePath`; if that is empty, a
  timestamped file (`FileUsers_yyyyMMdd_HHmmss.xlsx`) is written to `Output:Directory` (defaulting to
  the current working directory).

### Example input file

```text
\\fileserver\engagements\acme\acme.ac_
12345
\\fileserver\engagements\globex\globex.ac_
```

## Configuration

Settings are read from `appsettings.json`, overlaid by a git-ignored `appsettings.local.json` for
local secrets. Required values are validated at start-up — the utility fails fast with a clear
message if a required connection string or CaseWare credential is missing. Connectivity to the
CaseWare File Management database is verified before any per-file work begins, so a misconfigured
connection string (for example, a self-signed certificate that needs `TrustServerCertificate=True`)
also fails fast with a clean message and no stack trace.

| Section             | Key                                   | Purpose                                                              |
| ------------------- | ------------------------------------- | -------------------------------------------------------------------- |
| `ConnectionStrings` | `CaseWareFileManagement` *(required)* | CaseWare File Management database — file IDs (`dbo.CaseWareFilesForApplications`) and staff (`Lookups.ViewActiveStaff`). |
| `CaseWare`          | `LoginUserId` *(required)*            | CaseWare login used to open files.                                   |
| `CaseWare`          | `LoginUserPassword` *(required)*      | CaseWare login password.                                             |
| `CaseWare`          | `RetryRetrievingUsersMaximumAttempts` | Maximum attempts when reading the security group (default `3`).      |
| `Processing`        | `MaxDegreeOfParallelism`              | Files processed concurrently; `0` (or less) uses the processor count.|
| `ActiveDirectory`   | `DomainName`                          | Domain queried for the support-team group.                           |
| `ActiveDirectory`   | `CaseWareSupportTeamGroupName`        | AD group whose members are removed from the results.                 |
| `Workspace`         | `RootPath`                            | Root for per-file workspaces (defaults to the OS temp folder).       |
| `Output`            | `Directory` / `FilePath`              | Default output location (see [Usage](#usage)).                       |
| `Input`             | `FilePath`                            | Path to a text file with one input per line; used when `--input-files` is not supplied. |
| `Input`             | `Files`                               | Inline fallback inputs when neither `--input-files` nor `Input:FilePath` is supplied. |
| `ApplicationLogging`| —                                     | Serilog output templates, log levels, Application Insights, and the rolling file sink. |
| `ApplicationLogging:File` | `Path`                          | Rolling log file path; the rolling-interval suffix is appended. When blank, the file sink is not added. |
| `ApplicationLogging:File` | `RollingInterval`               | One of `Infinite`, `Year`, `Month`, `Day`, `Hour`, `Minute` (default `Day`). |
| `ApplicationLogging:File` | `RetainedFileCountLimit`        | Maximum number of rolled files to keep on disk (default `31`).       |

> The local-only secrets file `src/LegacyRecordsCaseWareFileUsers/appsettings.local.json` is checked
> in with empty placeholders for you to fill in. It is excluded from source control.

Active Directory settings are optional: if the support group cannot be queried (or `DomainName` /
`CaseWareSupportTeamGroupName` is blank), the error is logged once at start-up and processing
continues without removing support users.

## Project structure

```text
src/LegacyRecordsCaseWareFileUsers/
├── Program.cs                 # composition root, configuration, logging, CLI
├── CommandLineOptions/        # command line option definitions
├── Services/
│   ├── Interfaces/            # service abstractions
│   └── Implementations/       # input reading, workspace, CaseWare retrieval,
│                              #   staff mapping, support-user filtering, output
├── Data/
│   ├── Contexts/              # EF Core DbContext for the file-management database
│   ├── Repositories/          # CaseWare-files and staff repositories
│   └── Domain/                # database entities
├── Models/                    # input/result models
├── Options/                   # strongly-typed configuration
├── Helpers/, Logging/, Exceptions/
test/unit-tests/               # xUnit test project
```

## Building and testing

```pwsh
dotnet build
dotnet test
```

## Contributing

To contribute to this repository, please see the [contribution guidelines](CONTRIBUTING.md).
