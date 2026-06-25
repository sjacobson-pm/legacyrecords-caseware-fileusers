# LegacyRecordsCaseWareFileUsers

A Windows console utility that reports the staff assigned to CaseWare engagement files. Given a set
of files (by UNC path or by database ID), it opens each CaseWare file, reads the members of its
`FILE` security group, maps them to staff, removes CaseWare support users, and writes the result to
an Excel spreadsheet grouped by file.

## What it does

For each input file the utility:

1. Resolves the file location — an integer input is looked up in the file-management database to get
   its UNC path; a non-integer input is treated as a UNC path directly.
2. Copies the `.ac_` file into an isolated, per-file local workspace (so files never interfere with
   one another) and opens it with `ICaseWareIntegrationService.OpenCaseWareFile`.
3. Ensures file protection is enabled, then reads the users in the `FILE` security group (retrying on
   transient CaseWare server faults).
4. Maps the CaseWare user identifiers to staff records, then removes any members of the configured
   Active Directory CaseWare support team.
5. Cleans up the workspace.

Finally it writes a single spreadsheet, grouped by file, listing each retained user's **full name**
and **office**, along with any errors encountered.

Processing is resilient: if a file fails, its error is recorded and the remaining files still run; if
an individual user cannot be mapped, that user-level error is recorded and the remaining users still
run.

## Prerequisites

- Windows (the utility targets `net10.0-windows` and runs as a 64-bit process).
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- **CaseWare Working Papers** installed on the host — `PlanteMoran.CaseWare` is an x64 COM
  integration and activates Working Papers at runtime.
- Network access to the two SQL Server databases (see [Configuration](#configuration)) and to the
  Active Directory domain used for support-user removal.
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

- When `--input-files` is omitted, inputs are read from the `Input:Files` configuration array.
- When `--output` is omitted, the path is taken from `Output:FilePath`; if that is empty, a
  timestamped file (`FileUsers_yyyyMMdd_HHmmss.xlsx`) is written to `Output:Directory` (defaulting to
  the current working directory).

Press `Ctrl+C` to cancel a run.

### Example input file

```text
\\fileserver\engagements\acme\acme.ac_
12345
\\fileserver\engagements\globex\globex.ac_
```

## Configuration

Settings are read from `appsettings.json`, overlaid by a git-ignored `appsettings.local.json` for
local secrets. Required values are validated at start-up — the utility fails fast with a clear
message if a required connection string or CaseWare credential is missing.

| Section             | Key                                   | Purpose                                                              |
| ------------------- | ------------------------------------- | -------------------------------------------------------------------- |
| `ConnectionStrings` | `CaseWareFileManagement` *(required)* | Files database (`dbo.CaseWareFilesForApplications`) — ID → UNC path.  |
| `ConnectionStrings` | `CaseWareUsers` *(required)*          | Staff database (`Lookups.ViewActiveStaff`) — identifier → staff.      |
| `CaseWare`          | `LoginUserId` *(required)*            | CaseWare login used to open files.                                   |
| `CaseWare`          | `LoginUserPassword` *(required)*      | CaseWare login password.                                             |
| `CaseWare`          | `ServerFaultExceptionSubstring`       | Substring identifying a transient server fault that should retry.    |
| `CaseWare`          | `RetryRetrievingUsersMaximumAttempts` | Maximum attempts when reading the security group (default `3`).      |
| `ActiveDirectory`   | `DomainName`                          | Domain queried for the support-team group.                           |
| `ActiveDirectory`   | `CaseWareSupportTeamGroupName`        | AD group whose members are removed from the results.                 |
| `Workspace`         | `RootPath`                            | Root for per-file workspaces (defaults to the OS temp folder).       |
| `Output`            | `Directory` / `FilePath`              | Default output location (see [Usage](#usage)).                       |
| `Input`             | `Files`                               | Fallback inputs when `--input-files` is not supplied.                |
| `ApplicationLogging`| —                                     | Serilog output templates, log levels, and Application Insights.       |

> The local-only secrets file `src/LegacyRecordsCaseWareFileUsers/appsettings.local.json` is checked
> in with empty placeholders for you to fill in. It is excluded from source control.

Active Directory settings are optional: if the support group cannot be queried, the error is logged
and processing continues without removing support users.

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
│   ├── Contexts/              # EF Core DbContexts (one per database)
│   ├── Repositories/          # file-path and staff repositories
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
