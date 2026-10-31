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
test('scheduled work can be released by manager with a reason and rowversion', () => {
    assert.match(script, /options\.push\('schedule', 'unschedule'\)/);
    assert.match(script, /Release schedule/);
    assert.match(script, /সময়সূচি বাতিল/);
    assert.match(script, /el\('wfNote'\)\.required = \['reject', 'pause', 'complete', 'verify', 'return', 'unschedule', 'escalate'\]\.includes\(action\)/);
    assert.match(script, /action === 'unschedule' \? 5 : 0/);
    assert.match(script, /selected\.workOrderRowVersion/);
});

test('overdue SLA queue and manual escalation are scoped and bilingual', () => {
    assert.match(view, /id="wfOverdueOnly"/);
    assert.match(script, /q\.set\('overdueOnly', 'true'\)/);
    assert.match(script, /Escalate overdue SLA/);
    assert.match(script, /বিলম্বিত কাজ এস্কেলেট করুন/);
    assert.match(script, /row\.requesterUserId !== currentUserId/);
    assert.match(script, /row\.technicianUserId === currentUserId/);
    assert.match(script, /selected\.workOrderRowVersion/);
});

test('independent verifier can return unsafe completion for correction, with bilingual action and reason', () => {
    assert.match(script, /options\.push\('verify', 'return'\)/);
    assert.match(script, /Return for correction/);
    assert.match(script, /সংশোধনের জন্য ফেরত/);
    assert.match(script, /\['return', 'escalate'\]\.includes\(action\) \? 10/);
    assert.match(script, /'escalate', 'return'\]\.includes\(action\) \? 500/);
    assert.match(script, /actionLabels\[event\.actionCode\]/);
    assert.match(script, /selected\.workOrderRowVersion/);
});
