# Workflow

The diagram below traces a single run end-to-end. The four phases are inside the process; every
boundary the run crosses (SQL Server, network shares, the local workspace, CaseWare Working Papers
via COM, Active Directory, the output file, optional Application Insights) is drawn as an external
node. Dashed edges are boundary crossings; solid edges are control flow inside the process.

```mermaid
flowchart TB
    classDef phase  fill:#dbeafe,stroke:#1d4ed8,stroke-width:1px,color:#0b2a6b
    classDef ext    fill:#fde2e2,stroke:#b91c1c,stroke-width:2px,color:#5b0d0d
    classDef se     fill:#f5f5f5,stroke:#666,stroke-width:1px,color:#222

    Start([Run starts]):::se
    Inputs["Read inputs<br/>(--input-files file<br/>or Input:Files config)"]:::se

    P0["Phase 0 — Resolve file IDs<br/>(single bounded query)"]:::phase
    PrepRoot["Prepare workspace root"]:::se

    P1["Phase 1 — Read FILE-group identifiers<br/>(Parallel.ForEachAsync,<br/>bounded by MaxDegreeOfParallelism;<br/>no DB, no AD)"]:::phase

    CleanRoot["Clean up workspace root"]:::se

    P2["Phase 2 — Resolve staff for<br/>identifiers seen across the run<br/>(single bounded query;<br/>skipped if no identifiers)"]:::phase

    P3["Phase 3 — Map identifiers to staff,<br/>remove support users, write spreadsheet<br/>(in-memory)"]:::phase

    Done([Spreadsheet ready]):::se

    DB[("SQL Server<br/>CaseWareFileManagement<br/>(dbo.CaseWareFilesForApplications,<br/>Lookups.ViewActiveStaff)")]:::ext
    NET[("Network shares<br/>(UNC paths to .ac_ files)")]:::ext
    LOCAL[("Local workspace<br/>OS temp\lr-caseware-fileusers\&lt;guid&gt;")]:::ext
    CW[("CaseWare Working Papers<br/>via PlanteMoran.CaseWare COM<br/>(x64; retries on transient faults)")]:::ext
    AD[("Active Directory<br/>support-team group")]:::ext
    OUT[("Output file system<br/>(.xlsx)")]:::ext
    AI[("Application Insights<br/>optional sink")]:::ext

    Start --> Inputs --> P0 --> PrepRoot --> P1 --> CleanRoot --> P2 --> P3 --> Done

    P0 -. "SQL #1: WHERE Id IN @batchIds" .-> DB

    P1 -. "read .ac_ over UNC" .-> NET
    P1 -. "copy to per-file workspace<br/>(isolated per parallel worker)" .-> LOCAL
    P1 -. "OpenCaseWareFile / ensure protection /<br/>GetAllUsersInSecurityGroup(FILE) / Close" .-> CW

    P2 -. "SQL #2: WHERE Identifier IN @uniqueIds" .-> DB

    P3 -. "load support-team membership<br/>(first call only, cached for the run)" .-> AD
    P3 -. "write spreadsheet,<br/>preserves input order" .-> OUT

    P0 -. "structured logs, all phases" .-> AI
    P1 -. "&nbsp;" .-> AI
    P2 -. "&nbsp;" .-> AI
    P3 -. "&nbsp;" .-> AI
```

## Guarantees this picture enforces

- **At most two SQL queries per run.** Phase 0 hits the database once; Phase 2 hits it once (and is
  skipped if no identifiers were seen). Nothing in the parallel phase touches the database.
- **At most one Active Directory query per run.** The support-team membership is loaded lazily on the
  first call from Phase 3 and cached in the singleton `SupportUserFilter` for the lifetime of the
  process.
- **CaseWare concurrency is exactly bounded by `Processing:MaxDegreeOfParallelism`.** Phase 1 is the
  only place a CaseWare session is opened; the parallel cap is enforced by `Parallel.ForEachAsync`,
  not by chance. Set it to `1` to serialize CaseWare access if concurrent COM sessions misbehave on a
  particular host.
- **Memory is bounded by what the input actually references** — the file IDs in the batch, the
  identifiers seen across all files, and the support-team's members. Never the full size of the
  underlying tables.
- **Per-file isolation.** Each Phase 1 worker copies its `.ac_` into a fresh per-file workspace
  directory (`Path.Combine(workspaceRoot, Guid.NewGuid())`) and gets a fresh dependency-injection
  scope, so each CaseWare session is isolated from every other in-flight session.

## What happens on failure

- **A per-file failure** (CaseWare open, FILE-group read, file-ID not found in the database, etc.)
  is recorded as that file's error and Phase 1 continues with the remaining files. The file still
  produces a row in the spreadsheet with its errors and no users.
- **A per-user failure** (a CaseWare identifier with no matching staff record) is recorded as a
  user-level error on that file and the remaining users on the file are still emitted.
- **AD failure or missing configuration** is logged once at first use; the cached membership becomes
  empty and no users are removed for the rest of the run.
- **`Ctrl+C`** cancels the run cleanly: the workspace root is still torn down, no spreadsheet is
  produced, and the process exits with code `2`.
- **Missing required configuration** at start-up (`ConnectionStrings:CaseWareFileManagement`,
  `CaseWare:LoginUserId`, `CaseWare:LoginUserPassword`, etc.) fails the run before any external
  boundary is crossed, with a clear message and exit code `-1`.
- **Database unreachable at start-up** is detected by a connectivity probe before Phase 1 begins —
  no CaseWare reads are wasted. The failure is logged with the underlying SQL error message and a
  hint about `TrustServerCertificate=True` for on-prem servers; exit code `-1`.
- **A locked file blocks workspace cleanup** (a PDF that antivirus, indexer, or a viewer is holding)
  is retried by Polly with exponential backoff. If the lock persists past the retries, a single
  warning is logged naming the workspace and the underlying reason — no stack trace — and the run
  continues. The leftover directory can be removed manually.
