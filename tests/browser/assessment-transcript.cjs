'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const razor = fs.readFileSync(path.join(root, 'EduOS.App/Views/AssessmentAdministration/Transcripts.cshtml'), 'utf8');
const start = razor.indexOf('<section class="container-fluid py-4" id="transcriptWorkspace">');
const end = razor.indexOf('@section Scripts', start);
assert(start >= 0 && end > start, 'Transcript register markup missing');
const html = '<!doctype html><html><body>' + razor.slice(start, end)
    .replace('@Html.AntiForgeryToken()',
        '<input name="__RequestVerificationToken" value="transcript-csrf">') + '</body></html>';
const reference = '11111111-2222-4333-8444-555555555555';
const student = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const success = (route, data) => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify({ success: true, data }) });

async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        const exceptions = [], mutations = [];
        page.on('pageerror', error => exceptions.push(error.message));
        page.on('dialog', dialog => dialog.type() === 'prompt'
            ? dialog.accept('Approved administrative revocation') : dialog.accept());
        let issued = false, revoked = false;
        await page.route('http://eduos.test/**', async route => {
            const request = route.request(), url = new URL(request.url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            if (url.pathname === '/api/assessment-administration/transcripts/candidates/page')
                return success(route, { page: 1, pageSize: 20, totalCount: 1,
                    items: [{ studentReference: student, studentCode: 'ST-101', studentName: 'Student A' }] });
            if (url.pathname === '/api/assessment-administration/transcripts/academic-record/' + student)
                return success(route, { studentCode: 'ST-101', studentName: 'Student A',
                    latestPublishedCgpa: 3.5, legacyPublishedResults: 0,
                    courses: [{ assessmentName: 'Final', publicationVersionNo: 1,
                        academicBatchName: 'Cohort A', subjectCode: 'MAT', subjectName: 'Math',
                        obtainedMarks: 76, fullMarks: 100, creditHours: 3, gradePoint: 3.5,
                        gradeLetter: 'A', isPassed: true, isWithheld: false }] });
            if (url.pathname === '/api/assessment-administration/transcripts' && request.method() === 'GET')
                return success(route, { page: 1, pageSize: 20, totalCount: issued ? 1 : 0,
                    items: issued ? [{ reference, studentReference: student, transcriptNumber: 'TRN-1',
                        studentCode: 'ST-101', studentName: 'Student A', issueDate: '2026-10-29',
                        rowVersion: 'AQIDBAUGBwg=', isRevoked: revoked }] : [] });
            if (request.method() === 'POST') {
                assert.equal(request.headers().requestverificationtoken, 'transcript-csrf');
                mutations.push({ path: url.pathname, body: request.postDataJSON() });
                if (url.pathname === '/api/assessment-administration/transcripts') {
                    issued = true;
                    return success(route, { reference, transcriptNumber: 'TRN-1' });
                }
                if (url.pathname === '/api/assessment-administration/transcripts/' + reference + '/revoke') {
                    revoked = true;
                    return success(route, { reference, isRevoked: true });
                }
            }
            return route.fulfill({ status: 404, contentType: 'application/json',
                body: JSON.stringify({ success: false, message: 'Unexpected browser endpoint' }) });
        });
        await page.goto('http://eduos.test/AssessmentAdministration/Transcripts');
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/assessment/transcripts.js') });
        await page.locator('#transcriptStudentSearch').fill('ST-101');
        await page.locator('#transcriptFindStudent').click();
        await page.locator('#transcriptStudent option').nth(1).waitFor({ state: 'attached' });
        await page.selectOption('#transcriptStudent', student);
        await page.locator('#transcriptPreviewRecord').click();
        await page.getByText('MAT — Math').waitFor();
        await page.locator('#transcriptDate').fill('2026-10-29');
        await page.locator('#transcriptIssue').click();
        await page.getByRole('link', { name: 'Document' }).waitFor();
        const link = await page.getByRole('link', { name: 'Document' }).getAttribute('href');
        assert.equal(link, '/AssessmentAdministration/TranscriptDocument?reference=' + reference);
        assert.equal(mutations[0].body.studentReference, student);
        assert.equal(mutations[0].body.issueDate, '2026-10-29');
        await page.getByRole('button', { name: 'Revoke' }).click();
        await page.getByText('Revoked', { exact: true }).waitFor();
        assert.equal(mutations[1].body.rowVersion, 'AQIDBAUGBwg=');
        assert.equal(mutations[1].body.reason, 'Approved administrative revocation');
        assert.equal(await page.getByRole('link', { name: 'Document' }).count(), 0);
        assert.deepEqual(exceptions, []);
        console.log('Academic transcript browser: candidate → history → issue → authenticated document link → revoke PASS');
    } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
