'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Facility parts use dedicated tenant/module/role-gated Inventory API, never manual source spoofing', () => {
    const api = read('EduOS.App/Controllers/Api/InventoryMovementsActions.cs');
    const service = read('EduOS.Service/Services/Inventory/InventoryMovementFacilityParts.cs');
    const validation = read('EduOS.Service/Services/Inventory/InventoryMovementValidation.cs');
    assert.match(api, /HttpPost\("facility-issues\/\{facilityRequestId:long\}"\), RequireModule\("FACILITIES"\)/);
    assert.match(api, /HttpPost\("facility-issues\/\{reference:guid\}\/void"\), RequireModule\("FACILITIES"\)/);
    assert.match(service, /db\.CurrentTenantId != user\.TenantId/);
    assert.match(service, /source\.Request\.StateCode != "Approved"/); // Checked before new draft.
    assert.match(service, /normalized\.SourceType = "FacilityWorkOrder"/);
    assert.match(service, /normalized\.SourceId = source\.Order\.Id/);
    assert.match(service, /movement\.CreatedBy\.Value == user\.UserId/);
    assert.match(validation, /request\.SourceId\.HasValue/);
});

test('Inventory draft form uses server-resolved work-order reference and actual-cost posting', () => {
    const markup = read('EduOS.App/Views/InventoryMovements/_Create.cshtml');
    const script = read('EduOS.App/wwwroot/js/inventory/movements-form.js');
    assert.match(markup, /IsCurrentTenantModuleAvailableAsync\("FACILITIES"\)/);
    assert.match(markup, /value="FacilityWorkOrder"/);
    assert.match(markup, /id="movementFacilityRequest"/);
    assert.match(script, /facilityRequestId = Number\(el\('movementFacilityRequest'\)\.value\)/);
    assert.match(script, /facility \? 'movements\/facility-issues\/' \+ facilityRequestId : 'movements'/);
    assert.match(script, /lines: editor\.lines\.map\(\(\{ inventoryItemId, quantity, lotOrBatchNo \}\)/);
    assert.match(script, /el\('movementTo'\)\.disabled = type === 'Issue' \|\| facility/);
});

test('Facilities read-through is paged, tenant scoped, immutable-posting aware and reports absent GL', () => {
    const readModel = read('EduOS.Service/Services/Facilities/FacilityWorkflowParts.cs');
    const posting = read('EduOS.Service/Services/Inventory/InventoryMovementPost.cs');
    assert.match(readModel, /VisibleRequests\(\)\.AnyAsync/);
    assert.match(readModel, /\.Skip\(\(page - 1\) \* pageSize\)\.Take\(pageSize\)/);
    assert.match(readModel, /SourceType == "FacilityWorkOrder"/);
    assert.match(readModel, /SourceType == "InventoryMovement"/);
    assert.match(readModel, /"Unlinked"/);
    assert.match(readModel, /reversal == null \? cost : 0m/);
    assert.match(posting, /FacilityIssuePostingAllowedAsync/);
});
