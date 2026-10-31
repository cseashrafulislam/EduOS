# EduOS V3 Inventory & Store — Physical Stock Count

Scope: `Ashraful` V3 Inventory track. Existing V1 Finance and V2 Library, LMS, Hostel, Transport remain untouched. A physical count produces **canonical posted InventoryMovements**; there is no independent editable stock-balance source.

## Workflow

1. A scoped StoreKeeper, InventoryManager, Principal or TenantAdmin starts a count at an accessible, active stock location. Select 1–100 unique stock-tracked item/lot combinations, provide a reason of 10–800 characters and a client request UUID. Book quantity, book valuation and reservation holds are captured from the *posted* inventory ledger inside a serializable transaction. Locations and items are validated against the current tenant and campus access. The snapshot is immutable in SQL. A new/zero-book item or lot is valid.
2. The maker records every counted quantity, **including explicit zero**, then submits with a fresh `rowVersion` and unique submission UUID. A blind draft does not disclose book/hold/variance values through list, detail or CSV; counts become visible only after submission, when the measurement is locked. The maker can submit their own draft but cannot submit someone else's draft.
3. A *different* TenantAdmin, Principal or InventoryManager reviews the submitted count. A checker cannot also be the maker or submitting operator. A rejection records reason/actor/time, never creates stock movements, and cannot be approved later.
4. For approval, the reviewer chooses one of the explicitly supported policies:

   | Policy | Posted signed variance |
   | --- | --- |
   | `Automatic` | `Counted − Book` |
   | `ManualFinal` | Explicit final signed variance for **every** line |
   | `AutoPlusManual` | `(Counted − Book) + signed correction` |

   Each deviation from the suggested variance **or** from the existing book unit cost needs its own reason (10–800 characters); a general approval/rejection reason is always required. Positive variance uses the original book average unit cost unless an approved positive unit cost is supplied. When book valuation is zero, the checker **must** supply a positive unit cost for surplus receipt, including in Automatic mode. Negative variance uses the stock ledger's current weighted-average cost; it cannot consume someone else's reservation.
5. Before posting, the reviewer checks the actual current item/lot quantity and valuation against the snapshot, plus any newer ledger movements. A stale count returns HTTP `409` and must be restarted; a failed policy, inaccessible campus/item, negative resulting stock or reserved shortage cannot partially post. Every nonzero line creates exactly one posted `Receipt` or `Issue` with `SourceType=PhysicalStockCount`, `SourceId=count.Id`, a unique movement UUID and a line-level posted movement link. The whole approval and all adjustments commit or roll back together under SQL Server **Serializable** isolation. Zero-variance lines create no redundant stock movement.
6. After decision, the count is historical evidence with checker/maker identities, snapshot, measured quantities, suggested/final variance, reasons, linked movement UUIDs and CSV export. Normal approved compensating movement reversal remains the stock correction path; book snapshots are never rewritten.

## Endpoints and authorization

`GET /api/inventory/movements/counts` (`locationId`, `state`, `page`, `pageSize`), `GET /api/inventory/movements/counts/{uuid}`: Inventory-entitled inventory read roles. Page size ≤100. Draft blind fields are redacted.

`POST /api/inventory/movements/counts`: Inventory-entitled inventory write roles, body `clientRequestId`, `locationId`, `blind`, `reason`, `lines:[{inventoryItemId,lotOrBatchNo}]`.

`POST /api/inventory/movements/counts/{uuid}/submit`: inventory write roles, `clientRequestId`, `rowVersion`, all `lines:[{lineNo,countedQuantity}]`.

`POST /api/inventory/movements/counts/{uuid}/review`: TenantAdmin/Principal/InventoryManager, `clientRequestId`, `rowVersion`, `decision` (`Approve`/`Reject`), `varianceMode`, `reason`, optional `corrections:[{lineNo,quantity,receiptUnitCost,reason}]`. Automatic permits cost-only corrections with `quantity=0`, not quantity overrides. The API and Razor UI retain anti-forgery, rate limiting, no-store responses and `INVENTORY` module entitlement.

The first successful request of each phase stores a request UUID and a canonical payload fingerprint. Identical retries return committed evidence. Changed payload on the same UUID, a changed rowversion or a terminal state returns `409`. Tenant and campus visibility are rechecked inside every write transaction; a direct guessed UUID from another tenant returns `404`.

## Database and rollback

Migration `20261031180000_V3InventoryPhysicalStockCounts` creates only `InventoryPhysicalCounts` and `InventoryPhysicalCountLines`. Both have tenant-leading composite foreign keys, rowversion, tenant/client-request uniqueness, unique item/lot per count, bounded quantity checks and SQL triggers preventing changes to saved book snapshots. It does **not** rewrite InventoryMovements, legacy reservations, V1/V2 history, item balances or existing procurement states. The Down path refuses to delete history unless both new tables are empty.

## Acceptance and remaining gates

- SQL integration coverage is added to `SqlServerV3InventoryTransferLifecycleTests`: blind disclosure, zero stock, maker/checker separation, replay, stale snapshots, rejection, three variance modes, valuation, reservation holds, tenant/role IDOR. The existing SQL smoke workflow explicitly includes this test class.
- JS contracts verify the bilingual UI, immutability, three policies, CSV report and safe DOM rendering; browser JS passes syntax checks.
- A full-solution Release build, EF model-snapshot drift, fresh/repeat migration, SQL integration and publish gates must pass at the **exact pushed commit** in GitHub Actions. A local Node pass alone is not sufficient evidence.
- Institutional browser/mobile accessibility UAT, real-store reconciliation, production-like load/lock timings, operational backup/restore and explicit deployment go/no-go remain separate acceptance work. Do not claim V3 or Inventory 100% production-ready from code alone.
