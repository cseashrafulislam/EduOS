'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('occupied transfer UI collects both concurrency versions, durable ID and audit reason', () => {
    const js = read('EduOS.App/wwwroot/js/hostel-operations/occupied-transfer.js');
    assert.match(js, /crypto\.randomUUID\(\)/);
    assert.match(js, /sourceRowVersion: pending\.rowVersion/);
    assert.match(js, /destinationBedRowVersion, reason, financeClearanceId/);
    assert.match(js, /approved Finance clearance ID/);
    assert.match(js, /clientRequestId: requestId/);
    assert.match(js, /RequestVerificationToken = token/);
    assert.match(js, /encodeURIComponent\(pending\.id\)/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});

test('occupied transfer is discoverable for active residents; charged stays require clearance', () => {
    const listing = read('EduOS.App/wwwroot/js/hostel-operations/index.js');
    const service = read('EduOS.Service/Services/Hostel/HostelService.OccupiedTransfer.cs');
    const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    assert.match(listing, /!future && item\.startDate < today\(\)/);
    assert.match(listing, /hostelOccupiedTransfer\?\.begin\(item\)/);
    assert.match(service, /System\.Data\.IsolationLevel\.Serializable/);
    assert.match(service, /PreviousAllocationId = source\.Id/);
    assert.match(service, /HasPostedActivityAsync/);
    assert.match(service, /ConsumeHostelFinanceClearanceAsync/);
    assert.match(service, /_commandReceipts\.ReplayAsync/);
    assert.match(service, /_commandReceipts\.RecordAsync/);
    assert.match(view, /id="hostelOccupiedTransferPanel"/);
    assert.match(view, /hostel-operations\/occupied-transfer\.js/);
});

test('finance clearances support campus policy, explicit override, checker and safe UI text', () => {
    const js = read('EduOS.App/wwwroot/js/finance/hostel-clearances.js');
    const view = read('EduOS.App/Views/FinanceAdjustments/HostelClearances.cshtml');
    const operations = read('EduOS.App/wwwroot/js/hostel-operations/index.js');
    assert.match(js, /billing-policy/);
    assert.match(js, /allowManualOverride/);
    assert.match(js, /manualRentCredit/);
    assert.match(js, /manualDepositRefund/);
    assert.match(js, /reconciliationNote/);
    assert.match(js, /RequestVerificationToken = token/);
    assert.match(view, /Daily prorated estimate|daily prorated estimate/i);
    assert.match(view, /Fixed monthly/i);
    assert.match(view, /Manual · enter settlement/);
    assert.match(operations, /financeClearanceId: clearanceId/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});
