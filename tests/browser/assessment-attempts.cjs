'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const project = path.resolve(__dirname, '../..');
const razor = fs.readFileSync(path.join(project, 'EduOS.App/Views/AssessmentAdministration/Attempts.cshtml'), 'utf8');
const start = razor.indexOf('<div class="container-fluid py-3" id="assessmentAttemptWorkspace">');
const end = razor.indexOf('<script src=', start);
assert(start >= 0 && end > start, 'Attempt view markup missing');
const html = '<!doctype html><html><body>' + razor.slice(start, end)
    .replace('@Html.AntiForgeryToken()', '<input name="__RequestVerificationToken" value="attempt-csrf">') + '</body></html>';
const ref = '11111111-2222-4333-8444-555555555555';
const success = (route, data) => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify({ success: true, data }) });

async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        const errors = [], writes = [];
        page.on('pageerror', e => errors.push(e.message));
        page.on('dialog', dialog => dialog.accept());
        let status = 0;
        await page.route('http://localhost/**', route => {
            const req = route.request(), url = new URL(req.url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            if (url.pathname === '/api/assessment-administration/scopes/page')
                return success(route, { items: [{ assessmentId: 5, assessmentReference: ref,
                    assessmentName: 'Math Retake', academicBatchId: 4, academicBatchName: 'Batch A',
                    assessmentSubjectId: 8, subjectName: 'Math' }] });
            if (url.pathname === '/api/assessment-administration/assessments/' + ref)
                return success(route, { attemptMode: 1, state: 1 });
            if (url.pathname === '/api/assessment-administration/attempts/candidates')
                return success(route, { totalCount: 1, items: [{ studentSubjectRegistrationId: 17,
                    studentCode: 'ST-1', studentName: 'Student A', rollNo: '01',
                    priorOutcome: 'Failed prior course', priorResultPublicationId: 25,
                    canRequest: status === 0 }] });
            if (url.pathname === '/api/assessment-administration/attempts' && req.method() === 'GET')
                return success(route, { totalCount: status ? 1 : 0, items: status ? [{ id: 31,
                    assessmentSubjectId: 8, studentSubjectRegistrationId: 17, studentCode: 'ST-1',
                    studentName: 'Student A', subjectName: 'Math', decision: status,
                    reason: 'Failed terminal result', rowVersion: 'AQIDBAUGBwg=',
                    decisionNote: status === 2 ? 'Evidence verified' : null }] : [] });
            if (req.method() === 'POST') {
                assert.equal(req.headers().requestverificationtoken, 'attempt-csrf');
                writes.push({ path: url.pathname, body: req.postDataJSON() });
                if (url.pathname === '/api/assessment-administration/attempts') {
                    status = 1; return success(route, { id: 31 });
                }
                if (url.pathname === '/api/assessment-administration/attempts/31/decision') {
                    status = 2; return success(route, true);
                }
            }
            return route.fulfill({ status: 404, contentType: 'application/json',
                body: JSON.stringify({ success: false, message: 'Unexpected browser endpoint' }) });
        });
        await page.goto('http://localhost/AssessmentAdministration/Attempts');
        await page.addScriptTag({ path: path.join(project, 'EduOS.App/wwwroot/js/assessment/attempts.js') });
        await page.locator('#attemptSubject option').nth(1).waitFor({ state: 'attached' });
        await page.selectOption('#attemptSubject', '8');
        await page.getByText('Failed prior course').waitFor();
        await page.locator('#attemptRequestReason').fill('Failed terminal result');
        await page.getByRole('button', { name: 'Request', exact: true }).click();
        await page.getByText('Requested', { exact: true }).waitFor();
        await page.locator('#attemptDecisionReason').fill('Evidence verified');
        await page.getByRole('button', { name: 'Approve', exact: true }).click();
        await page.getByText('Approved', { exact: true }).waitFor();
        assert.equal(writes.length, 2);
        assert.deepEqual(writes[0].body, { assessmentSubjectId: 8,
            studentSubjectRegistrationId: 17, priorResultPublicationId: 25,
            reason: 'Failed terminal result' });
        assert.equal(writes[1].body.decision, 2);
        assert.equal(writes[1].body.rowVersion, 'AQIDBAUGBwg=');
        assert.deepEqual(errors, []);
        console.log('Academic targeted attempt browser: candidate → request → approval PASS');
    } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
