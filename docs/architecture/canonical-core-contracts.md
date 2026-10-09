# EduOS.Core canonical contracts — initial consolidation
## Core baseline
- Authoritative entity types live under `EduOS.Core.Entities`, with exactly one persistent owner for a business fact.
- Public workflow DTOs are under their domain-specific `EduOS.Core.DTOs` namespaces; different input, output, list and report DTOs are intentional, not duplicates.
- Never create one entity or repository per screen. A domain entity and a DTO are not interchangeable.
- Public API compatibility is required before retiring used DTO names or legacy workflow services.

## Legacy scaffolding removed
The retired DTO files only contained empty namespaces, excluded types, or simplistic `int Id` / `string Name` placeholders. They had no stable authoritative business contract. Use canonical DTOs in these modules instead:
| Area | Canonical API contract |
|---|---|
| Student | `DTOs.Students.StudentDto` / `DTOs.Student.StudentDirectoryDetailsDto` |
| Teacher and Employee | `DTOs.HR.EmployeeDto` |
| Academic Department | `DTOs.Academic.AcademicDepartmentDto` |
| Academic Section/Batch | `DTOs.Academic.AcademicBatchDto` and enrollment DTOs |
| Course | `DTOs.LMS.CourseDto` |
| Invoice | `DTOs.Finance.StudentInvoiceDto` or `DTOs.SaaS.SubscriptionInvoiceDto` |
| Payment | `DTOs.Finance.StudentPaymentDto` or `DTOs.SaaS.SubscriptionPaymentDto` |
| Subject | `DTOs.Academic.SubjectDto` (empty placeholder removed) |
| Role | `DTOs.Auth.RoleSummaryDto` / `DTOs.Auth.SaveRolePermissionsRequestDto` (excluded old `RoleSetupDto` removed) |

## Outstanding compatibility migrations (not silently deleted)
1. `Entities.Accounting.Account.ParentId` and `ParentAccountId`: first inspect deployed values, backfill canonical parent, update EF migration and dependent consumers; then retire `ParentId`.
2. `Entities.SaaS.Tenant.CustomDomain` and `TenantDomain.HostName`: identify authoritative domain record and migrate/deprecate duplicate editable value safely.
3. The unregistered historical level-service and duplicate level DTO have been retired. `AcademicSetupService` is the authoritative academic-level creation workflow; administrative edit/deactivation requires canonical implementation with concurrency checks.
4. `DTOs.Student` and `DTOs.Students` have distinct directory/management projections. Rename ambiguous output DTOs only with API and Service compatibility tests.
5. `DTOs.Hostel` and `DTOs.Transport` have intentional adapter subclasses still needed by implemented services. Retire only after callers have switched.
6. `DTOs.Admission`, `DTOs.Academic` and `DTOs.LMS` contain compatibility-oriented workflow DTOs whose shape differs from final entities; fix their consumer mappings rather than introducing duplicate entity properties.
7. Financial `PaidAmount` / `DueAmount` and enrollment state flags are denormalized workflow fields; validate derived consistency, concurrency and historical behavior in service/DB before refactoring.

## Verification and migrations
- No entity or database schema was changed in this cleanup.
- No existing API route, service interface, or usable business DTO was changed.
- Validate with .NET 10: `dotnet build EduOS.Core/EduOS.Core.csproj -c Release`, `dotnet build EduOS.slnx -c Release`, `dotnet test EduOS.slnx -c Release`.
- Full-solution failures caused by old service-to-model mappings need an independent migration-safe implementation; Core compiling alone is not evidence of production readiness.

## Automated guard
The CI step `python3 scripts/check_core_contracts.py` fails on empty Core C# files, duplicate public fully qualified types, or repeated public `*Dto` simple names across DTO namespaces. This naming guard does not validate runtime ownership, migrations or business semantics; service/EF integration tests remain mandatory.
