'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const project = path.resolve(__dirname, '../..');
const razor = fs.readFileSync(path.join(project,
    'EduOS.App/Views/AssessmentAdministration/Setup.cshtml'), 'utf8');
const start = razor.indexOf('<div class="container-fluid py-3" id="assessmentSetupWorkspace">');
const end = razor.indexOf('<script src=', start);
assert(start >= 0 && end > start, 'Exam setup view markup missing');
const html = '<!doctype html><html><body>' + razor.slice(start, end)
    .replace('@Html.AntiForgeryToken()', '<input name="__RequestVerificationToken" value="exam-csrf">') +
    '</body></html>';
const assessmentReference = '11111111-2222-4333-8444-555555555555';
const offeringReference = '22222222-3333-4444-8555-666666666666';
const result = (route, data) => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify({ success: true, data }) });

async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        const errors = [], writes = [];
        let saved = false, assigned = false;
        page.on('pageerror', e => errors.push(e.message));
        await page.route('http://localhost/**', route => {
            const request = route.request(), url = new URL(request.url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            const p = url.pathname;
            if (p === '/api/assessment-administration/setup/references')
                return result(route, { campuses: [{ id: 3, name: 'Campus C' }],
                    academicYears: [{ id: 2026, campusId: 3, name: '2026' }],
                    academicTerms: [{ id: 5, academicYearId: 2026, name: 'Term 1' }] });
            if (p === '/api/assessment-administration/grade-schemes')
                return result(route, { items: [{ id: 9, code: 'GPA', name: 'Credit weighted',
                    useCreditWeightedGpa: true, repeatCourseGradePolicy: 1 }] });
            if (p === '/api/assessment-administration/assessments/page')
                return result(route, { totalCount: saved ? 1 : 0, items: saved ? [
                    { reference: assessmentReference, code: 'RETAKE-2026', name: 'Mathematics retake', state: 1 }
                ] : [] });
            if (p === '/api/assessment-administration/setup/batches')
                return result(route, { items: [{ id: 41, campusId: 3,
                    academicYearId: 2026, code: 'B-1', name: 'Batch 1' }] });
            if (p === '/api/assessment-administration/setup/batches/41/offerings')
                return result(route, { items: [{ reference: offeringReference, code: 'MAT-101',
                    subjectName: 'Mathematics' }] });
            if (p === '/api/assessment-administration/assessments/' + assessmentReference)
                return result(route, { reference: assessmentReference, name: 'Mathematics retake',
                    code: 'RETAKE-2026', state: 1, campusId: 3, academicYearId: 2026,
                    academicTermId: 5, gradeSchemeId: 9, type: 1, attemptMode: 1,
                    countsTowardCgpa: true, startDate: '2026-05-01', endDate: '2026-05-15',
                    remarks: '', rowVersion: 'AQIDBAUGBwg=', subjects: assigned ? [
                        { subjectCode: 'MAT-101', subjectName: 'Mathematics',
                            fullMarks: 100, passMarks: 33, weightage: 100 }
                    ] : [] });
            if (request.method() === 'POST') {
                assert.equal(request.headers().requestverificationtoken, 'exam-csrf');
                writes.push({ path: p, body: request.postDataJSON() });
                if (p === '/api/assessment-administration/assessments') {
                    saved = true; return result(route, { reference: assessmentReference });
                }
                if (p === '/api/assessment-administration/subjects') {
                    assigned = true; return result(route, { id: 12 });
                }
            }
            return route.fulfill({ status: 404, contentType: 'application/json',
                body: JSON.stringify({ success: false, message: 'Unexpected setup endpoint' }) });
        });
        await page.goto('http://localhost/AssessmentAdministration/Setup');
        await page.addScriptTag({ path: path.join(project, 'EduOS.App/wwwroot/js/assessment/setup.js') });
        await page.locator('#setupCampus option').nth(1).waitFor({ state: 'attached' });
        await page.selectOption('#setupCampus', '3');
        await page.selectOption('#setupYear', '2026');
        await page.selectOption('#setupTerm', '5');
        await page.selectOption('#setupScheme', '9');
        await page.locator('#setupName').fill('Mathematics retake');
        await page.locator('#setupCode').fill('RETAKE-2026');
        await page.locator('#setupStart').fill('2026-05-01');
        await page.locator('#setupEnd').fill('2026-05-15');
        await page.selectOption('#setupAttemptMode', '1');
        await page.locator('#setupCgpa').check();
        await page.locator('#setupSave').click();
        await page.getByText('Assessment draft saved', { exact: false }).waitFor();
        await page.selectOption('#setupBatch', '41');
        await page.locator('#setupOffering option').nth(1).waitFor({ state: 'attached' });
        await page.selectOption('#setupOffering', offeringReference);
        await page.locator('#setupAddSubject').click();
        await page.getByText('Assessment subject saved', { exact: false }).waitFor();
        await page.getByRole('cell', { name: 'MAT-101 — Mathematics' }).waitFor();
        assert.equal(writes.length, 2);
        assert.equal(writes[0].body.attemptMode, 1);
        assert.equal(writes[0].body.gradeSchemeId, 9);
        assert.equal(writes[0].body.countsTowardCgpa, true);
        assert.equal(writes[0].body.campusId, 3);
        assert.equal(writes[1].body.assessmentReference, assessmentReference);
        assert.equal(writes[1].body.subjectOfferingReference, offeringReference);
        assert.deepEqual(errors, []);
        console.log('Academic exam setup browser: draft → credit retake → subject assignment PASS');
    } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
