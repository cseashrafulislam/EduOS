# EduOS final-domain service contract coverage
These are **additive interface contracts**, not working implementations. The existing interfaces, services, API routes and dependency registrations are unchanged. An interface without a registered implementation must not be injected into a controller.

## Contract scope
- Accounting: setup, journal state transitions and tenant-scoped reporting.
- Finance: discounts, fines, approvals and refund disbursements.
- Inventory: item/location setup, movement posting/reversal, derived balances and asset lifecycle.
- LMS: quiz authoring vs learner-safe quiz delivery, attempt submission and live classes.
- Communication and documents: privacy-aware messaging, metadata, scan verification and document lifecycle.
- Student management, assessment administration, library reservation, hostel and transport administration, payroll workflows.

## Implementation requirements (not implemented by this commit)
- Resolve tenant from authenticated server context for every command/query. Cross-tenant IDs are invalid even if GUIDs are guessed; never trust client-supplied tenancy.
- Enforce permissions, role/campus scope, ownership, rowversion checks and status transition tables on the server.
- Journal posting and reversal must be balanced and atomic; period closure must block posting/reversal into a closed period. Posting creates immutable history.
- Movement posting and reversal must be atomic, check stock availability and location compatibility, avoid double-posts and derive balances from movement history.
- Require nonempty client request IDs and enforce uniqueness per tenant for retry-sensitive commands; reconcile existing records and return original responses on retries.
- Refund disbursements require verified payout evidence, audited approvals and linked accounting reversals; recording a request alone must never represent money paid.
- Only an authorized educator may access author quiz data (including IsCorrect). Learners receive QuizCandidateDto only; learner attempts must be restricted to their enrollment and actual attempt count.
- Upload metadata registration and malware scan status are trusted backend operations, not public arbitrary-user write endpoints. Never reveal storage keys or unscanned files.
- Route/bed assignment limits, overlapping assignments, student status, due-date constraints and external access must be enforced in the implementation.
- Use SQL-side filtering/paging for large datasets, rather than loading all records.
- Any entity/DTO with RowVersion must enforce optimistic concurrency before updates. Tenant-scoped audit logs should contain action references, not sensitive identifiers.

## Release
- No schema changes or migrations from these contract declarations; rollback is removal of added files.
- Follow up with **separate end-to-end implementations**, DI wiring, authorization checks, API routes and tests. Existing working behavior must remain backward compatible.
- Release gate: `dotnet restore EduOS.slnx`, `dotnet build EduOS.slnx -c Release`, `dotnet test EduOS.slnx -c Release`. These commands require a .NET 10 environment.
