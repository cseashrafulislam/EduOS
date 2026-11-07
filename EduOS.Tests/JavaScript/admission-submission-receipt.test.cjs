const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/admissions/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Admissions/Index.cshtml'), 'utf8');

test('successful intake submission provides a focusable bilingual receipt before resetting form', () => {
  assert.match(view, /id="admissionSubmissionReceipt"[^>]*role="status"[^>]*aria-live="polite"[^>]*tabindex="-1"[^>]*hidden/);
  assert.match(js, /showApplicationReceipt\(\{[\s\S]*number: payload\.data\?\.applicationNumber[\s\S]*name: request\.applicantName/);
  assert.ok(js.indexOf('showApplicationReceipt({') < js.indexOf('form.reset();\n            state.pendingRequestId'));
  assert.match(js, /host\.hidden = false;\s*host\.focus\(\)/);
});

test('receipt is privacy-minimal and uses textContent, never HTML interpolation', () => {
  const start = js.indexOf('function showApplicationReceipt(receipt)');
  const end = js.indexOf('async function loadApplications()', start);
  const receipt = js.slice(start, end);
  assert.ok(start > 0 && end > start);
  assert.match(receipt, /description\.textContent = value/);
  assert.doesNotMatch(receipt, /innerHTML|primaryMobile|guardianMobile|dateOfBirth|email/);
  assert.match(receipt, /window\.print\(\)/);
});

test('failed application requests preserve form and request key for retry', () => {
  assert.match(js, /if \(!response\.ok \|\| !payload\?\.success\) \{[\s\S]*?showAlert\('danger', i18n\.createFailed\);[\s\S]*?return;/);
  assert.match(js, /state\.pendingFingerprint !== null && state\.pendingFingerprint !== fingerprint/);
  assert.match(js, /state\.submitting = true/);
  assert.match(js, /state\.submitting = false/);
});
