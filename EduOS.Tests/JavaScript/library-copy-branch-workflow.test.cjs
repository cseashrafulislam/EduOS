const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../..', name), 'utf8');

test('library copy editor sends an active branch and provides paged campus-scoped branch selection', () => {
    const view = read('EduOS.App/Views/Library/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/library/index.js');
    const api = read('EduOS.App/Controllers/Api/LibraryController.cs');
    const service = read('EduOS.Service/Services/Library/LibraryService.Circulation.cs');
    assert.match(view, /id="copyBranch"[^>]*required/);
    assert.match(view, /id="copyBranchSearch"/);
    assert.match(js, /\/api\/library\/copy-branches\?/);
    assert.match(js, /libraryBranchId: branchId/);
    assert.match(js, /chooseCopyBranch\(copy\.libraryBranchId\)/);
    assert.match(js, /sequence !== branchRequest/);
    assert.match(api, /\[HttpGet\("copy-branches"\)\]/);
    assert.match(service, /campuses\.Contains\(x\.CampusId\)/);
    assert.match(service, /Skip\(\(int\)offset\)\.Take\(pageSize\)/);
    new Function(js);
});
