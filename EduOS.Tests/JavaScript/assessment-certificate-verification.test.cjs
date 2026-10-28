const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = p => fs.readFileSync(path.join(__dirname, '../../', p), 'utf8');
const js = read('EduOS.App/wwwroot/js/public-credentials/certificate-verification.js');
const view = read('EduOS.App/Views/CertificateVerification/Index.cshtml');
const register = read('EduOS.App/wwwroot/js/assessment/certificates.js');
test('public verification is read-only, no-store and PII-free', () => {
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /method|fetch\('/);
    assert.doesNotMatch(js, /innerHTML|insertAdjacentHTML|localStorage|sessionStorage/);
    assert.match(js, /\.textContent = data\.issuerName/);
    assert.match(view, /_PublicLayout\.cshtml/);
    assert.match(view, /aria-live="polite"/);
});
test('issued and revoked certificate records link to the same public verification route', () => {
    assert.match(register, /\/verify\/certificate\//);
    assert.match(register, /row\.reference/);
});
