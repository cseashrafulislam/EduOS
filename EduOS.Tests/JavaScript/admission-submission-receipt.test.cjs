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

test('options refresh preserves still-authorized academic and campus selections', () => {
  const start = js.indexOf('async function loadOptions()');
  const end = js.indexOf('function fillSelect(', start);
  const refresh = js.slice(start, end);
  assert.match(refresh, /previousSelections = \{[\s\S]*year: valueOf\('academicYearId'\)[\s\S]*term: valueOf\('academicTermId'\)[\s\S]*campus: valueOf\('campusId'\)[\s\S]*level: valueOf\('academicUnitId'\)/);
  assert.match(refresh, /restoreAvailableOption\('academicYearId', previousSelections\.year\)/);
  assert.match(refresh, /restoreAvailableOption\('campusId', previousSelections\.campus\)/);
  assert.match(refresh, /restoreAvailableOption\('academicUnitId', previousSelections\.level\)/);
  assert.match(refresh, /refreshTerms\(\);\s*restoreAvailableOption\('academicTermId', previousSelections\.term\)/);
  assert.match(refresh, /select\.options\]\.some\(option => option\.value === String\(previousValue\)\)/);
});
