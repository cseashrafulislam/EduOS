const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../EduOS.App');
const view = fs.readFileSync(path.join(root, 'Views/Library/Circulation.cshtml'), 'utf8');
const script = fs.readFileSync(path.join(root, 'wwwroot/js/library/circulation.js'), 'utf8');
const controller = fs.readFileSync(path.join(root, 'Controllers/Api/LibraryController.cs'), 'utf8');

test('staff direct issue workspace selects eligible students, books and paged copies', () => {
    for (const id of ['directIssueStudent', 'directIssueBook', 'directIssueCopy',
        'directIssueDueDate', 'directIssueForm', 'directIssueStudentMore',
        'directIssueBookMore', 'directIssueCopyMore']) {
        assert.match(view, new RegExp('id="' + id + '"'));
    }
    assert.match(script, /\/api\/library\/reservations\/eligible-students\?/);
    assert.match(script, /\/api\/library\/catalog\?/);
    assert.match(script, /\/available-copies\?/);
    assert.match(script, /studentReference:student/);
    assert.match(script, /hasNextPage/);
    assert.match(script, /Number\(copy\.state\)!==1/);
});

test('direct issue uses CSRF, stable retry key and canonical circulation endpoint', () => {
    assert.match(controller, /HttpPost\("issues"\)/);
    assert.match(script, /headers\.RequestVerificationToken=token/);
    assert.match(script, /clientRequestId:direct\.key/);
    assert.match(script, /studentReference,bookCopyId:copyValue,dueDate/);
    assert.match(script, /await api\('\/api\/library\/issues','POST'/);
    assert.match(script, /newIssueKey\(\);message\('Copy issued successfully\.'/);
    assert.match(view, /asp-action="Reservations"/);
});
