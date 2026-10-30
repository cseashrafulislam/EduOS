'use strict';
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');

const script = path.resolve(__dirname, '../../EduOS.App/wwwroot/js/library/overdue-notices.js');
const SESSION = 'A'.repeat(64);
const unsafeTitle = '<img src=x onerror=alert(1)> Confidential loan';

function html(language) {
    return '<!doctype html><html lang="' + language + '"><head>' +
        '<meta name="request-verification-token" content="csrf-notice"></head>' +
        '<body data-auth-session-reference="' + SESSION + '" data-auth-tenant-id="101">' +
        '<section id="libraryOverdueNoticeApp">' +
        '<input id="libraryOverdueCampus" type="number" aria-label="Campus ID">' +
        '<button id="libraryNoticePreview" type="button">Preview recipients</button>' +
        '<button id="libraryNoticeSend" type="button" disabled>Send next 50</button>' +
        '<div id="libraryNoticeMessage" class="d-none" role="alert"></div>' +
        '<p id="libraryNoticeCount" aria-live="polite"></p>' +
        '<table><tbody id="libraryNoticeRows"></tbody></table></section></body></html>';
}

async function scenario(browser, language) {
    const page = await browser.newPage({ viewport: { width: 390, height: 800 } });
    const errors = [], posts = [];
    let previewRequests = 0;
    let firstPost = true, sent = false, holdPreview = false;
    let startDelayed, releaseDelayed;
    let delayedStarted = new Promise(resolve => { startDelayed = resolve; });
    let delayedReleased = new Promise(resolve => { releaseDelayed = resolve; });
    page.on('pageerror', error => errors.push(error.message));
    page.on('dialog', dialog => dialog.accept());
    await page.route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: html(language) });
        if (url.pathname.endsWith('/preview')) {
            previewRequests++;
            const campusId = Number(url.searchParams.get('campusId'));
            if (holdPreview && campusId === 202) {
                startDelayed();
                await delayedReleased;
            }
            try {
                return await route.fulfill({ contentType: 'application/json',
                    body: JSON.stringify({ success: true, data: {
                        campusId, asOfDate: '2026-10-30',
                        pendingCount: sent && campusId === 101 ? 0 : 2,
                        nextRecipients: sent && campusId === 101 ? [] : [
                            { studentCode: 'S-1', bookTitle: unsafeTitle,
                              dueDate: '2026-10-25', daysOverdue: 5 },
                            { studentCode: 'S-2', bookTitle: 'Textbook',
                              dueDate: '2026-10-26', daysOverdue: 4 }
                        ]
                    } }) });
            } catch (error) {
                if (!/abort|cancel|closed|intercept|target/i.test(String(error))) throw error;
            }
        }
        if (url.pathname.endsWith('/send') && req.method() === 'POST') {
            posts.push({ body: JSON.parse(req.postData()),
                csrf: req.headers().requestverificationtoken });
            if (firstPost) {
                firstPost = false;
                return route.abort('failed');
            }
            sent = true;
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: {
                    sentCount: 2, alreadyProcessed: false
                } }) });
        }
        return route.fulfill({ status: 404, body: 'Not found' });
    });
    try {
        await page.goto('http://localhost/Library/Overdue');
        await page.addScriptTag({ path: script });
        await page.locator('#libraryOverdueCampus').fill('101');
        await page.locator('#libraryNoticePreview').focus();
        await page.keyboard.press('Enter');
        await page.getByText('S-1').waitFor({ timeout: 7000 }).catch(async error => {
            const diagnostic = {
                previewRequests,
                message: await page.locator('#libraryNoticeMessage').innerText(),
                count: await page.locator('#libraryNoticeCount').innerText(),
                rows: await page.locator('#libraryNoticeRows').innerText(),
                errors
            };
            throw new Error('Initial overdue preview did not render: ' +
                JSON.stringify(diagnostic), { cause: error });
        });
        assert.match(await page.locator('#libraryNoticeCount').innerText(), /2/);
        assert.match(await page.locator('#libraryNoticeRows').innerText(), /<img src=x/);
        assert.equal(await page.locator('#libraryNoticeRows img').count(), 0,
            'untrusted book title must be text, never executable markup');

        await page.locator('#libraryNoticeSend').click();
        await page.waitForFunction(() =>
            !document.getElementById('libraryNoticeSend').disabled);
        assert.equal(posts.length, 1);
        assert.equal(posts[0].csrf, 'csrf-notice');
        assert.equal(posts[0].body.campusId, 101);
        assert.ok(/^[0-9a-f-]{36}$/i.test(posts[0].body.clientRequestId));

        // Refresh after lost acknowledgement; retry must preserve the key.
        await page.locator('#libraryNoticePreview').click();
        await page.getByText('S-1').waitFor();
        await page.locator('#libraryNoticeSend').click();
        await page.waitForFunction(() =>
            document.getElementById('libraryNoticeCount').textContent.includes('0'));
        assert.equal(posts.length, 2);
        assert.equal(posts[0].body.clientRequestId, posts[1].body.clientRequestId,
            'network retry must replay the same idempotency key');
        assert.equal(posts[1].csrf, 'csrf-notice');
        assert.equal(await page.locator('#libraryNoticeSend').isDisabled(), true);
        const successText = await page.locator('#libraryNoticeMessage').innerText();
        assert.match(successText, language === 'bn-BD' ? /স্মরণবার্তা/ : /reminder/);

        // A delayed response from the old campus must not restore private rows
        // after the staff member changes the campus input.
        holdPreview = true;
        await page.locator('#libraryOverdueCampus').fill('202');
        await page.locator('#libraryNoticePreview').click();
        await delayedStarted;
        await page.locator('#libraryOverdueCampus').fill('101');
        releaseDelayed();
        await page.waitForTimeout(150);
        assert.equal(await page.locator('#libraryNoticeRows').innerText(), '');
        assert.equal(await page.locator('#libraryNoticeCount').innerText(), '');
        assert.equal(await page.locator('#libraryNoticeSend').isDisabled(), true);

        // Tenant/session switch must scrub preview and prevent further mutations.
        holdPreview = false;
        // Campus 101 has zero pending after successful send; use 202 for scrub.
        await page.locator('#libraryOverdueCampus').fill('202');
        await page.locator('#libraryNoticePreview').click();
        await page.getByText('S-1').waitFor();
        await page.evaluate(() => { document.body.dataset.authTenantId = '202'; });
        await page.waitForFunction(() =>
            document.getElementById('libraryNoticeRows').children.length === 0);
        assert.equal(await page.locator('#libraryNoticeCount').innerText(), '');
        assert.equal(await page.locator('#libraryNoticeSend').isDisabled(), true);
        assert.equal(posts.length, 2);
        assert.deepEqual(errors, []);
        console.log('Library overdue notice ' + language +
            ' mobile, keyboard, CSRF, retry, stale campus and session privacy PASS');
    } finally {
        releaseDelayed();
        await page.close();
    }
}

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await scenario(browser, 'bn-BD');
        await scenario(browser, 'en-BD');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
