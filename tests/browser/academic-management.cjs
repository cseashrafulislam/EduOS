'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const razor = fs.readFileSync(path.join(root, 'EduOS.App/Views/AcademicManagement/Index.cshtml'), 'utf8');
const from = razor.indexOf('<div id="academicManagement"');
const until = razor.indexOf('@section Scripts');
assert(from >= 0 && until > from, 'AcademicManagement markup missing');
const html = '<!doctype html><html><head><title>Academic UI</title></head><body>' +
    razor.slice(from, until).replace('@Html.AntiForgeryToken()',
        '<input name="__RequestVerificationToken" value="browser-test-csrf">') + '</body></html>';

const years = [{
    id: 101, campusId: null, name: 'Academic 2026', code: 'Y2026', cycleType: 1,
    startDate: '2026-01-01', endDate: '2026-12-31',
    isCurrent: true, isActive: true, rowVersion: 'AQIDBAUGBwg='
}];
const terms = [];
const campuses = [{ id: 201, name: 'Main Campus', code: 'MAIN', isActive: true }];
const catalog = {
    programs: [], levels: [], tracks: [], subjects: [], curricula: [],
    curriculumSubjects: [], batches: [], rooms: []
};
const mutations = [];

async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        page.on('pageerror', error => { throw error; });
        page.on('dialog', dialog => dialog.accept());
        await page.route('http://eduos.test/**', async route => {
            const request = route.request();
            const url = new URL(request.url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            if (request.method() === 'GET') {
                const data = {
                    '/api/institution-onboarding/academic-years': years,
                    '/api/institution-onboarding/academic-terms': terms,
                    '/api/institution-onboarding/campus-list': campuses,
                    '/api/academic-setup/catalog': catalog
                }[url.pathname];
                return route.fulfill({ status: data ? 200 : 404, contentType: 'application/json',
                    body: JSON.stringify({ success: !!data, data, message: 'Not found' }) });
            }
            assert.equal(request.headers()['requestverificationtoken'], 'browser-test-csrf');
            const data = request.postDataJSON() || {};
            mutations.push({ path: url.pathname, method: request.method(), data });
            let result = {};
            if (url.pathname.endsWith('/academic-year')) {
                if (data.id) Object.assign(years.find(x => x.id === data.id), data);
                else years.push({ ...data, id: 102, rowVersion: 'AQIDBAUGBwg=' });
            }
            if (url.pathname.endsWith('/academic-term'))
                terms.push({ ...data, id: 301, rowVersion: 'AQIDBAUGBwg=' });
            if (url.pathname.endsWith('/programmes'))
                catalog.programs.push({ ...data, id: 401, rowVersion: 'AQIDBAUGBwg=' });
            if (url.pathname.endsWith('/levels'))
                catalog.levels.push({ ...data, id: 501, rowVersion: 'AQIDBAUGBwg=' });
            if (url.pathname.endsWith('/subjects'))
                catalog.subjects.push({ ...data, id: 601, rowVersion: 'AQIDBAUGBwg=' });
            if (url.pathname.endsWith('/curricula'))
                catalog.curricula.push({ ...data, id: 701, rowVersion: 'AQIDBAUGBwg=' });
            if (url.pathname.endsWith('/batches'))
                catalog.batches.push({ ...data, id: 801, rowVersion: 'AQIDBAUGBwg=' });
            if (url.pathname.endsWith('/subjects') && url.pathname.includes('/curricula/'))
                catalog.curriculumSubjects.push({ ...data, id: 901, academicCurriculumId: 701 });
            return route.fulfill({ status: 200, contentType: 'application/json',
                body: JSON.stringify({ success: true, data: result }) });
        });
        await page.goto('http://eduos.test/AcademicManagement/Index');
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/academic/management.js') });
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/academic/management-forms.js') });
        await page.waitForSelector('#yearRows tr');
        assert.match(await page.locator('#yearRows').innerText(), /Academic 2026/);

        await page.locator('[data-panel="classes"]').click();
        await page.locator('#programName').fill('Secondary');
        await page.locator('#programCode').fill('SEC');
        await page.locator('#programForm button[type="submit"]').click();
        await page.waitForFunction(() => document.querySelector('#programList')?.textContent.includes('Secondary'));
        assert.equal(mutations.find(x => x.path.endsWith('/programmes')).data.code, 'SEC');

        await page.locator('#levelName').fill('Grade Nine');
        await page.locator('#levelCode').fill('G9');
        await page.locator('#levelNumber').fill('9');
        await page.locator('#levelForm button[type="submit"]').click();
        await page.waitForFunction(() => document.querySelector('#levelRows')?.textContent.includes('Grade Nine'));

        await page.locator('[data-panel="subjects"]').click();
        await page.locator('#subjectName').fill('Mathematics');
        await page.locator('#subjectCode').fill('MATH');
        await page.locator('#subjectForm button[type="submit"]').click();
        await page.waitForFunction(() => document.querySelector('#subjectRows')?.textContent.includes('Mathematics'));

        await page.locator('[data-panel="curricula"]').click();
        await page.locator('#curriculumName').fill('Secondary Curriculum 2026');
        await page.locator('#curriculumCode').fill('SC-2026');
        await page.locator('#curriculumFrom').fill('2026-01-01');
        await page.locator('#curriculumForm button[type="submit"]').click();
        try {
            await page.waitForFunction(() => document.querySelector('#curriculumRows')?.textContent.includes('Secondary Curriculum'),
                { timeout: 8000 });
        } catch (error) {
            console.error('Curriculum UI diagnostic:', JSON.stringify({
                mutations, curricula: catalog.curricula,
                notice: await page.locator('#academicNotice').innerText(),
                selectedProgram: await page.locator('#curriculumProgram').inputValue(),
                rows: await page.locator('#curriculumRows').innerText(),
                formValid: await page.locator('#curriculumForm').evaluate(x => x.checkValidity())
            }, null, 2));
            throw error;
        }

        await page.locator('#mappingMarks').fill('100');
        await page.locator('#mappingPassMarks').fill('33');
        await page.locator('#curriculumSubjectForm button[type="submit"]').click();
        await page.waitForFunction(() => document.querySelector('#curriculumRows')?.textContent.includes('1'));
        assert(mutations.some(x => x.path.endsWith('/curricula/701/subjects')));
        const scriptCount = await page.locator('#academicManagement script').count();
        assert.equal(scriptCount, 0);
        console.log('Academic browser smoke: PASS — DOM, tenant admin forms, CSRF, CRUD and curriculum mapping.');
    } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
