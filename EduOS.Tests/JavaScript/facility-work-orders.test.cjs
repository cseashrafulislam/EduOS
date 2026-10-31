const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/facilities/work-orders.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/Views/FacilityWorkOrders/Index.cshtml'), 'utf8');

test('facilities workflow is bilingual, mobile and escapes untrusted data', () => {
    assert.match(script, /textContent = value/);
    assert.doesNotMatch(script, /innerHTML/);
    assert.match(view, /table-responsive/);
    assert.match(view, /data-bn=/);
    assert.match(view, /aria-live="polite"/);
    assert.match(view, /col-12 col-xl-7/);
});

test('workflow posts antiforgery and retries the same idempotency command', () => {
    assert.match(script, /RequestVerificationToken = token/);
    assert.match(script, /credentials: 'same-origin'/);
    assert.match(script, /clientCommandId: commandId\(key\)/);
    assert.match(script, /rowVersion:/);
    assert.match(script, /\/history\?page=/);
    assert.match(script, /facilityExport/);
    assert.match(script, /\/dashboard\?/);
    assert.match(script, /wfDashboard/);
    assert.match(view, /Service dashboard/);
});

test('the UI never claims to post Inventory or Finance', () => {
    assert.match(view, /Parts and Finance posting are not enabled here/);
    assert.doesNotMatch(script, /\/api\/inventory\/movements\/post/);
});