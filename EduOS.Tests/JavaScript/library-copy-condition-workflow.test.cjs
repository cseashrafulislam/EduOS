const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Library/LibraryService.Conditions.cs');
const api = read('EduOS.App/Controllers/Api/LibraryController.cs');
const view = read('EduOS.App/Views/Library/Conditions.cshtml');
const js = read('EduOS.App/wwwroot/js/library/conditions.js');

test('physical copy incident is atomic, tenant scoped and does not post speculative fines', () => {
    assert.match(service, /x\.TenantId == tenant && x\.Id == copyId && !x\.IsDeleted/);
    assert.match(service, /StaffCanManageBranchAsync/);
    assert.match(service, /System\.Data\.IsolationLevel\.Serializable/);
    assert.match(service, /MatchesVersion\(open\[0\]\.RowVersion, request\.OpenIssueRowVersion\)/);
    assert.match(service, /CopyConditionAction = "LibraryCopy\.Condition"/);
    assert.match(service, /FineAmount untouched/);
    assert.doesNotMatch(service, /StudentFine\s*\{/);
    assert.match(api, /HttpPost\("copies\/\{copyId:long\}\/condition"\)/);
    assert.match(api, /HttpGet\("copies\/\{copyId:long\}\/condition-history"\)/);
    assert.match(api, /CacheControl = "no-store, private"/);
});

test('condition UI is bilingual, CSRF protected, session-bound and text-only', () => {
    assert.match(view, /data-bn="কপির অবস্থা"/);
    assert.match(view, /data-bn="কারণ \(ব্যক্তিগত তথ্য ছাড়া\)"/);
    assert.match(view, /id="conditionDialog"/);
    assert.match(js, /request-verification-token/);
    assert.match(js, /AbortController/);
    assert.match(js, /data-auth-session-reference/);
    assert.match(js, /td\.textContent =/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    new Function(js);
});
