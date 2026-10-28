const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = p => fs.readFileSync(path.join(__dirname, '../../', p), 'utf8');
const js = read('EduOS.App/wwwroot/js/public-credentials/transcript-verification.js');
const view = read('EduOS.App/Views/CertificateVerification/Transcript.cshtml');
const register = read('EduOS.App/wwwroot/js/assessment/transcripts.js');
test('anonymous verification is uncached, read-only and DOM-safe', () => {
    assert.match(js, /cache: 'no-store'/);
    assert.doesNotMatch(js, /innerHTML|insertAdjacentHTML|localStorage|sessionStorage/);
    assert.match(js, /\.textContent = data\.issuerName/);
    assert.match(view, /_PublicLayout\.cshtml/);
    assert.match(view, /aria-live="polite"/);
});
test('transcript register links without opener privileges', () => {
    assert.match(register, /\/verify\/transcript\//);
    assert.match(register, /noopener noreferrer/);
    assert.match(register, /row\.reference/);
});
