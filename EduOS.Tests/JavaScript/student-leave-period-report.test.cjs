const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('student leave period report uses encoded filters, safe cells and stale-response protection', () => {
    const script = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/attendance/leave-period-report.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname, '../../EduOS.App/Views/StudentLeaves/Index.cshtml'), 'utf8');
    assert.match(script, /URLSearchParams/);
    assert.match(script, /textContent/);
    assert.match(script, /request !== pending/);
    assert.match(script, /pageSize: '20'/);
    assert.match(script, /api\/student-leaves\/report/);
    assert.match(view, /id="studentLeavePeriodReport"/);
    assert.match(view, /leave-period-report\.js/);
});
