'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(base, relative), 'utf8');
const view = read('EduOS.App/Views/Admissions/Index.cshtml');
const app = read('EduOS.App/wwwroot/js/admissions/index.js');
const css = read('EduOS.App/wwwroot/css/site.css');

test('new admission only shows one clean form with canonical fields and no draft or readiness clutter', () => {
    assert.doesNotMatch(view, /admissionDraftHost|admissionReadinessDisclosure|transcript-draft\.js/);
    assert.match(view, /id="admissionForm" class="admission-application-form" novalidate/);
    assert.match(view, /id="admissionSubmitFeedback" class="d-none" role="alert"/);
    assert.match(view, /@if \(admissionFeature != "new"\)/);
    assert.match(view, /aria-labelledby="@\(admissionFeature == "new" \? "newApplicationTitle" : "admissionTitle"\)"/);
    assert.match(view, /<h2 id="newApplicationTitle">[^\n]*New Admission Application/);
    assert.doesNotMatch(view, /<h3 id="newApplicationTitle">/);
    assert.match(view, /admissionStep01[\s\S]*?Admission details/);
    assert.match(view, /admissionStep02[\s\S]*?Applicant information/);
    assert.match(view, /admissionStep03[\s\S]*?Guardian & contact/);
    assert.equal((view.match(/class="admission-form-section"/g) || []).length, 3);
    for (const field of [
        'admissionFormReference', 'academicYearId', 'academicTermId', 'campusId',
        'academicUnitId', 'applicantName', 'applicantNameBangla', 'dateOfBirth',
        'gender', 'primaryMobile', 'email', 'guardianName', 'guardianRelation',
        'guardianMobile', 'presentAddress', 'preferredLanguage', 'admissionCustomFields',
        'submitApplicationButton'
    ])
        assert.equal(view.split('id="' + field + '"').length - 1, 1, field);
    assert.match(view, /js\/admissions\/index\.js/);
});

test('submit still validates required, dynamic, guardian and server conditions; retry remains safe', () => {
    new Function(app);
    assert.match(app, /form\.addEventListener\('submit', submitApplication\)/);
    assert.match(app, /if \(!form\.checkValidity\(\) \|\| missingChoices\.length\)/);
    assert.match(app, /admissionErrors\(form, missingChoices\)/);
    assert.match(app, /const selectedForm = state\.options\?\.openForms/);
    assert.match(app, /isMinor\(dateOfBirth\)/);
    assert.match(app, /field\.closest\('label'\)/);
    assert.match(app, /field\.dataset\.admissionField/);
    assert.match(app, /fetch\('\/api\/admission-applications', apiOptions\('POST', request\)\)/);
    assert.match(app, /state\.pendingFingerprint = fingerprint/);
    assert.match(app, /request\.clientRequestId = state\.pendingRequestId/);
    assert.match(app, /if \(!response\.ok \|\| !payload\?\.success\)/);
    assert.match(app, /showSubmissionFeedback\(applicationSubmissionError\(response, payload\)\)/);
    assert.match(app, /payload\?\.message/);
    assert.match(app, /payload\?\.errors/);
    assert.match(app, /response\.status >= 500/);
    assert.match(app, /form\.reset\(\)/);
    assert.match(app, /showApplicationReceipt\(/);
    assert.doesNotMatch(app, /showAlert\('danger', i18n\.createFailed\)/);
});

test('inline accessible error does not expose server exception or erase applicant values', () => {
    assert.match(view, /id="admissionSubmitFeedback"[^>]*role="alert"[^>]*aria-live="assertive"/);
    assert.match(app, /detail\.textContent = message/);
    assert.match(app, /host\.replaceChildren\(heading, detail\)/);
    assert.match(app, /host\.focus\(\)/);
    assert.match(app, /Your entered information is still in the form/);
    assert.match(app, /Check the application list before retrying/);
    assert.match(app, /clearSubmissionFeedback\(\)/);
    assert.match(css, /#admissionSubmitFeedback:not\(\.d-none\)/);
    assert.doesNotMatch(view, /Check application readiness|Microphone unavailable|Speech recognition is not enabled/);
});

test('form styling remains responsive and isolated to the new application', () => {
    assert.match(css, /\.admission-application-form \{ display: grid/);
    assert.match(css, /\.admission-form-section-heading/);
    assert.match(css, /\.admission-page \.admission-page-header h2/);
    assert.match(css, /@media \(max-width: 575\.98px\)[\s\S]*?\.admission-new-card/);
    assert.match(css, /\.admission-form-actions \.btn \{ width: 100%/);
});
