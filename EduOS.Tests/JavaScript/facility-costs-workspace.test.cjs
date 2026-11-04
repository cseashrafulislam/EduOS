const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Finance Cost Management view exposes bilingual maker-checker and safe source lookups', () => {
    const view = read('EduOS.App/Views/FacilityCosts/Index.cshtml');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const controller = read('EduOS.App/Controllers/FacilityCostsController.cs');
    assert.match(view, /data-maker=/);
    assert.match(view, /data-checker=/);
    assert.match(view, /data-user-id=/);
    assert.match(view, /fcVendorSource/);
    assert.match(view, /fcLabourSource/);
    assert.match(view, /fcDecisionPanel/);
    assert.match(view, /fcVendorCreate/);
    assert.match(view, /fcLabourCreate/);
    assert.match(layout, /FacilityCosts\/Index/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
    assert.match(controller, /RequireModule\("FACILITIES"\)/);
});

test('Finance costs UI validates session and tenant response before rendering private JSON', () => {
    const source = read('EduOS.App/wwwroot/js/facilities/costs.js');
    assert.match(source, /EduOSVerifySessionContext/);
    assert.match(source, /X-EduOS-Session-Reference/);
    assert.match(source, /X-EduOS-Tenant-Id/);
    assert.match(source, /response\.headers\.get/);
    assert.match(source, /if \(!matches\(response\)\)/);
    assert.match(source, /RequestVerificationToken/);
    assert.match(source, /cache: 'no-store'/);
    assert.match(source, /credentials: 'same-origin'/);
    assert.match(source, /new Option\(/);
    assert.match(source, /textContent =/);
    assert.doesNotMatch(source, /\.innerHTML\s*=/);
});

test('Service bill maker/checker and full contra workflow are reachable from the actual UI', () => {
    const source = read('EduOS.App/wwwroot/js/facilities/costs.js');
    const api = read('EduOS.App/Controllers/Api/FacilityVendorServiceBillsApiController.cs');
    assert.match(source, /\/verified-work/);
    assert.match(source, /\/posting-options/);
    assert.match(source, /\/submit/);
    assert.match(source, /\/review/);
    assert.match(source, /\/reversal/);
    assert.match(source, /approve-reversal/);
    assert.match(source, /reject-reversal/);
    assert.match(source, /clientRequestId: pending\.id/);
    assert.match(source, /if \(busy\) return/);
    assert.match(api, /service\.CreateAsync/);
    assert.match(api, /service\.ReviewAsync/);
    assert.match(api, /service\.ReviewReversalAsync/);
});

test('Labour cost picker and review use payroll source instead of re-posting GL', () => {
    const source = read('EduOS.App/wwwroot/js/facilities/costs.js');
    const api = read('EduOS.App/Controllers/Api/FacilityLabourCostsApiController.cs');
    assert.match(source, /eligible-sources/);
    assert.match(source, /payrollEmployeeId: source\.payrollEmployeeId/);
    assert.match(source, /actualWorkMinutes: Number/);
    assert.match(source, /approve-labour/);
    assert.match(source, /reject-labour/);
    assert.match(api, /service\.EligibleSourcesAsync/);
    assert.match(api, /service\.ReviewAsync/);
    assert.match(read('EduOS.Service/Services/Facilities/FacilityLabourCostService.cs'),
        /PayrollRunState\.Posted/);
});
