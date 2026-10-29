const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const page = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/learning/certificates.js'), 'utf8');
const register = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/learning/course-completions.js'), 'utf8');

test('LMS certificate browser code is parseable and never inserts raw student HTML', () => {
    assert.doesNotThrow(() => new vm.Script(page));
    assert.doesNotThrow(() => new vm.Script(register));
    assert.doesNotMatch(page, /innerHTML|insertAdjacentHTML|document\.write|localStorage|sessionStorage/);
});

test('certificate print links to a fresh authenticated server-side check and exact session', () => {
    assert.match(page, /authSessionReference/);
    assert.match(page, /authTenantId/);
    assert.match(page, /EduOSSessionTransitionPending/);
    assert.match(page, /\/Learning\/CertificatePrint\?reference=/);
    assert.match(page, /if\(!currentSession\(\)\|\|seq!==state\.detailRequest\)/);
    assert.match(page, /print\.removeAttribute\('href'\)/);
});

test('revocation requires reason and row version; reissue uses a fresh request GUID', () => {
    assert.match(page, /rowVersion:row\.rowVersion/);
    assert.match(page, /reason:reason\.trim\(\)/);
    assert.match(page, /crypto\.randomUUID\(\)/);
    assert.match(register, /Issue \/ Reissue certificate/);
});

test('certificate readback is bounded and all API calls disable HTTP caching', () => {
    assert.match(page, /pageSize=20/);
    assert.match(page, /cache:'no-store'/);
    assert.match(page, /credentials:'same-origin'/);
    assert.match(page, /AbortController/);
});

test('certificate audit history is rendered without injecting raw HTML', () => {
    assert.match(page, /\/history/);
    assert.match(page, /entry\.actorUserId/);
    assert.match(page, /td\(tr,t\(key\)\)/);
});

test('changing institute, BFCache navigation and filter errors scrub certificate PII and print links', () => {
    assert.match(page, /new MutationObserver/);
    assert.match(page, /data-auth-session-reference/);
    assert.match(page, /data-auth-tenant-id/);
    assert.match(page, /pagehide',clearSensitive/);
    assert.match(page, /lms-certificate-cleared/);
    assert.match(page, /lmsCertificateStudent','lmsCertificateTitle'/);
    assert.match(page, /state\.rows=\[\];state\.pages=1;renderRows\(\)/);
});
