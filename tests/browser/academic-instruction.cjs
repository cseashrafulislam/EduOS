'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const razor = fs.readFileSync(path.join(root, 'EduOS.App/Views/AcademicInstruction/Index.cshtml'), 'utf8');
const start = razor.indexOf('<section id="instructionApp"');
const end = razor.indexOf('@section Scripts', start);
assert(start >= 0 && end > start, 'Academic Instruction Razor workspace missing');
const html = '<!doctype html><html><head>' +
    '<meta name="request-verification-token" content="academic-instruction-csrf"></head><body>' +
    razor.slice(start, end)
        .replace('data-can-manage="@canManage.ToString().ToLowerInvariant()"', 'data-can-manage="true"')
        .replace('data-can-teach="@User.IsInRole("Teacher").ToString().ToLowerInvariant()"', 'data-can-teach="false"') +
    '</body></html>';
const reply = (route, status, data, message = '') => route.fulfill({
    status, contentType: 'application/json',
    body: JSON.stringify({ success: status < 400, data, message })
});
const teacherRef = '22222222-3333-4444-8555-666666666666';
const version = 'AQIDBAUGBwg=';
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage({ acceptDownloads: true });
        const requests = [];
        const cancellations = [];
        let active = false, cancelled = false;
        page.on('dialog', d => d.accept());
        await page.route('http://eduos.test/**', async route => {
            const req = route.request(), url = new URL(req.url());
            const name = url.pathname;
            if (!name.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            if (name === '/api/academic-setup/catalog')
                return reply(route, 200, { batches: [{
                    id: 10, name: 'Section A', academicYearName: '2026', isActive: true
                }] });
            if (name === '/api/academic-instruction/lesson-plans')
                return reply(route, 200, { page: 1, totalPages: 1, totalCount: 0, items: [] });
            if (name === '/api/academic-instruction/batches/10/lesson-offerings')
                return reply(route, 200, { page: 1, totalPages: 1, totalCount: 0, items: [] });
            if (name === '/api/academic-instruction/substitutions/page') {
                assert.equal(url.searchParams.get('pageSize'), '20');
                return reply(route, 200, { page: 1, totalPages: 1,
                    totalCount: active ? 1 : 0, items: active ? [{
                        id: 300, rowVersion: version, date: '2026-10-25',
                        batchName: 'Section A', subjectName: 'Mathematics',
                        timeSlotName: 'Period 2', originalTeacherName: 'Original teacher',
                        substituteEmployeeName: 'Substitute teacher', reason: '=SUM(1,1)',
                        isCancelled: cancelled, contextUnavailable: false
                    }] : [] });
            }
            if (name === '/api/academic-instruction/batches/10/substitution-routines') {
                assert.equal(url.searchParams.get('pageSize'), '50');
                if (url.searchParams.get('date') !== '2026-10-25')
                    return reply(route, 200, { page: 1, totalPages: 1, totalCount: 0, items: [] });
                const current = Number(url.searchParams.get('page'));
                const items = current === 1
                    ? Array.from({ length: 50 }, (_, i) => ({
                        id: i + 1, dayOfWeek: 0, subjectName: 'Subject ' + i,
                        timeSlotName: 'Period 1', originalTeacherName: 'Original teacher'
                    }))
                    : [{ id: 901, dayOfWeek: 0, subjectName: 'Mathematics',
                        timeSlotName: 'Period 2', originalTeacherName: 'Original teacher' }];
                return reply(route, 200, { page: current, totalPages: 2, totalCount: 51, items });
            }
            if (name === '/api/academic-routines/batches/10/instructors/page') {
                assert.equal(url.searchParams.get('pageSize'), '50');
                const current = Number(url.searchParams.get('page'));
                const items = current === 1
                    ? Array.from({ length: 50 }, (_, i) => ({
                        employeeReference: 'aaaaaaaa-aaaa-4aaa-8aaa-' + String(i + 1).padStart(12, '0'),
                        employeeCode: 'T-' + i, name: 'Teacher ' + i
                    }))
                    : [{ employeeReference: teacherRef, employeeCode: 'T-999',
                        name: 'Substitute teacher' }];
                return reply(route, 200, { page: current, totalPages: 2, totalCount: 51, items });
            }
            if (name === '/api/academic-instruction/substitutions' && req.method() === 'POST') {
                assert.equal(req.headers()['requestverificationtoken'], 'academic-instruction-csrf');
                const data = req.postDataJSON();
                assert.equal(data.routineEntryId, 901);
                assert.equal(data.substituteEmployeeReference, teacherRef);
                assert.equal(data.date, '2026-10-25');
                assert.match(data.clientRequestId, /^[0-9a-f-]{36}$/i);
                requests.push(data);
                if (requests.length <= 2) return reply(route, 503, null, 'Temporary connection error');
                active = true;
                return reply(route, 201, { id: 300, rowVersion: version });
            }
            if (name === '/api/academic-instruction/substitutions/300/cancel') {
                assert.equal(req.headers()['requestverificationtoken'], 'academic-instruction-csrf');
                assert.equal(req.postDataJSON().rowVersion, version);
                cancellations.push(true); cancelled = true;
                return reply(route, 200, { id: 300, isCancelled: true });
            }
            return reply(route, 404, null, 'Unexpected API ' + name);
        });
        await page.goto('http://eduos.test/AcademicInstruction/Index');
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/academic/instruction.js') });
        await page.locator('#instructionBatch option[value="10"]').waitFor({ state: 'attached' });
        await page.locator('#instructionBatch').selectOption('10');
        await page.waitForFunction(() => document.querySelector('#substitutionTeacherPage')?.textContent.includes('Page 1 / 2'));
        await page.locator('#substitutionDate').fill('2026-10-25');
        await page.waitForFunction(() => document.querySelector('#substitutionRoutinePage')?.textContent.includes('Page 1 / 2'));
        await page.locator('#substitutionRoutineNext').click();
        await page.locator('#substitutionRoutine option[value="901"]').waitFor({ state: 'attached' });
        await page.locator('#substitutionRoutine').selectOption('901');
        await page.locator('#substitutionTeacherNext').click();
        await page.locator('#substitutionTeacher option[value="' + teacherRef + '"]').waitFor({ state: 'attached' });
        await page.locator('#substitutionTeacher').selectOption(teacherRef);
        await page.locator('#substitutionReason').fill('Coverage');

        for (let i = 0; i < 2; i++) {
            await Promise.all([
                page.waitForResponse(r => r.url().endsWith('/api/academic-instruction/substitutions') && r.status() === 503),
                page.locator('#substitutionSave').click()
            ]);
            await page.waitForFunction(() => document.querySelector('#instructionAlert')?.textContent.includes('Temporary connection error'));
        }
        assert.equal(requests.length, 2);
        assert.equal(requests[0].clientRequestId, requests[1].clientRequestId,
            'Uncertain same-payload retries must retain the idempotency key');
        await page.locator('#substitutionReason').fill('=SUM(1,1)');
        await page.locator('#substitutionSave').click();
        await page.waitForFunction(() => document.querySelector('#instructionAlert')?.textContent.includes('Substitution scheduled.'));
        assert.equal(requests.length, 3);
        assert.notEqual(requests[1].clientRequestId, requests[2].clientRequestId,
            'Changed payload must use a fresh idempotency key');
        await page.locator('#substitutionRows button[data-action="cancelSub"]').click();
        await page.waitForFunction(() => document.querySelector('#substitutionRows')?.textContent.includes('Cancelled'));
        assert.equal(cancellations.length, 1);
        const downloadPromise = page.waitForEvent('download');
        await page.locator('#substitutionCsv').click();
        const download = await downloadPromise;
        const csv = fs.readFileSync(await download.path(), 'utf8');
        assert.match(csv, /"'=SUM\(1,1\)"/, 'CSV formula injection must be escaped');
        await page.close();
        console.log('Academic Instruction browser: PASS — paged routine/teacher, CSRF, retry key, cancellation rowversion, CSV safety.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
