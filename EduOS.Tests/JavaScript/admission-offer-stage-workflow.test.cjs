const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '../../EduOS.App');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const core = read('wwwroot/js/admissions/offers-core.js');
const search = read('wwwroot/js/admissions/offers-search.js');
const view = read('Views/Admissions/Offers.cshtml');
test('stage-specific panels are accessible and remain in the existing authorized workflow', () => {
 for (const id of ['offerIssueSection', 'offerAcceptanceSection', 'offerEnrollmentSection']) {
  assert.match(view, new RegExp('id="' + id + '"'));
  assert.match(core, new RegExp(id));
 }
 assert.match(core, /state === 3 \? 'enroll'/);
 assert.match(core, /stage === 'accept'/);
 assert.match(core, /stage === 'issue'/);
 assert.match(core, /section\.hidden = !visible/);
 assert.match(view, /id="offerStageGuidance"[^>]*aria-live="polite"/);
 assert.match(core, /Review the application|Complete the admission review/);
});
test('search has Bengali labels, accessible selection and localized pagination', () => {
 assert.match(search, /toLocaleString\('bn-BD'\)/);
 assert.match(search, /অফার খুলুন/);
 assert.match(search, /aria-label/);
 assert.match(search, /পৃষ্ঠা/);
 assert.match(search, /latestSearch/);
 assert.match(search, /pageSize: String\(pageSize\)/);
});
test('server-authoritative refresh preserves applicant-scoped data and CSRF', () => {
 assert.match(core, /currentReference\(reference\)/);
 assert.match(core, /Promise\.all/);
 assert.match(core, /RequestVerificationToken/);
 assert.match(core, /credentials: 'same-origin'/);
 assert.match(core, /offerIdentityChecked/);
 assert.match(core, /offerEnrollButton/);
});
