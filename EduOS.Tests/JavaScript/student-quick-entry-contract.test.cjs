const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const file = name => fs.readFileSync(path.join(root, name), 'utf8');

test('running student entry uses existing academic enrollment and transactional guardian link', () => {
    const source = file('EduOS.Service/Services/Students/StudentQuickEntryService.cs');
    assert.match(source, /ExecuteInTransactionAsync/);
    assert.match(source, /IsolationLevel\.ReadCommitted/);
    assert.match(source, /academicEnrollments\.EnrollAsync/);
    assert.match(source, /if \(!enrolled\.Success[^\n]+/);
    assert.match(source, /throw new RejectEntry/);
    assert.match(source, /StudentPersonLinks\.Add/);
    assert.match(source, /StudentGuardians\.Add/);
    assert.match(source, /SubscriptionActiveCapacityPolicy\.CanAddStudentsAsync/);
    assert.match(source, /ClientRequestId == request\.ClientRequestId/);
    assert.match(source, /db\.CurrentTenantId != user\.TenantId/);
    assert.doesNotMatch(source, /MAX\s*\(\s*\)\s*\+\s*1/i);
});
test('optional Guardian tab and single Save Student action keep admin authorization', () => {
    const page = file('EduOS.App/Views/Students/Create.cshtml');
    const js = file('EduOS.App/wwwroot/js/students/quick-entry.js');
    const api = file('EduOS.App/Controllers/Api/RunningStudentsController.cs');
    const nav = file('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(page, /id="runningGuardianTab"/);
    assert.match(page, /id="runningAddGuardian"/);
    assert.match(page, /id="runningSave"/);
    assert.match(js, /guardian: guardianEnabled\(\)/);
    assert.match(js, /sameSession\(\)/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /if \(state\.saving/);
    assert.match(js, /state\.curricula\.length === 1/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(nav, /\/Students\/Create/);
});
