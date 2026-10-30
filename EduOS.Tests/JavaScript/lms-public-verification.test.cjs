const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '../../EduOS.App');
const student = fs.readFileSync(path.join(root, 'wwwroot/js/learning/certificate-public-sharing.js'), 'utf8');
const publicPage = fs.readFileSync(path.join(root, 'wwwroot/js/learning/lms-public-verification.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'Views/Learning/Certificates.cshtml'), 'utf8');
const publicView = fs.readFileSync(path.join(root, 'Views/LmsPublicVerification/Index.cshtml'), 'utf8');

test('LMS public verification scripts parse and keep token out of persistent browser storage', () => {
    assert.doesNotThrow(() => new vm.Script(student));
    assert.doesNotThrow(() => new vm.Script(publicPage));
    for (const code of [student, publicPage]) {
        assert.doesNotMatch(code, /localStorage|sessionStorage|document\.cookie|innerHTML|document\.write/);
    }
});

test('learner controls require CSRF, session continuity, fresh server status and revocation', () => {
    assert.match(student, /authSessionReference/);
    assert.match(student, /authTenantId/);
    assert.match(student, /EduOSSessionTransitionPending/);
    assert.match(student, /RequestVerificationToken/);
    assert.match(student, /lms-certificate-selected/);
    assert.match(student, /lms-certificate-cleared/);
    assert.match(student, /pagehide',scrub/);
    assert.match(student, /rowVersion:state\.rowVersion/);
    assert.match(student, /\/disable/);
    assert.match(view, /lmsCertificatePublicSharing/);
    assert.match(view, /certificate-public-sharing\.js/);
});

test('anonymous verifier uses URL fragment and POST body, never token in request URL', () => {
    assert.match(student, /url\.hash='token='/);
    assert.match(publicPage, /location\.hash/);
    assert.match(publicPage, /history\.replaceState/);
    assert.match(publicPage, /method:'POST'/);
    assert.match(publicPage, /credentials:'omit'/);
    assert.match(publicPage, /referrerPolicy:'no-referrer'/);
    assert.match(publicPage, /body:JSON\.stringify\(\{token\}\)/);
    assert.match(publicView, /name="referrer" content="no-referrer"/);
    assert.match(publicView, /role="status"/);
});
