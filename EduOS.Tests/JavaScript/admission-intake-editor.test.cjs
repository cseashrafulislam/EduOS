const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/admissions/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Admissions/Index.cshtml'), 'utf8');
test('intake editor preserves invalid edits and provides accessible error navigation', () => {
 assert.match(view, /id="intakeFormEditor" novalidate aria-describedby="intakeEditorHelp"/);
 assert.match(js, /if \(!editor\.checkValidity\(\)\)/);
 assert.match(js, /showIntakeValidation\(invalid\)/);
 assert.match(js, /summary\.setAttribute\('role', 'alert'\)/);
 assert.match(js, /link\.addEventListener\('click', \(\) => field\?\.focus\(\)\)/);
});
test('intake save prevents duplicate concurrent requests and preserves form on conflicts', () => {
 assert.match(js, /if \(state\.savingIntake\) return/);
 assert.match(js, /state\.savingIntake = true/);
 assert.match(js, /state\.savingIntake = false/);
 assert.match(js, /response\.status === 409 \|\| response\.status === 412/);
 assert.match(js, /state\.intakeFingerprint !== null && state\.intakeFingerprint !== fingerprint/);
 assert.match(js, /request\.clientRequestId = valueOf\('intakeFormRequestId'\)/);
});
test('validation covers field and document definitions before writes', () => {
 assert.match(js, /fieldKeys\.has\(key\)/);
 assert.match(js, /documentTypes\.has\(key\)/);
 assert.match(js, /closes <= opens/);
 assert.match(js, /if \(invalid\.length\) \{/);
});

test('intake catalogue supports bilingual searchable statuses and an accessible result count', () => {
 assert.match(view, /id="intakeFormSearch"/);
 assert.match(view, /id="intakeFormState"/);
 assert.match(view, /id="intakeFormCount"[^>]*role="status" aria-live="polite"/);
 assert.match(js, /intakeFormSearch'\)\?\.addEventListener\('input', renderIntakeForms\)/);
 assert.match(js, /const visible = state\.intakeForms\.filter/);
 assert.match(js, /No forms match these filters/);
 assert.match(js, /intakeText\('Published', 'প্রকাশিত'\)/);
});
test('publish and close operations reload canonical options and recover on stale row versions', () => {
 assert.match(js, /controls\.dataset\.mutatingIntake/);
 assert.match(js, /response\.status === 409 \|\| response\.status === 412/);
 assert.match(js, /await loadIntakeForms\(\)/);
 assert.match(js, /await loadOptions\(\)/);
 assert.match(js, /delete controls\.dataset\.mutatingIntake/);
});

test('published-intake refresh preserves current valid selection and entered custom responses', () => {
 assert.match(js, /const previousReference = valueOf\('admissionFormReference'\)/);
 assert.match(js, /state\.options\.openForms\?\.some\(x => x\.reference === previousReference\)/);
 assert.match(js, /selectAdmissionForm\(previousReference === valueOf\('admissionFormReference'\) \? previousCustom : null\)/);
 assert.match(js, /function snapshotAdmissionFields\(\)/);
 assert.match(js, /function restoreAdmissionFields\(values\)/);
 assert.match(js, /selected\.has\(option\.value\)/);
});
test('required multi-select validation and invalid datetime values are safe before submission', () => {
 assert.match(js, /select\[multiple\]\[required\]/);
 assert.match(js, /missingChoices\.length/);
 assert.match(js, /Number\.isFinite\(parsed\.valueOf\(\)\) \? parsed\.toISOString\(\) : ''/);
});
