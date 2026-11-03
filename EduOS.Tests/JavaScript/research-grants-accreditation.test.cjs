const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const grants = read('EduOS.App/wwwroot/js/research/research-grants.js');
const accreditation = read('EduOS.App/wwwroot/js/research/accreditation.js');
const grantView = read('EduOS.App/Views/ResearchGrants/Index.cshtml');
const accreditationView = read('EduOS.App/Views/Accreditation/Index.cshtml');
const help = read('EduOS.App/wwwroot/js/platform/page-context-help.js');

test('research grant UI scopes session, clears stale details and retains retry keys', () => {
    assert.match(grants, /sessionValid\(\)/);
    assert.match(grants, /detailGeneration\+\+/);
    assert.match(grants, /generation !== detailGeneration/);
    assert.match(grants, /clientRequestId: actionRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(grants, /'X-EduOS-Tenant-Id'/);
    assert.doesNotMatch(grants, /\.innerHTML\s*=/);
    assert.match(grantView, /Html\.AntiForgeryToken\(\)/);
});

test('accreditation UI retains version, reviewer and report scope without HTML injection', () => {
    assert.match(accreditation, /framework\.rowVersion/);
    assert.match(accreditation, /declaresNoConflict/);
    assert.match(accreditation, /reportGeneration\+\+/);
    assert.match(accreditation, /accCsv.*removeAttribute\('href'\)/);
    assert.match(accreditation, /X-EduOS-Session-Reference/);
    assert.doesNotMatch(accreditation, /\.innerHTML\s*=/);
    assert.match(accreditationView, /Html\.AntiForgeryToken\(\)/);
    assert.match(help, /'\/accreditation\/index':/);
    assert.match(help, /'\/researchgrants\/index':/);
});
