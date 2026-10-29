const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const share = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/learning/certificate-sharing.js'), 'utf8');
const certificates = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/learning/certificates.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/Views/Learning/Certificates.cshtml'), 'utf8');

test('LMS guardian consent UI parses and never renders untrusted certificate or guardian HTML', () => {
    assert.doesNotThrow(() => new vm.Script(share));
    assert.doesNotMatch(share, /innerHTML|insertAdjacentHTML|document\.write|localStorage|sessionStorage/);
    assert.match(share, /textContent=choice\.guardianName/);
    assert.match(share, /textContent=grant\.guardianName/);
});

test('grant and revoke require explicit consent, CSRF and a bound tenant session', () => {
    assert.match(share, /authSessionReference/);
    assert.match(share, /authTenantId/);
    assert.match(share, /EduOSSessionTransitionPending/);
    assert.match(share, /window\.confirm\(text\.share\)/);
    assert.match(share, /window\.confirm\(text\.revoke\)/);
    assert.match(share, /RequestVerificationToken=token/);
    assert.match(share, /clientRequestId:crypto\.randomUUID\(\)/);
    assert.match(share, /rowVersion:grant\.rowVersion/);
});

test('guardian read and print use separate server-authorized routes, never student routes', () => {
    assert.match(certificates, /\/api\/lms\/certificate-sharing\/mine/);
    assert.match(certificates, /\/LmsGuardianCertificates\/Print\?reference=/);
    assert.match(certificates, /lms-certificate-selected/);
    assert.match(view, /@if \(studentMode\)/);
    assert.match(view, /data-guardian-mode=/);
    assert.match(share, /cache:'no-store'/);
    assert.match(share, /credentials:'same-origin'/);
});
