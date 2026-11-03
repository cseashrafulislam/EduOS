const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/research/research-proposals.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/ResearchProposals/Index.cshtml'), 'utf8');
const help = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/platform/page-context-help.js'), 'utf8');

test('research UI never inserts protocol or reason as HTML and invalidates stale selection', () => {
    assert.match(js, /body\.textContent = pair\[1\]/);
    assert.match(js, /clearDetail\(\); const generation = detailGeneration/);
    assert.match(js, /generation !== detailGeneration/);
    assert.match(js, /response\.headers\.get\('X-EduOS-Session-Reference'\)/);
    assert.doesNotMatch(js, /\.innerHTML\s*=/);
    assert.match(view, /Html\.AntiForgeryToken\(\)/);
});

test('research workflow has idempotent action retry and two language help', () => {
    assert.match(js, /clientRequestId: actionRetry\.id/);
    assert.match(js, /actionRetry = null; clearDetail\(\); await load\(\)/);
    assert.match(js, /researchConflict/);
    assert.match(help, /'\/researchproposals\/index':/);
    assert.match(help, /গবেষণা প্রস্তাব ও নৈতিক অনুমোদন/);
});
