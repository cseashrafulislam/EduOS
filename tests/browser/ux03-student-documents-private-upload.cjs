'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const razor = fs.readFileSync(path.join(root, 'EduOS.App/Views/StudentDocuments/Index.cshtml'), 'utf8');
const from = razor.indexOf('<div id="studentDocumentsApp"');
const to = razor.indexOf('@section Scripts', from);
assert.ok(from >= 0 && to > from, 'Student Documents Razor view must exist');
const markup = razor.slice(from, to).replace('@Html.AntiForgeryToken()',
    '<input name="__RequestVerificationToken" value="test-csrf">');
const a = '11111111-2222-4333-8444-555555555555';
const b = 'bbbbbbbb-1111-4111-8111-222222222222';
const reply = (route, status, data) => route.fulfill({ status,
    contentType: 'application/json', body: JSON.stringify({ success: status < 400, data }) });

async function verify(browser, width, language) {
    const page = await browser.newPage({ viewport: { width, height: 840 } });
    const attaches = [], uploads = [];
    let resolveUpload, markUpload;
    const uploadStarted = new Promise(resolve => { markUpload = resolve; });
    const uploadReleased = new Promise(resolve => { resolveUpload = resolve; });
    try {
        await page.route('http://eduos.test/**', async route => {
            const request = route.request(), url = new URL(request.url());
            if (!url.pathname.startsWith('/api/')) return route.fulfill({
                status: 200, contentType: 'text/html', body: '<!doctype html><html lang="' + language +
                    '"><body data-auth-tenant-id="25" data-auth-session-reference="current">' + markup +
                    '</body></html>' });
            if (url.pathname === '/api/student-documents/types')
                return reply(route, 200, [{ name: 'Birth certificate', code: 'Birth' }]);
            if (url.pathname === '/api/students') {
                const isB = url.searchParams.get('search') === 'BB';
                return reply(route, 200, { items: [{ reference: isB ? b : a,
                    studentCode: isB ? 'BB' : 'AA', fullName: isB ? 'Student B' : 'Student A' }] });
            }
            if (url.pathname === '/api/students/' + a || url.pathname === '/api/students/' + b)
                return reply(route, 200, { reference: url.pathname.split('/').pop(),
                    studentCode: url.pathname.endsWith(a) ? 'AA' : 'BB',
                    fullName: url.pathname.endsWith(a) ? 'Student A' : 'Student B', rowVersion: 'v1' });
            if (url.pathname === '/api/document-admin/files' && request.method() === 'POST') {
                uploads.push(request.headers()['requestverificationtoken']);
                markUpload(); await uploadReleased;
                return reply(route, 200, { reference: '99999999-aaaa-4bbb-8ccc-111111111111' });
            }
            if (url.pathname.startsWith('/api/student-documents/students/')) {
                if (request.method() === 'GET') return reply(route, 200, attaches.length ? [
                    { reference: 'doc-1', documentTypeCode: 'Birth', title: 'School record',
                        fileName: 'record.pdf', isActive: false }] : []);
                assert.equal(request.headers()['requestverificationtoken'], 'test-csrf');
                attaches.push({ url: url.pathname, body: request.postDataJSON() });
                return reply(route, 200, { reference: 'doc-1' });
            }
            return reply(route, 404, null);
        });
        await page.goto('http://eduos.test/StudentDocuments/Index');
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/students/documents.js') });
        await page.locator('#documentType option[value="Birth"]').waitFor({ state: 'attached' });
        await page.locator('#documentSearch').fill('AA');
        await page.locator('#documentSearchForm button').click();
        await page.locator('#documentMatches button').click();
        await page.waitForFunction(() => document.querySelector('#documentSelected')?.textContent.includes('Student A'));
        await page.locator('#documentType').selectOption('Birth');
        await page.locator('#documentTitle').fill('School record');
        await page.locator('#documentFile').setInputFiles({
            name: 'record.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.7\nexample') });
        await page.locator('#documentAttach').click();
        await uploadStarted;
        assert.equal(await page.locator('#documentFile').isDisabled(), true);
        await page.locator('#documentSearch').fill('BB');
        await page.locator('#documentSearchForm button').click();
        await page.locator('#documentMatches button').click();
        assert.match(await page.locator('#documentSelected').innerText(), /Student A/);
        resolveUpload();
        await page.waitForFunction(() => document.querySelector('#documentRows')?.textContent.includes('record.pdf'));
        assert.equal(uploads.length, 1);
        assert.equal(attaches.length, 1);
        assert.equal(attaches[0].url, '/api/student-documents/students/' + a);
        assert.equal(attaches[0].body.studentRowVersion, 'v1');
        await page.evaluate(() => { document.body.dataset.authTenantId = '26'; });
        await page.waitForFunction(() => document.querySelector('#documentSelected')?.textContent === '');
        assert.equal(await page.locator('#documentRows tr').count(), 0);
        assert.equal(await page.locator('#documentMatches button').count(), 0);
        console.log(`Student Documents synthetic Chromium ${width}px ${language}: PASS`);
    } finally { await page.close(); }
}

async function main() {
    const browser = await chromium.launch({ headless: true,
        executablePath: process.env.CHROMIUM_PATH || undefined, args: ['--no-sandbox'] });
    try {
        await verify(browser, 360, 'en-BD');
        await verify(browser, 390, 'bn-BD');
    } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
