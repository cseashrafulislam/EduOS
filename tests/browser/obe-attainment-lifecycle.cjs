'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const script = path.join(root, 'EduOS.App/wwwroot/js/research/obe-attainment.js');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Accreditation/Obe.cshtml'), 'utf8');
const session = 'A'.repeat(64);
const digest = 'B'.repeat(64);
const reference = '11111111-2222-4333-8444-555555555555';
const start = view.indexOf('<div class="container-fluid');
const end = view.indexOf('<style media="print">');
assert.ok(start > -1 && end > start, 'Use the real OBE Razor workspace, not a duplicate test-only form');
const workspace = view.slice(start, end).replace('@Html.AntiForgeryToken()',
    '<input type="hidden" name="__RequestVerificationToken" value="csrf-obe">');

function html(locale) {
    return '<!doctype html><html lang="' + locale + '"><head><meta charset="utf-8">' +
        '<style>.d-none{display:none!important}</style></head>' +
        '<body data-auth-tenant-id="101" data-auth-session-reference="' + session + '">' +
        workspace + '</body></html>';
}

function evidence() {
    return { learnerCount: 2, publicationVersionNo: 1, evidenceSha256: digest,
        courseOutcomes: [{ courseOutcomeCode: '<img src=x onerror="window.injection=1">',
            componentCode: 'QUIZ', attainmentPercent: 75 }],
        outcomes: [{ programOutcomeCode: 'PLO1', attainmentPercent: 75 }] };
}

async function scenario(browser, locale) {
    const page = await browser.newPage({ viewport: { width: 390, height: 844 }, acceptDownloads: true });
    const errors = [], writes = [];
    let savedPolicy = false, report = null;
    page.on('pageerror', error => errors.push(error.message));
    await page.route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url()), pathname = url.pathname;
        if (!pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: html(locale) });

        if (req.method() !== 'GET') {
            assert.equal(req.headers().requestverificationtoken, 'csrf-obe');
            writes.push({ path: pathname, body: JSON.parse(req.postData()) });
        }
        const json = data => route.fulfill({ contentType: 'application/json',
            headers: { 'X-EduOS-Tenant-Id': '101', 'X-EduOS-Session-Reference': session },
            body: JSON.stringify({ success: true, data }) });

        if (pathname === '/api/accreditation/campuses') return json([{ id: 1, name: 'North Campus' }]);
        if (pathname === '/api/accreditation/programs') return json([{ id: 2, name: 'BSc CSE' }]);
        if (pathname === '/api/accreditation/program-outcomes') return json([
            { id: 4, code: 'PLO1', versionNo: 1, statement: 'Solve real problems' }
        ]);
        if (pathname === '/api/accreditation/obe/courses') return json([
            { curriculumSubjectId: 3, subjectCode: 'CSE101', subjectName: 'Programming' }
        ]);
        if (pathname === '/api/accreditation/obe/publications') return json([
            { resultPublicationId: 5, assessmentName: 'Final Exam', batchName: 'B1', versionNo: 1,
                academicYearId: 6, academicYearName: '2026', academicTermId: 7, academicTermName: 'Fall' }
        ]);
        if (pathname === '/api/accreditation/obe/policies' && req.method() === 'GET')
            return json(savedPolicy ? [{ id: 20, versionNo: 1 }] : []);
        if (pathname === '/api/accreditation/obe/policies' && req.method() === 'POST') {
            const body = writes.at(-1).body;
            assert.equal(body.campusId, 1);
            assert.equal(body.academicProgramId, 2);
            assert.equal(body.courseOutcomes[0].componentCode, 'QUIZ');
            assert.equal(body.contributions[0].weightPercent, 100);
            assert.match(body.clientRequestId, /^[\da-f-]{36}$/i);
            savedPolicy = true;
            return json({ policy: { id: 20, versionNo: 1 } });
        }
        if (pathname === '/api/accreditation/obe/publications/5/component-options') return json({
            hasFrozenRegister: true, components: [{ curriculumSubjectId: 3,
                componentCode: 'QUIZ', learnerCount: 2, completeForCourse: true }]
        });
        if (pathname === '/api/accreditation/obe/policies/20/preview') return json(evidence());
        if (pathname === '/api/accreditation/obe/policies/20/reports' && req.method() === 'POST') {
            assert.equal(writes.at(-1).body.resultPublicationId, 5);
            report = { reference, publicationVersionNo: 1, stateCode: 'Submitted',
                submittedAtUtc: '2026-11-08T01:00:00Z', snapshot: evidence() };
            return json(report);
        }
        if (pathname === '/api/accreditation/obe/reports') return json(report ? [report] : []);
        if (pathname === '/api/accreditation/obe/reports/' + reference && req.method() === 'GET')
            return json(report);
        if (pathname === '/api/accreditation/obe/reports/' + reference + '/review') {
            const body = writes.at(-1).body;
            assert.equal(body.decision, 'Approve');
            assert.ok(body.reason.length >= 10);
            report = { ...report, stateCode: 'Approved' };
            return json(report);
        }
        if (pathname === '/api/accreditation/obe/policies/20/programme-rollup') {
            assert.equal(url.searchParams.get('academicYearId'), '6');
            assert.equal(url.searchParams.get('academicTermId'), '7');
            return json({ approvedPublicationCount: 1, learnerPublicationObservations: 2,
                outcomes: [{ programOutcomeCode: 'PLO1', attainmentPercent: 75 }],
                sourceReportReferences: [reference] });
        }
        return route.fulfill({ status: 404, body: 'Unmocked OBE endpoint: ' + pathname });
    });

    try {
        await page.goto('http://localhost/Accreditation/Obe');
        await page.addScriptTag({ path: script });
        await page.locator('#obeCampus option[value="1"]').waitFor({ state: 'attached' });
        await page.locator('#obeCampus').selectOption('1');
        await page.locator('#obeProgram option[value="2"]').waitFor({ state: 'attached' });
        await page.locator('#obeProgram').selectOption('2');
        await page.locator('#obePublication option[value="5"]').waitFor({ state: 'attached' });
        await page.locator('#obeCloRows [data-role="course"]').selectOption('3');
        await page.locator('#obeCloRows [data-role="code"]').fill('CLO1');
        await page.locator('#obeCloRows [data-role="component"]').fill('QUIZ');
        await page.locator('#obeCloRows [data-role="statement"]').fill('Apply algorithms to practical problems');
        await page.locator('#obeLinkRows [data-role="clo"]').selectOption('CLO1');
        await page.locator('#obeLinkRows [data-role="plo"]').selectOption('4');
        await page.locator('#obeLinkRows [data-role="weight"]').fill('100');
        await page.locator('#obeSavePolicy').click();
        await page.waitForFunction(() => document.querySelector('#obePolicy').value === '20');
        await page.locator('#obePublication').selectOption('5');
        await page.locator('#obeEvidenceNotice').getByText(/fully evidenced|সম্পূর্ণ তথ্যসহ/).waitFor();
        await page.locator('#obePreview').click();
        await page.locator('#obePreviewDetails').getByText('75.00', { exact: true }).first().waitFor();
        assert.equal(await page.locator('#obePreviewDetails img').count(), 0,
            'Untrusted outcome labels must use textContent rather than executable markup');
        assert.equal(await page.evaluate(() => window.injection), undefined);
        await page.locator('#obeSubmit').click();
        await page.locator('#obeReportPanel').waitFor({ state: 'visible' });
        await page.locator('#obeReason').fill('Reviewed independently against frozen evidence');
        await page.locator('#obeReview').click();
        await page.locator('#obeCsv').waitFor({ state: 'visible' });
        await page.locator('#obeYear').selectOption('6');
        await page.locator('#obeTerm').selectOption('7');
        await page.locator('#obeRollup').click();
        await page.locator('#obeRollupDetails').getByText(reference, { exact: false }).waitFor();
        assert.match(await page.locator('#obeRollupDetails').innerText(), /75\.00/);
        assert.equal(writes.length, 3, 'Only policy, submit and independent review may write');
        assert.equal(writes[0].path, '/api/accreditation/obe/policies');
        assert.equal(writes[1].path, '/api/accreditation/obe/policies/20/reports');
        assert.equal(writes[2].path, '/api/accreditation/obe/reports/' + reference + '/review');
        if (locale === 'bn-BD')
            assert.match(await page.locator('#obeScopeHeading').innerText(), /একাডেমিক/);

        // A stale page must immediately discard private report and attainment
        // state. No write may leave the browser after its trusted tenant changes.
        await page.evaluate(() => { document.body.dataset.authTenantId = '202'; });
        await page.locator('#obePreview').click();
        await page.waitForFunction(() => document.getElementById('obePreviewDetails').children.length === 0);
        assert.equal(await page.locator('#obeReportPanel').isVisible(), false);
        assert.equal(writes.length, 3);
        assert.match(await page.locator('#obeNotice').innerText(), /Session changed|সেশন পরিবর্তিত/);
        assert.deepEqual(errors, [], 'Real browser JavaScript must not throw uncaught errors');
    } finally {
        await page.close();
    }
}

(async () => {
    const browser = await chromium.launch({ headless: true,
        ...(process.env.EDUOS_BROWSER_EXECUTABLE ?
            { executablePath: process.env.EDUOS_BROWSER_EXECUTABLE } : {}) });
    try {
        await scenario(browser, 'en-BD');
        await scenario(browser, 'bn-BD');
        process.stdout.write('OBE bilingual Chromium policy, review, rollup and session isolation passed\n');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
