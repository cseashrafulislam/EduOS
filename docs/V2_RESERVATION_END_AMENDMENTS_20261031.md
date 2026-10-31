# V2 Hostel/Transport end-date amendments — engineering handoff

**Branch:** Ashraful. **Scope:** existing Hostel/Transport Finance-cleared bookings only.
This is an additive follow-up to [reservation credit-note posting](V2_FINANCE_RESERVATION_CREDIT_NOTES_20261031.md).
LMS, Library, V1 and V3 feature baselines are unchanged.

## Implemented code paths

| Workflow | Finance operation | Operations | Settlement |
| --- | --- | --- | --- |
| Hostel fixed-term extension within existing 90-day limit | UpcomingEndChange = 4 | Existing extension endpoint plus extend-stay route | Signed automatic/manual/hybrid delta; positive reviewed invoice or negative original-invoice credit; independent no-adjustment attestation |
| Hostel upcoming checkout shortening | UpcomingEndChange = 4 | Existing future shortening endpoint plus shorten-upcoming route | Same Finance-owned adjustment and immutability rules |
| Transport upcoming rider shortening | UpcomingRiderShortening = 4 | Existing future shortening endpoint plus shorten-upcoming route | Same Finance-owned adjustment and immutability rules |

- Old no-charge endpoints remain route aliases. Without a charge or posted Finance exposure, existing free changes still work. With charge/posted exposure, they require a **single-use, separate Finance-approved, exact-date/rowversion/campus-bound clearance**.
- An independent Finance reviewer selects the existing policy calculation or explicitly authorized manual final/hybrid signed correction; adjustments require the same reason and precision as preceding V2 changes.
- Automatic negative settlements use the original posted StudentInvoice and immutable credit-note/paid/unpaid split. They **do not** delete a receipt, initiate cash refunds, create another ledger or redeem credit balances.
- Operations rechecks current dates, ownership, booking limits, route/bed availability, approval and Finance snapshot; it consumes and posts the approved journal with the reservation mutation in its existing serializable transaction.
- The new additive SQL migration widens existing operation/target check constraints from {1,2,3} to {1,2,3,4}; no invoice or payment data is rewritten. Its Down migration deliberately stops if any new operation-4 history exists.

## Verification and acceptance boundary

- JavaScript static/browser-contract regression suite: **316 passed, 0 failed**, including the new end-date Finance and idempotency checks.
- SQL Server test added for charged upcoming Transport shortening, exact-date rejection, independent zero-waiver and single-use consumption. It runs with the disposable SQL acceptance connection in CI.
- Same-commit .NET Release build, full .NET regression, migration smoke, SQL acceptance and CI must be checked on the resulting pushed SHA before marking this increment verified.
- **Not certified by code alone:** institute-specific real-data/reconciliation, vendor/device configuration, staged migration/rollback, p95/p99 load/security/browser-mobile checks, wardens/operators/Finance UAT and owner go/no-go. Open-ended stays and renewals beyond the established fixed-term booking limit are not silently extended.
