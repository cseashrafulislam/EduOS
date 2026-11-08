const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const js = read('EduOS.App/wwwroot/js/research/obe-attainment.js');
const view = read('EduOS.App/Views/Accreditation/Obe.cshtml');
const api = read('EduOS.App/Controllers/Api/ObeAttainmentApiController.cs');
const help = read('EduOS.App/wwwroot/js/platform/page-context-help.js');

test('OBE workflow requires actual scoped and authenticated published evidence', () => {
    assert.match(js, /EduOSVerifySessionContext/);
    assert.match(js, /X-EduOS-Tenant-Id/);
    assert.match(js, /X-EduOS-Session-Reference/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /generation\+\+/);
    assert.match(js, /clientRequestId: policyRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(js, /clientRequestId: submitRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(js, /clientRequestId: reviewRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(js, /\/publications/);
    assert.match(api, /HttpGet\("publications"\)/);
    assert.doesNotMatch(js, /\.innerHTML\s*=/);
});

test('OBE UI provides bilingual mapping, review, privacy-aware export and page help', () => {
    assert.match(view, /Html\.AntiForgeryToken\(\)/);
    assert.match(view, /id="obeCloRows"/);
    assert.match(view, /id="obeLinkRows"/);
    assert.match(view, /id="obePublication"/);
    assert.match(view, /id="obeReviewForm"/);
    assert.match(view, /data-o-bn=/);
    assert.match(js, /textContent/);
    assert.match(js, /renderAttainment/);
    assert.match(api, /StateCode != "Approved"/);
    assert.match(api, /Download.*CSV|ApprovedCsv/);
    assert.match(help, /'\/accreditation\/obe\/index':/);
    assert.match(help, /course-grade CLO proxies/);
});
