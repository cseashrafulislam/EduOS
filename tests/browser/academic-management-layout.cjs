'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const index = read('EduOS.App/Views/AcademicManagement/Index.cshtml');
const prerequisites = read('EduOS.App/Views/AcademicManagement/Prerequisites.cshtml');
const cssBootstrap = path.join(root, 'EduOS.App/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css');
const cssSite = path.join(root, 'EduOS.App/wwwroot/css/site.css');
const cssAcademic = path.join(root, 'EduOS.App/wwwroot/css/academic/management-workspace.css');

function form(id, source) {
    const first = source.indexOf('<form id="' + id + '"');
    const last = source.indexOf('</form>', first);
    assert(first >= 0 && last > first, 'Missing form ' + id);
    return source.slice(first, last + '</form>'.length);
}

function catalogRow() {
    const first = prerequisites.indexOf('<div class="row g-3 align-items-end">');
    const last = prerequisites.indexOf('\n        </div>', first);
    assert(first >= 0 && last > first, 'Missing compact curriculum catalog layout');
    return prerequisites.slice(first, last + '\n        </div>'.length)
        .replace(/@\(\s*bn \? "পাঠ্যক্রমে ফিরুন" : "Back to Curriculum"\)/, 'Back to Curriculum');
}
function fixture(pageName) {
    const content = pageName === 'curriculum'
        ? '<div id="academicManagement"><section data-section="curricula" class="card"><div class="card-body">' +
          form('curriculumForm', index) + '</div></section></div>'
        : '<div id="prerequisiteWorkspace"><div class="card"><div class="card-body">' +
          catalogRow() + '</div></div><div class="card mt-3"><div class="card-body">' +
          form('prerequisiteForm', prerequisites) + '</div></div></div>';
    return '<!DOCTYPE html><html lang="en"><head><meta name="viewport" content="width=device-width, initial-scale=1"></head><body>' +
        '<main class="page-body">' + content + '</main></body></html>';
}

function intersect(a, b) {
    const width = Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left));
    const height = Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top));
    return width * height;
}

async function geometry(page, sel) {
    const rect = await page.locator(sel).boundingBox();
    if (!rect) return null;
    return {
        ...rect,
        left: rect.x,
        top: rect.y,
        right: rect.x + rect.width,
        bottom: rect.y + rect.height
    };
}
async function run() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        for (const width of [390, 768, 1280]) {
            for (const view of ['curriculum', 'prerequisites']) {
                const page = await browser.newPage({ viewport: { width, height: 850 } });
                await page.setContent(fixture(view));
                await page.addStyleTag({ path: cssBootstrap });
                await page.addStyleTag({ path: cssSite });
                await page.addStyleTag({ path: cssAcademic });
                const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
                assert(overflow < 2, view + ' horizontal document overflow at ' + width + 'px: ' + overflow);
                if (view === 'curriculum') {
                    const check = await geometry(page, 'label:has(#curriculumCurrent)');
                    const action = await geometry(page, '#curriculumForm button[type="submit"]');
                    assert(check && action, 'Curriculum action geometry missing');
                    assert.equal(intersect(check, action), 0, 'Current curriculum intersects Create at ' + width);
                    assert(check.height >= 40, 'Current curriculum touch target too small at ' + width);
                    assert(action.height >= 40, 'Create curriculum touch target too small at ' + width);
                    if (width <= 575) assert(check.y + check.height <= action.y, 'Mobile curriculum actions should stack');
                } else {
                    const first = await geometry(page, '#prerequisiteWorkspace .card:first-child');
                    const check = await geometry(page, 'label:has(#prerequisiteMandatory)');
                    const select = await geometry(page, '#prerequisiteRequired');
                    const submit = await geometry(page, '#prerequisiteForm button[type="submit"]');
                    const back = await geometry(page, '.academic-back-button');
                    const catalog = await geometry(page, '#prerequisiteLoadCatalog');
                    assert(first && check && select && submit && back && catalog);
                    assert(first.y < 60, 'Back button creates top whitespace at ' + width);
                    assert.equal(intersect(check, select), 0, 'Mandatory overlaps subject selection at ' + width);
                    assert.equal(intersect(check, submit), 0, 'Mandatory overlaps Add link at ' + width);
                    assert.equal(intersect(back, catalog), 0, 'Back overlaps catalog button at ' + width);
                    assert(check.height >= 40 && back.height >= 40, 'Checkbox/back target too small at ' + width);
                }
                await page.close();
            }
        }
        console.log('Academic Curriculum + Prerequisites: 390/768/1280 responsive geometry PASS');
    } finally {
        await browser.close();
    }
}
run().catch(error => { console.error(error); process.exitCode = 1; });
