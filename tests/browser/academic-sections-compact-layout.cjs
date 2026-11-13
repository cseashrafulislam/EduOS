'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const sectionsRazor = read('EduOS.App/Views/AcademicManagement/SectionRegister.cshtml');
const indexRazor = read('EduOS.App/Views/AcademicManagement/Index.cshtml');
const cssFiles = [
    'EduOS.App/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css',
    'EduOS.App/wwwroot/css/site.css',
    'EduOS.App/wwwroot/css/academic/management-workspace.css'
];
const javascript = path.join(root, 'EduOS.App/wwwroot/js/academic/management-workspace.js');
function english(raw) {
    return raw
        .replace(/@\(\s*bn\s*\?\s*"[^"]*"\s*:\s*"([^"]*)"\s*\)/g, '$1')
        .replace('@Html.AntiForgeryToken()', '')
        .replace(/@\(\s*section\s*==\s*"sections"\s*\?\s*""\s*:\s*"d-none"\s*\)/g, '');
}
function fixture(kind) {
    let content;
    if (kind === 'sections') {
        const a = sectionsRazor.indexOf('<div id="academicSectionsPage"');
        const b = sectionsRazor.indexOf('@section Scripts', a);
        assert(a >= 0 && b > a);
        content = english(sectionsRazor.slice(a, b));
    } else {
        const a = indexRazor.indexOf('<section data-section="sections"');
        const b = indexRazor.indexOf('<section data-section="subjects"', a);
        assert(a >= 0 && b > a);
        content = '<div id="academicManagement" data-active-feature="sections">' +
            english(indexRazor.slice(a, b)) + '</div>';
    }
    return '<!DOCTYPE html><html lang="en"><head><meta name="viewport" content="width=device-width,initial-scale=1"></head>' +
        '<body><main class="page-body">' + content + '</main></body></html>';
}
function rect(page, selector) {
    return page.locator(selector).first().boundingBox();
}
async function run() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        for (const width of [390, 768, 1280]) {
            for (const kind of ['sections', 'batches']) {
                const page = await browser.newPage({ viewport: { width, height: 900 } });
                const errors = [];
                page.on('pageerror', err => errors.push(err.message));
                await page.setContent(fixture(kind));
                for (const p of cssFiles)
                    await page.addStyleTag({ path: path.join(root, p) });
                await page.addScriptTag({ path: javascript });
                const rootId = kind === 'sections' ? '#academicSectionsPage' : '#academicManagement';
                const formId = kind === 'sections' ? '#sectionCreateForm' : '#batchCreateForm';
                const bar = page.locator(rootId + ' .academic-editor-bar--split');
                const related = bar.locator('.academic-related-link');
                const toggle = bar.locator('button[aria-expanded]');
                assert.equal(await related.count(), 1, kind + ' related navigation must share the add toolbar');
                assert.equal(await toggle.count(), 1, kind + ' must have one add/close action');
                assert.equal(await page.locator(rootId + ' .academic-actions').count(), 0,
                    kind + ' must not leave a standalone action strip');
                assert.equal(await page.locator(formId).evaluate(e => e.hidden), true,
                    kind + ' editor starts collapsed');
                if (kind === 'sections') {
                    assert.equal(await page.locator(rootId + ' > .card').count(), 1,
                        'Sections must use one directory/editor card');
                    const card = await rect(page, rootId + ' > .card');
                    const main = await rect(page, 'main.page-body');
                    assert(card.y - main.y < 40, 'Sections starts too far below header at ' + width);
                    const actions = await rect(page, rootId + ' .academic-editor-bar');
                    const filter = await rect(page, '#sectionFilter');
                    assert(filter.y - (actions.y + actions.height) < 38,
                        'Sections has a blank strip above the register at ' + width);
                }
                const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
                assert(overflow < 2, kind + ' document horizontally overflows at ' + width);
                await toggle.click();
                assert.equal(await page.locator(formId).evaluate(e => e.hidden), false);
                const barRect = await rect(page, rootId + ' .academic-editor-bar');
                const fieldRect = await rect(page, formId + ' .form-control, ' + formId + ' .form-select');
                assert(fieldRect.y - (barRect.y + barRect.height) < 52,
                    kind + ' has an oversized empty band above form fields at ' + width);
                assert.equal(errors.length, 0, kind + ' runtime JS errors: ' + errors.join('; '));
                await page.close();
            }
        }
        console.log('Academic Sections/Batches compact toolbar + editor geometry PASS at 390/768/1280px');
    } finally {
        await browser.close();
    }
}
run().catch(e => { console.error(e); process.exitCode = 1; });
