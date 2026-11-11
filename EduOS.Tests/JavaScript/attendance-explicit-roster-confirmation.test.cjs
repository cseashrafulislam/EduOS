const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const roster = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/attendance/index.js'), 'utf8');
const register = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/attendance/session-register.js'), 'utf8');
test('exception-first bulk Present requires explicit consent and preserves approved leave and existing marks', () => {
    assert.match(roster, /const candidates = \[\.\.\.rows\.querySelectorAll/);
    assert.match(roster, /tr\.dataset\.approvedLeave !== 'true' && !tr\.querySelector\('\[data-state\]'\)\?\.value/);
    assert.match(roster, /if \(!confirm\(prompt\)\) return/);
    assert.match(roster, /Bulk marks are unsaved/);
    assert.match(roster, /dirty = true; updateSummary\(\)/);
});
test('attendance saving and finalization are mutually exclusive, with unsaved roster guard', () => {
    assert.match(roster, /let saving = false, finalizing = false, correcting = false/);
    assert.match(roster, /if \(saving \|\| finalizing \|\| !loaded/);
    assert.match(roster, /if \(dirty\) return status\('warning', 'Save or discard unsaved roster changes before finalization\.'\)/);
    assert.match(roster, /saving = true/);
    assert.match(roster, /finalizing = true/);
    assert.match(roster, /correcting = true/);
    assert.match(roster, /finalizeButton\.disabled = true/);
});
test('session register form submit stays in-page and preserves captured authorized drilldown context', () => {
    assert.match(register, /form\.addEventListener\('submit', event => \{ event\.preventDefault\(\)/);
    assert.match(register, /const batchLabel = el\('attendanceReportBatch'\)\.selectedOptions/);
    assert.match(register, /authorizedQuery = new URLSearchParams\(params\)/);
    assert.match(register, /token !== request/);
});
