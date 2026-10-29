'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const project = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(project,
    'EduOS.App/Views/AssessmentAdministration/Certificates.cshtml'), 'utf8');
const start = view.indexOf('<section class="container-fluid py-4" id="certificateIssueWorkspace">');
const end = view.indexOf('</section>', start);
assert(start >= 0 && end > start, 'Certificate view missing');
const html = '<!doctype html><html><body>' + view.slice(start, end + '</section>'.length)
    .replace('@Html.AntiForgeryToken()', '<input name="__RequestVerificationToken" value="cert-csrf">') +
    '</body></html>';
const refs = [
    '11111111-2222-4333-8444-555555555551',
    '11111111-2222-4333-8444-555555555552',
    '11111111-2222-4333-8444-555555555553'
];
async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.route('http://localhost/**', route => {
            const url = new URL(route.request().url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ contentType: 'text/html', body: html });
            const payload = url.pathname === '/api/assessment-administration/certificates' ?
                { items: [
                    { id: 1, reference: refs[0], certificateNumber: 'CERT-1', studentCode: 'ST-1',
                        studentName: 'Student A', campusName: 'Main', batchName: 'B-1', templateName: 'Achievement',
                        issueDate: '2026-10-29', isRevoked: false, hasPrintableDocument: true,
                        rowVersion: 'AQIDBAUGBwg=' },
                    { id: 2, reference: refs[1], certificateNumber: 'CERT-2', studentCode: 'ST-2',
                        studentName: 'Student B', campusName: 'Main', batchName: 'B-1', templateName: 'Legacy',
                        issueDate: '2026-10-29', isRevoked: false, hasPrintableDocument: false,
                        rowVersion: 'AQIDBAUGBwg=' },
                    { id: 3, reference: refs[2], certificateNumber: 'CERT-3', studentCode: 'ST-3',
                        studentName: 'Student C', campusName: 'Main', batchName: 'B-1', templateName: 'Achievement',
                        issueDate: '2026-10-29', isRevoked: true, hasPrintableDocument: true }
                ], totalCount: 3 } : { items: [], totalCount: 0 };
            return route.fulfill({ status: 200, contentType: 'application/json',
                body: JSON.stringify({ success: true, data: payload }) });
        });
        await page.goto('http://localhost/AssessmentAdministration/Certificates');
        await page.addScriptTag({ path: path.join(project, 'EduOS.App/wwwroot/js/assessment/certificates.js') });
        await page.locator('#certificateRegisterRows tr').nth(2).waitFor();
        const actions = await page.locator('#certificateRegisterRows tr').all();
        assert.equal(await actions[0].getByRole('link', { name: 'Print' }).count(), 1);
        assert.equal(await actions[1].getByRole('link', { name: 'Print' }).count(), 0);
        assert.equal(await actions[2].getByRole('link', { name: 'Print' }).count(), 0);
        const href = await actions[0].getByRole('link', { name: 'Print' }).getAttribute('href');
        assert.equal(href, '/AssessmentAdministration/CertificateDocument?reference=' + refs[0]);
        assert.deepEqual(errors, []);
        console.log('Certificate browser: issued print link, legacy and revoked cannot print PASS');
    } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
