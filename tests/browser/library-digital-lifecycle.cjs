'use strict';
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');

const script = path.resolve(__dirname, '../../EduOS.App/wwwroot/js/library/digital.js');
const SESSION = 'A'.repeat(64);
const RESOURCE = '11111111-2222-4333-8444-555555555551';
const BOOK = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const resource = {
    reference: RESOURCE, bookReference: BOOK, bookTitle: 'Private catalogue title',
    title: 'Protected course reader', fileName: 'private-reader.pdf',
    contentType: 'application/pdf', sizeBytes: 32, isActive: true,
    expiresOn: '2027-12-31', campusId: 41, rowVersion: 'AQIDBA=='
};

function fixture(admin, language) {
    const adminFields = admin ? [
        '<form id="digitalUploadForm"><input name="__RequestVerificationToken" value="csrf">',
        '<input id="digitalBookSearch"><button id="digitalBookFind" type="button">Find</button>',
        '<select id="digitalBook" required><option value="">Select</option></select>',
        '<input id="digitalTitle" required><input id="digitalFile" type="file" required>',
        '<input id="digitalExpiresOn" type="date"><select id="digitalCampus" disabled></select>',
        '<button id="digitalPublish" type="submit">Publish</button></form>',
        '<select id="digitalStatus"><option value="active">Active</option><option value="all">All</option></select>',
        '<button id="digitalExport" type="button">Export</button>',
        '<dialog id="digitalEditDialog"><form id="digitalEditForm">',
        '<input id="digitalEditName" required><input id="digitalEditExpiry" type="date">',
        '<select id="digitalEditCampus" disabled></select>',
        '<button id="digitalEditSave" type="submit">Save</button>',
        '<button id="digitalEditCancel" type="button">Cancel</button></form></dialog>',
        '<dialog id="digitalReplaceDialog"><form id="digitalReplaceForm">',
        '<input id="digitalReplaceTitle" required><input id="digitalReplaceExpiry" type="date">',
        '<input id="digitalReplaceFile" type="file" required>',
        '<button id="digitalReplaceSave" type="submit">Publish version</button>',
        '<button id="digitalReplaceCancel" type="button">Cancel</button></form></dialog>',
        '<form id="digitalAuditFilters"><input id="digitalAuditFrom" type="date">',
        '<input id="digitalAuditTo" type="date"><select id="digitalAuditAction">',
        '<option value="all">All</option></select><button type="submit">Apply</button></form>',
        '<button id="digitalAuditExport" type="button">CSV</button>',
        '<table><tbody id="digitalAuditRows"></tbody></table>',
        '<button id="digitalAuditPrevious" type="button"></button>',
        '<span id="digitalAuditPage"></span>',
        '<button id="digitalAuditNext" type="button"></button>'
    ].join('') : '';
    return '<!doctype html><html lang="' + language + '"><head>' +
        '<meta name="request-verification-token" content="csrf"></head>' +
        '<body data-auth-session-reference="' + SESSION + '" data-auth-tenant-id="101">' +
        '<div id="digitalAlert" class="d-none" tabindex="-1"></div>' +
        adminFields +
        '<form id="digitalSearchForm"><input id="digitalQuery"></form>' +
        '<span id="digitalCount"></span><span id="digitalPage"></span>' +
        '<button id="digitalPrevious" type="button"></button>' +
        '<button id="digitalNext" type="button"></button>' +
        '<table><tbody id="digitalRows"></tbody></table></body></html>';
}

async function scenario(browser, admin, language) {
    const page = await browser.newPage({ viewport: { width: 390, height: 800 },
        acceptDownloads: true });
    const errors = [], writes = [], downloads = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('download', download => downloads.push(download.suggestedFilename()));
    let releaseDownload;
    let downloadStarted;
    const started = new Promise(resolve => { downloadStarted = resolve; });
    const released = new Promise(resolve => { releaseDownload = resolve; });
    page.on('dialog', dialog => dialog.accept());
    await page.route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: fixture(admin, language) });
        if (req.method() !== 'GET')
            writes.push({ method: req.method(), path: url.pathname,
                csrf: req.headers().requestverificationtoken });
        if (url.pathname === '/api/library/catalog')
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: { items: [
                    { reference: BOOK, title: 'Private catalogue title' }] } }) });
        if (url.pathname === '/api/library/digital/campuses')
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: [
                    { id: 41, name: 'North', code: 'N' },
                    { id: 42, name: 'South', code: 'S' }
                ] }) });
        if (url.pathname === '/api/library/digital/audit')
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: {
                    items: [{ currentTitle: 'Protected course reader', resourceReference: RESOURCE,
                        action: 'LibraryDigitalResource.Download', versionNo: 1,
                        occurredAtUtc: '2026-10-30T00:00:00Z', actorUserId: 7 }],
                    totalCount: 1, totalPages: 1
                } }) });
        if (url.pathname === '/api/library/digital')
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: {
                    items: [resource], totalCount: 1, totalPages: 1,
                    hasPreviousPage: false, hasNextPage: false
                } }) });
        if (url.pathname.endsWith('/download')) {
            downloadStarted();
            await released;
            try {
                return await route.fulfill({ status: 200, contentType: 'application/pdf',
                    body: '%PDF-1.4\nprivate bytes' });
            } catch (error) {
                // The browser intentionally aborts this in-flight private read on
                // tenant switch; Playwright may reject fulfill after cancellation.
                if (!/abort|cancel|closed|intercept/i.test(String(error))) throw error;
            }
        }
        if (url.pathname.endsWith('/revoke'))
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: { ...resource, isActive: false } }) });
        if (req.method() === 'PUT')
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: resource }) });
        return route.fulfill({ status: 404, body: 'Not found' });
    });

    try {
        await page.goto('http://localhost/Library/Digital');
        await page.addScriptTag({ path: script });
        await page.locator('#digitalRows').getByText('Protected course reader').waitFor();
        assert.match(await page.locator('#digitalRows').innerText(), /private-reader.pdf/);
        if (admin) {
            await page.locator('#digitalCampus option').nth(2).waitFor({ state: 'attached' });
            assert.equal(await page.locator('#digitalPublish').isDisabled(), false);
            await page.locator('#digitalAuditRows').getByText('Protected course reader').waitFor();
            await page.locator('#digitalRows button').filter({ hasText:
                language === 'bn-BD' ? 'সম্পাদনা' : 'Edit' }).click();
            assert.equal(await page.locator('#digitalEditDialog').evaluate(el => el.open), true);
            assert.equal(await page.locator('#digitalEditName').inputValue(),
                'Protected course reader');
            assert.equal(await page.locator('#digitalEditCampus').inputValue(), '41');
            await page.locator('#digitalBook option').nth(1).waitFor({ state: 'attached' });
            assert.equal(await page.locator('#digitalBook option').count(), 2);
            await page.locator('#digitalEditCancel').click();
            assert.equal(await page.locator('#digitalEditDialog').evaluate(el => el.open), false);
            await page.locator('#digitalRows button').filter({ hasText: 'Replace' }).click();
            assert.equal(await page.locator('#digitalReplaceDialog').evaluate(el => el.open), true);
            assert.equal(await page.locator('#digitalReplaceTitle').inputValue(),
                'Protected course reader');
            await page.locator('#digitalReplaceCancel').click();
            assert.equal(await page.locator('#digitalReplaceDialog').evaluate(el => el.open), false);
            // Reopen the edit dialog only after the download has started, so
            // the subsequent session transition also proves modal scrubbing.
        }
        await page.locator('#digitalRows button').filter({ hasText:
            language === 'bn-BD' ? 'ডাউনলোড' : 'Download' }).click();
        await started;
        if (admin) {
            await page.locator('#digitalRows button').filter({ hasText: 'Edit' }).click();
            assert.equal(await page.locator('#digitalEditDialog').evaluate(el => el.open), true);
        }
        await page.evaluate(() => { document.body.dataset.authTenantId = '202'; });
        await page.waitForFunction(() => document.getElementById('digitalRows').children.length === 0);
        releaseDownload();
        await page.waitForTimeout(200);
        assert.equal(await page.locator('#digitalRows').innerText(), '');
        assert.equal(await page.locator('#digitalCount').innerText(), '');
        assert.equal(await page.locator('#digitalPage').innerText(), '');
        if (admin) assert.equal(await page.locator('#digitalAuditRows').innerText(), '');
        assert.equal(await page.locator('#digitalPrevious').isDisabled(), true);
        assert.equal(await page.locator('#digitalNext').isDisabled(), true);
        assert.deepEqual(downloads, [], 'old tenant private bytes must never be downloaded');
        if (admin) {
            assert.equal(await page.locator('#digitalEditDialog').evaluate(el => el.open), false);
            assert.equal(await page.locator('#digitalEditName').inputValue(), '');
            assert.equal(await page.locator('#digitalBook option').count(), 0);
            assert.equal(await page.locator('#digitalCampus option').count(), 0);
            assert.equal(await page.locator('#digitalEditCampus option').count(), 0);
        }
        assert.deepEqual(writes, [], 'session transition must not submit a stale mutation');
        assert.deepEqual(errors, []);
        console.log('Library digital ' + (admin ? 'admin' : 'student') + ' ' +
            language + ' session/download privacy PASS');
    } finally {
        releaseDownload();
        await page.close();
    }
}

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await scenario(browser, false, 'bn-BD');
        await scenario(browser, true, 'en-BD');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
