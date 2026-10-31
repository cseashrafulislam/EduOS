const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');

const read = path => readFileSync(resolve(__dirname, '../../', path), 'utf8');
const api = read('EduOS.App/Controllers/Api/V3EnterpriseLedgerTraceController.cs');
const service = read('EduOS.Service/Services/Enterprise/V3EnterpriseProcurementTraceService.cs');
const register = read('EduOS.Service/Services/Enterprise/V3EnterpriseReconciliationRegisterService.cs');
const view = read('EduOS.App/Views/V3EnterpriseTrace/Index.cshtml');
const ui = read('EduOS.App/wwwroot/js/inventory/v3-enterprise-trace.js');

test('purchase commitment trace is read-only and requires tenant admin plus both entitlements', () => {
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api, /RequireModule\("INVENTORY"\).*RequireModule\("FINANCE"\)/s);
    assert.match(api, /HttpGet\("procurement\/\{reference:guid\}"\)/);
    assert.match(api, /HttpGet\("procurement\/\{reference:guid\}\/csv"\)/);
    assert.doesNotMatch(api, /HttpPost\("procurement/);
    assert.match(service, /user\.IsTenantAdmin/);
    assert.match(service, /db\.CurrentTenantId == user\.TenantId/);
    assert.match(service, /TenantId == user\.TenantId/);
    assert.doesNotMatch(service, /SaveChangesAsync|ExecuteUpdateAsync|ExecuteDeleteAsync/);
});

test('reconciliation checks historical source tombstones and negotiated quantity/budget', () => {
    assert.match(service, /ProcurementPurchaseOrderLines\.IgnoreQueryFilters\(\)/);
    assert.match(service, /ProcurementRequisitionLines\.IgnoreQueryFilters\(\)/);
    assert.match(service, /original\.IsDeleted/);
    assert.match(service, /x\.Quantity > original\.Quantity/);
    assert.match(service, /inspected\.Total > source\.Sum/);
    assert.match(service, /Take\(5001\)/);
    assert.match(register, /kind != "procurement"/);
    assert.match(register, /V3EnterpriseProcurementTraceService/);
});

test('bilingual office UI distinguishes PO commitment from stock and Finance posting', () => {
    assert.match(view, /value="procurement"/);
    assert.match(ui, /selectedKind === 'procurement'/);
    assert.match(ui, /d\.requisitionReference/);
    assert.match(ui, /d\.supplierReference/);
    assert.match(ui, /d\.evidenceStatus/);
    assert.match(ui, /ApprovedCommitment/);
    assert.match(ui, /PendingApproval/);
    assert.doesNotMatch(service, /JournalId|InventoryMovementId|AssetBookId/);
});
