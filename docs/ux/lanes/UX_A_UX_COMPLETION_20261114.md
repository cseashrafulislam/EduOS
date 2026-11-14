# UX A — Completed implementation handoff (2026-11-14)

**Repository:** `cseashrafulislam/EduOS` • **Branch:** `Ashraful` • **Scope:** 41 canonical sidebar routes (one unified UX program).  
**UX status:** 41 / 41 **UX Done (source implementation scope)**.  
**Final QA:** **DEFERRED** until the whole product UX backlog is complete. UX Done is not acceptance, deployment approval, or a declaration that browser, SQL, role or financial regression tests have passed.

## Scope and route ownership

| Menu group | Routes | UX implementation / ownership |
| --- | --- | --- |
| Platform Dashboard | `/Dashboard/Admin` | 1 route; existing compact bilingual SuperAdmin dashboard, historical scoped QA evidence retained |
| Platform Institutions | `/PlatformTenants/Index`, `/PlatformCatalogAdministration/Index` | 2; lifecycle, audit, code-safe institution type settings, historical scoped QA retained |
| Platform Features | `/PlatformFeatureAdministration/Index`, `/PlatformProductModuleAdministration/Index`, `/PlatformInstitutionModulePresets/Index`, `/PlatformModuleFeatures/Index` | 4; future-plan catalogs, immutable internal Codes, canonical mappings, bilingual UX |
| Platform Subscription | `/SubscriptionPlanAdministration/Index`, `/SubscriptionPaymentHistory/Index`, `/Account/PaymentVerification` | 3; existing price/limits, read-only finance reconciliation, manual payment permissioned approvals |
| Platform Governance | `/LegalDocuments/Index` | 1; legally approved version publication, code type selection, retry-stable unchanged draft |
| Institution Dashboard | `/Dashboard/Index` | 1; existing role-aware institution dashboard, historical scoped QA retained |
| Access | `/TenantUsers/Index`, `/TenantPermissionOverrides/Index` | 2; invitations, system/custom roles, scoped overrides, access history |
| Institution & Smart Setup | `/TenantDomains/Index`, `/TenantTerminology/Index`, `/TenantSettings/Index`, `/SmartSetup/Index`, `/NumberSeries/Index`, `/TenantModules/Index`, `/IntegrationAdministration/Index`, `/InstitutionManagement/Profile`, `/InstitutionManagement/Campuses`, `/InstitutionManagement/Regional`, `/InstitutionManagement/Branding`, `/InstitutionManagement/Subdomain`, `/InstitutionManagement/History` | 13; server-backed configuration, shared six-screen institution view, secure domains/credentials, seed preview/apply, audited number issue |
| Subscription & Usage | `/SubscriptionBilling/Index`, `/UsageHistory/Index` | 2; authorized plan/invoice/history and usage/quota report |
| Work Center | `/CentralWorkCenter/Index` | 1; tenant/campus/role-scoped approval inbox and direct permissioned destinations |
| Academic Setup | `/AcademicManagement/Years`, `/AcademicManagement/Terms`, `/AcademicManagement/Departments`, `/AcademicManagement/Classes`, `/AcademicManagement/Batches`, `/AcademicManagement/Sections`, `/AcademicManagement/Rooms`, `/AcademicManagement/Subjects`, `/AcademicManagement/Curricula`, `/AcademicManagement/Prerequisites`, `/AcademicManagement/Offerings` | 11; shared canonical management view for nine routes plus separate Section Register and Prerequisites; Batches/Sections distinct |

### Completion basis

- **Existing implementations reused, not re-created.** Controllers route every canonical page to a real Razor view; Campus six routes share `InstitutionManagement/Index.cshtml`, Academic routes share their existing authorized views. Current UX is compact and responsive, with one title provided by the shell. Related editor, list, history, and operational UI is owned by its source page rather than a duplicate parallel form.
- **User-facing Code generation.** Creation of academic programme, level/class, batch, subject, curriculum, track, room, section, and offering uses server-generated, retry-stable Codes through canonical services; relevant user entry fields are hidden. Existing explicit-Code integration contracts remain backward-compatible. Campus Code is generated server-side; the canonical institution form does not solicit it. Number Series *Key* and tenant-custom settings *Key* are administrator configuration identifiers, not operational document numbers. Issued document numbers must always come from the concurrency-safe number service.
- **Bilingual and Help.** All 41 routes have canonical English/Bangla Page Help; shared assistant/voice controls remain global rather than reimplemented on each page. UX A table/card structures and major workflows have localized labels, compact spacing and accessible controls. Operational feedback was improved for the Section Register, Institution Manager, payment reconciliation, legal publishing and access role tabs.
- **Critical UX fixes from this implementation pass.** Repaired seven malformed `<thead>` opening tags across Payment Reconciliation, Domains, Number Series and Subscription Billing; repaired Academic Prerequisites. Updated bilingual payment/history/legal screens and removed duplicated payment verification heading/Refresh; improved Campus and Academic section/system-generated Code flows. Reused immutable subscription invoices, approved payments, role grants, audit trails and centralized settings as the single sources of truth.
- **Fast source-level evidence.** `EduOS.Tests/JavaScript/ux-a-source-integrity.test.cjs` checks 41 unique routes, canonical view existence, valid table headers and existing JavaScript assets, server-side authorized controller actions, hidden generated academic Codes and 41 Page Help mappings. **All six focused checks passed** in general CI [run 43571116784](https://github.com/cseashrafulislam/EduOS/actions/runs/43571116784); application release build had succeeded earlier, but this is not full runtime certification. Earlier Dashboard and Institution Management scoped SQL/Chromium QA reports remain linked in `UX_A.md`.
- **Known non-UX gating status.** The **repository-wide JavaScript suite still has failures** (48 in general CI run 43571116784), including tests in other lanes and some older structural contracts for refactored interfaces. Do not interpret the focused six source checks as the entire CI passing. Do not discard or suppress failing tests; investigate and repair during separate Final QA.
- **No production database mutations.** This work only commits source code, comments, tests and documentation to `Ashraful`. The five scheduled UX automations remain **OFF**.

## Deferred independent Final QA register

This handoff intentionally **does not** claim the following have completed for all 41 pages: authenticated browser E2E across roles/tenants/campuses, mobile/tablet/responsive and bn/en snapshots, dark/light, shared Assistant/Voice behavior, SQL Server migrations/data persistence, CSRF/IDOR and state transition races, retry/idempotency under concurrent submissions, accounting/stock/reporting invariants, payment/provider reconciliation, print/export, accessibility, and load/performance checks.

**After all product UX lanes are UX Done:** run existing full CI, repair the remaining failures, perform targeted SQL and Chromium acceptance on an exact SHA, audit source-of-truth and tenant isolation, verify release/rollback, and separately mark Final QA PASS for the actual tested commit and scope. Historical prior SHA PASS is not an automatic PASS for later code changes.
