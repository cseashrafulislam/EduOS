'use strict';
// Real data-entry acceptance on a disposable GitHub runner. Do not redirect
// this file to production or to a remote Codespace.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { execFileSync } = require('node:child_process');
const { chromium } = require('playwright');

const origin = 'https://qa.eduos.test:7243';
const password = process.env.QA_DEMO_PASSWORD || '';
const programName = process.env.QA_PROGRAM_NAME || '';
const levelName = process.env.QA_LEVEL_NAME || '';
const subjectName = process.env.QA_SUBJECT_NAME || '';
const sectionName = process.env.QA_SECTION_NAME || '';
const sha = process.env.GITHUB_SHA || '';
assert.equal(process.env.EDUOS_QA_ORIGIN, origin, 'Only isolated runner target is accepted');
assert(/^[a-f0-9]{40}$/i.test(sha), 'Missing exact candidate SHA');
for (const [key, name] of Object.entries({
    'QA Programme ': programName, 'QA Class One ': levelName,
    'QA Subject ': subjectName, 'QA Section A ': sectionName
})) assert(name.startsWith(key), 'Refusing non-synthetic fixture for ' + key);
assert(password.length >= 16, 'Missing disposable demo credentials');

async function login(page) {
    const response = await page.goto(origin + '/Account/Login', {waitUntil: 'domcontentloaded'});
    assert(response && response.status() === 200, 'Login screen unavailable');
    await page.locator('#userNameOrEmail').fill('demo.tenantadmin@eduos.example.invalid');
    await page.locator('#password').fill(password);
    await page.locator('#loginBtn').click();
    await page.waitForURL(url => url.pathname.toLowerCase() !== '/account/login',
        {timeout: 25000});
}
async function feature(page, endpoint, section) {
    const response = await page.goto(origin + '/AcademicManagement/' + endpoint,
        {waitUntil: 'domcontentloaded'});
    assert(response && response.status() === 200, 'Academic page failed: ' + endpoint);
    await page.locator('#academicManagement[data-active-feature="' + section + '"]').waitFor();
    await page.waitForFunction(() => !!window.eduosAcademicManagement?.state?.catalog,
        null, {timeout: 25000});
}
async function reveal(page, id) {
    const target = page.locator('#' + id);
    if (!(await target.isVisible())) await page.locator('[aria-controls="' + id + '"]').click();
    await target.waitFor({state: 'visible'});
}
async function catalogItem(page, group, name) {
    return page.evaluate(([key, label]) =>
        window.eduosAcademicManagement?.state?.catalog?.[key]?.find(x => x.name === label),
        [group, name]);
}
async function waitForCatalogItem(page, group, name) {
    try {
        await page.waitForFunction(([key, label]) =>
            window.eduosAcademicManagement?.state?.catalog?.[key]?.some(x => x.name === label),
            [group, name], {timeout: 25000});
    } catch {
        const notice = await page.locator('#academicNotice').textContent().catch(() => '');
        const catalog = await page.evaluate(key => ({
            count: window.eduosAcademicManagement?.state?.catalog?.[key]?.length ?? -1,
            names: (window.eduosAcademicManagement?.state?.catalog?.[key] || [])
                .map(x => x.name).filter(x => x?.startsWith('QA ')).slice(0, 8)
        }), group);
        throw new Error('Failed to load ' + group + ' after real form submission; URL=' +
            new URL(page.url()).pathname + '; notice=' + JSON.stringify(notice?.slice(0, 300)) +
            '; catalog=' + JSON.stringify(catalog));
    }
    const value = await catalogItem(page, group, name);
    assert(value && Number.isSafeInteger(value.id) && value.id > 0 && value.code,
        'Persistent backend-generated code missing: ' + group);
    return value;
}
async function run() {
    const browser = await chromium.launch({headless: true, args: ['--no-sandbox']});
    try {
        const context = await browser.newContext({
            ignoreHTTPSErrors: true, serviceWorkers: 'block',
            acceptDownloads: false, viewport: {width: 1280, height: 800}
        });
        await context.route('**/*', route =>
            new URL(route.request().url()).origin === origin
                ? route.continue() : route.abort('blockedbyclient'));
        const page = await context.newPage();
        page.setDefaultTimeout(20000);
        const failedWrites = [];
        let programmePostDispatched = false, programmePostFailed = '';
        let programmeStartedAt = 0, programmeDispatchAt = 0;
        const academicRequests = new Map();
        const academicRead = p => p.startsWith('/api/institution-onboarding/academic-') ||
            p === '/api/institution-onboarding/campus-list' ||
            p.startsWith('/api/academic-setup/catalog');
        page.on('request', request => {
            const route = new URL(request.url()).pathname;
            if (request.method() === 'GET' && academicRead(route))
                academicRequests.set(request, { route, startedAt: Date.now() });
            if (request.method() === 'POST' &&
                route === '/api/academic-setup/programmes') {
                programmePostDispatched = true;
                programmeDispatchAt = Date.now();
                console.log('[QA request] Programme POST dispatched');
            }
        });
        page.on('response', response => {
            const method = response.request().method(), path = new URL(response.url()).pathname;
            academicRequests.delete(response.request());
            if (method === 'GET' && academicRead(path))
                console.log('[QA academic GET]', path, 'HTTP', response.status());
            if ((method === 'POST' || method === 'GET') &&
                (path.startsWith('/api/academic-setup/catalog') ||
                path.startsWith('/api/academic-setup/programmes') ||
                path.startsWith('/api/academic-sections')))
                console.log('[QA HTTP]', method, path, 'status', response.status());
            if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method) && response.status() >= 400)
                failedWrites.push({endpoint: path, status: response.status()});
        });
        page.on('pageerror', error => console.error('[QA JavaScript error]', String(error.message).slice(0, 250)));
        page.on('framenavigated', frame => {
            if (frame === page.mainFrame()) console.log('[QA navigation]', new URL(frame.url()).pathname);
        });
        page.on('requestfailed', request => {
            const path = new URL(request.url()).pathname;
            academicRequests.delete(request);
            if (request.method() === 'POST' &&
                path === '/api/academic-setup/programmes')
                programmePostFailed = String(request.failure()?.errorText || '').slice(0, 80);
            if (path.startsWith('/api/')) console.error('[QA request failed]',path,
                String(request.failure()?.errorText || '').slice(0, 80));
        });
        await login(page);
        await feature(page, 'Classes', 'classes');
        assert(!(await catalogItem(page, 'programs', programName)), 'Demo programme already exists');
        console.log('[QA step] create programme');
        await reveal(page, 'programForm');
        await page.locator('#programName').fill(programName);
        const readiness = await page.locator('#programForm').evaluate(form => ({
            valid: form.checkValidity(),
            invalidFields: [...form.elements].filter(x =>
                x.willValidate && !x.checkValidity()).map(x => x.id || x.name),
            namePresent: !!form.querySelector('#programName')?.value,
            inert: form.inert,
            submitOwnsForm: form.querySelector('button[type="submit"]')?.form === form,
            sameSession: window.eduosAcademicManagement?.sameSession() === true,
            formScriptPresent: !![...document.scripts].find(x =>
                x.src.includes('/js/academic/management-forms.js'))
        }));
        assert(readiness.valid && readiness.namePresent && !readiness.inert && readiness.submitOwnsForm &&
            readiness.sameSession && readiness.formScriptPresent,
            'Programme create form not ready: ' + JSON.stringify(readiness));
        await page.evaluate(() => {
            window.__qaProgrammeSubmitSeen = false;
            window.__qaProgrammeInvalidFields = [];
            const form = document.getElementById('programForm');
            form.addEventListener('submit', () => { window.__qaProgrammeSubmitSeen = true; },
                {capture: true, once: true});
            form.addEventListener('invalid', e => {
                window.__qaProgrammeInvalidFields.push(e.target.id || e.target.name);
            }, {capture: true});
        });
        programmeStartedAt = Date.now();
        const post = page.waitForResponse(response =>
            new URL(response.url()).pathname === '/api/academic-setup/programmes' &&
            response.request().method() === 'POST', {timeout: 30000}).catch(() => null);
        await page.locator('#programForm button[type="submit"]').click();
        const created = await post;
        if (!created) {
            // Capture live SQL waits BEFORE browser.close() cancels the pending POST.
            // This script reports session metadata only and saves private app logs locally.
            try {
                const sqlSnapshot = execFileSync('bash', ['.devcontainer/qa-diagnostics.sh'], {
                    cwd: process.cwd(), encoding: 'utf8', timeout: 20000,
                    env: { ...process.env, EDUOS_QA_DIAGNOSTICS: '1' }
                });
                console.error('[QA stalled POST diagnostics]\n' + sqlSnapshot.trim().slice(0, 8000));
            } catch (error) {
                console.error('[QA diagnostics unavailable]', String(error.message).slice(0, 300));
            }
            const diagnosis = await page.evaluate(() => {
                const form = document.getElementById('programForm');
                return {
                    formSubmitObserved: window.__qaProgrammeSubmitSeen === true,
                    invalidFields: window.__qaProgrammeInvalidFields,
                    sameSession: window.eduosAcademicManagement?.sameSession() === true,
                    inert: form?.inert === true,
                    buttonDisabled: form?.querySelector('button[type="submit"]')?.disabled,
                    nameStillPresent: !!form?.querySelector('#programName')?.value,
                    noticeText: document.getElementById('academicNotice')?.textContent?.slice(0, 180)
                };
            });
            throw new Error('Programme POST did not return a response within 30s: ' +
                JSON.stringify({ ...diagnosis, programmePostDispatched,
                    elapsedMs: Date.now() - programmeStartedAt,
                    dispatchElapsedMs: programmeDispatchAt ? Date.now() - programmeDispatchAt : null,
                    programmePostFailed: programmePostFailed || null,
                    academicGetPending: [...academicRequests.values()].map(x => ({
                        route: x.route, pendingMs: Date.now() - x.startedAt
                    })).slice(0, 8) }));
        }
        console.log('[QA timing] Programme POST response', created.status(),
            'dispatchToHeadersMs', Date.now() - programmeDispatchAt);
        assert(created.status() >= 200 && created.status() < 300,
            'Programme POST returned HTTP ' + created.status());
        const program = await waitForCatalogItem(page, 'programs', programName);
        // The catalog can update before the async submit handler has released
        // the form/unsaved-changes guard. Never race a reload against the POST.
        await page.waitForFunction(() => {
            const form = document.querySelector('#programForm');
            return form && !form.inert && !form.querySelector('button[type="submit"]')?.disabled;
        }, null, {timeout: 15000});
        console.log('[QA stage] programme submit lifecycle complete; beginning full reload');
        const programReload = await page.reload({waitUntil: 'domcontentloaded'});
        assert(programReload && programReload.status() === 200,
            'Programme page reload did not return HTTP 200');
        try {
            await page.waitForFunction(() => !!window.eduosAcademicManagement?.state?.catalog,
                null, {timeout: 25000});
        } catch (error) {
            const diagnosis = await page.evaluate(() => ({
                readyState: document.readyState,
                path: location.pathname,
                title: document.title?.slice(0, 100),
                academicRoot: !!document.getElementById('academicManagement'),
                scriptPresent: !![...document.scripts].find(x =>
                    x.src.includes('/js/academic/management.js')),
                scriptCount: document.scripts.length,
                libraryReady: !!window.eduosAcademicManagement,
                catalogReady: !!window.eduosAcademicManagement?.state?.catalog,
                sameSession: window.eduosAcademicManagement?.sameSession?.(),
                sessionTransitionPending: window.EduOSSessionTransitionPending?.() === true,
                academicNotice: document.getElementById('academicNotice')?.textContent?.slice(0, 220),
                bodyPrefix: document.body?.innerText?.slice(0, 220)
            })).catch(e => ({pageInspectionError: String(e.message)}));
            console.error('[QA reload hydration diagnosis]', JSON.stringify({
                ...diagnosis, academicGetPending: [...academicRequests.values()]
                    .map(x => ({route: x.route, pendingMs: Date.now() - x.startedAt})).slice(0, 8)
            }));
            throw error;
        }
        console.log('[QA stage] academic catalogue loaded after programme reload');
        await waitForCatalogItem(page, 'programs', programName);

        console.log('[QA step] create class');
        await reveal(page, 'levelForm');
        await page.locator('#levelProgram').selectOption(String(program.id));
        await page.locator('#levelName').fill(levelName);
        await page.locator('#levelNumber').fill('1');
        await page.locator('#levelForm button[type="submit"]').click();
        const level = await waitForCatalogItem(page, 'levels', levelName);
        assert.equal(level.academicProgramId, program.id, 'Class linked to incorrect programme');
        await page.reload({waitUntil: 'domcontentloaded'});
        await waitForCatalogItem(page, 'levels', levelName);

        console.log('[QA step] create subject');
        await feature(page, 'Subjects', 'subjects');
        await reveal(page, 'subjectForm');
        await page.locator('#subjectName').fill(subjectName);
        await page.locator('#subjectCredits').fill('1.50');
        await page.locator('#subjectForm button[type="submit"]').click();
        const subject = await waitForCatalogItem(page, 'subjects', subjectName);
        await page.reload({waitUntil: 'domcontentloaded'});
        await waitForCatalogItem(page, 'subjects', subjectName);

        console.log('[QA step] create independent section');
        const sectionPage = await page.goto(origin + '/AcademicManagement/Sections',
            {waitUntil: 'domcontentloaded'});
        assert(sectionPage && sectionPage.status() === 200, 'Independent Sections register unavailable');
        await page.locator('#academicSectionsPage').waitFor();
        // Native <option> elements exist in the DOM but are often never
        // CSS-visible. Wait for population, then select by stable class ID.
        await page.locator('#sectionLevel option[value="' + level.id + '"]')
            .waitFor({state: 'attached'});
        await page.waitForFunction(() => [...document.querySelectorAll('#sectionCampus option')]
            .some(o => Number(o.value) > 0), null, {timeout: 25000});
        await reveal(page, 'sectionCreateForm');
        const campusId = await page.locator('#sectionCampus option').evaluateAll(items =>
            Number(items.find(x => Number(x.value) > 0)?.value || 0));
        assert(campusId > 0, 'Demo campus not available');
        await page.locator('#sectionCampus').selectOption(String(campusId));
        await page.locator('#sectionLevel').selectOption(String(level.id));
        await page.locator('#sectionName').fill(sectionName);
        await page.locator('#sectionCapacity').fill('35');
        await page.locator('#sectionCreateForm button[type="submit"]').click();
        let row = page.locator('#sectionRows tr').filter({hasText: sectionName});
        await row.waitFor({timeout: 25000});
        assert((await row.locator('td').nth(3).innerText()).trim(), 'Section code missing');
        await page.reload({waitUntil: 'domcontentloaded'});
        row = page.locator('#sectionRows tr').filter({hasText: sectionName});
        await row.waitFor({timeout: 25000});
        const sectionCode = (await row.locator('td').nth(3).innerText()).trim();
        assert(sectionCode.length > 0, 'Section code did not persist after reload');
        assert.deepEqual(failedWrites, [], 'A real form submission returned an error');

        fs.mkdirSync('qa-artifacts', {recursive: true});
        fs.writeFileSync('qa-artifacts/real-academic-foundation.json',
            JSON.stringify({
                sha, status: 'PASS', target: 'disposable-github-runner',
                checks: [
                    'programme-ui-create-reload', 'backend-programme-code',
                    'class-ui-create-reload', 'class-programme-fk', 'backend-class-code',
                    'subject-ui-create-reload', 'backend-subject-code',
                    'section-ui-create-reload', 'section-class-link', 'backend-section-code'
                ],
                programId: program.id, levelId: level.id, subjectId: subject.id,
                campusId, sectionCode
            }, null, 2));
        console.log('PASS real UI: programme, class, subject, independent section, reload and codes.');
        await context.close();
    } finally {
        await browser.close();
    }
}
run().catch(error => {
    console.error('Real academic data-entry QA failed: ' + error.message);
    process.exitCode = 1;
});
