'use strict';
// Synthetic viewport/locale/persona presentation gate. These personas are fixture
// markup, NOT authenticated authorization or institutional UAT evidence.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const cssPath = path.join(root, 'EduOS.App/wwwroot/css/site.css');
const scriptPath = path.join(root, 'EduOS.App/wwwroot/js/platform/native-form-validation.js');

function fixture(locale, persona) {
    const admin = persona === 'TenantAdmin';
    return `<!doctype html><html lang="${locale}"><head><meta name="viewport" content="width=device-width, initial-scale=1"></head>
    <body data-auth-tenant-id="21" data-auth-session-reference="synthetic-session">
    <main class="page-body" id="main-content"><div class="card"><div class="card-body">
    <h1>${locale.startsWith('bn') ? 'রেকর্ড সংশোধন' : 'Record correction'}</h1>
    <form id="edit" action="/never-submit" method="post">
      <label for="record">${locale.startsWith('bn') ? 'নাম' : 'Name'}</label>
      <input class="form-control" id="record" name="record" required maxlength="60" aria-describedby="recordHint">
      <span id="recordHint">${locale.startsWith('bn') ? 'অনুমোদিত তথ্য' : 'Authorized details'}</span>
      <div class="d-flex gap-2" id="actions">
        <button class="btn btn-primary" type="submit">${locale.startsWith('bn') ? 'সংরক্ষণ করুন' : 'Save record'}</button>
        ${admin ? '<button class="btn btn-outline-secondary" type="button" id="approval">Approve after separate server validation</button>' : ''}
      </div>
    </form></div></div></main></body></html>`;
}

(async () => {
    const executablePath = process.env.CHROMIUM_PATH ||
        (fs.existsSync('/usr/bin/chromium') ? '/usr/bin/chromium' : undefined);
    const browser = await chromium.launch({ headless: true, executablePath, args: ['--no-sandbox'] });
    let checked = 0;
    try {
        for (const width of [360, 390, 768, 1280, 1440]) {
            for (const locale of ['en-BD', 'bn-BD']) {
                for (const persona of ['TenantAdmin', 'Teacher', 'Student']) {
                    const context = await browser.newContext({ viewport: { width, height: 860 },
                        reducedMotion: 'reduce' });
                    const page = await context.newPage();
                    const errors = [];
                    page.on('pageerror', error => errors.push(error.message));
                    await page.route('http://eduos.test/**', route => route.fulfill({ status: 200,
                        contentType: 'text/html', body: fixture(locale, persona) }));
                    try {
                        await page.goto('http://eduos.test/Example/Index');
                        await page.addStyleTag({ path: cssPath });
                        await page.addScriptTag({ path: scriptPath });
                        assert.equal(await page.locator('#approval').count(), persona === 'TenantAdmin' ? 1 : 0);
                        await page.locator('#edit button[type=submit]').click();
                        const error = page.locator('.edu-native-validation');
                        await error.waitFor();
                        assert.equal(await error.innerText(), locale === 'bn-BD'
                            ? 'এই ঘরটি পূরণ করুন।' : 'Complete this field.');
                        assert.equal(await page.locator('#record').getAttribute('aria-invalid'), 'true');
                        assert.match(await page.locator('#record').getAttribute('aria-describedby'), /recordHint edu-native-validation/);
                        await page.locator('#record').fill('Verified');
                        assert.equal(await error.count(), 0);
                        assert.equal(await page.locator('#record').getAttribute('aria-describedby'), 'recordHint');
                        const state = await page.evaluate(() => ({
                            screen: innerWidth,
                            scroll: document.documentElement.scrollWidth,
                            buttons: [...document.querySelectorAll('#actions button')]
                                .map(button => button.getBoundingClientRect())
                        }));
                        assert.ok(state.scroll <= state.screen + 1, `${width}/${locale}/${persona}: no horizontal overflow`);
                        assert.ok(state.buttons.every(rect => rect.x >= -1 && rect.right <= width + 1),
                            `${width}/${locale}/${persona}: touch actions remain visible`);
                        assert.deepEqual(errors, []);
                        checked++;
                    } finally { await context.close(); }
                }
            }
        }
        console.log(`UX-03 shared responsive/locale/persona fixture: ${checked}/30 browser cases PASS`);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
