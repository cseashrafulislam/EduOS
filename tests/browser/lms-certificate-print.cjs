'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root,
    'EduOS.App/Views/Learning/CertificatePrint.cshtml'), 'utf8');
const script = fs.readFileSync(path.join(root,
    'EduOS.App/wwwroot/js/learning/certificate-print.js'), 'utf8');
const css = view.match(/<style>([\s\S]*?)<\/style>/)?.[1]?.replace(/@@/g, '@');
const main = view.match(/<main[\s\S]*?<\/main>/)?.[0];
const actions = view.match(/<div class="actions">[\s\S]*?<\/div>/)?.[0];
assert.ok(css && main && actions, 'Use actual certificate Razor print markup and CSS');
assert.match(css, /@page\s*\{\s*size:\s*A4 portrait/);

const fields = {
    StudentName: 'Sample Learner',
    StudentCode: 'STU-0001',
    CourseTitle: 'Verified Completion Course',
    CourseCode: 'LMS-001',
    Revision: '1',
    CompletedLessons: '3',
    RequiredLessons: '3',
    GradedAssignments: '2',
    RequiredAssignments: '2',
    PassedQuizzes: '1',
    RequiredQuizzes: '1',
    Reference: '11111111-2222-4333-8444-555555555555'
};

function render(fragment, bn) {
    const html = fragment
        .replace(/@\(bn\s*\?\s*"([^"]+)"\s*:\s*"([^"]+)"\)/g,
            (_, bangla, english) => bn ? bangla : english)
        .replace(/@Model\.(\w+)/g, (_, field) => {
            assert.ok(Object.hasOwn(fields, field), 'Unexpected certificate field: ' + field);
            return fields[field];
        })
        .replace(/@completed/g, '29 Oct 2026')
        .replace(/@issued/g, '30 Oct 2026');
    assert.doesNotMatch(html, /@Model\.|@\(bn/, 'No Razor expressions left in print fixture');
    return html;
}

async function check(browser, bn, width) {
    const page = await browser.newPage({ viewport: { width, height: 820 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const html = '<!doctype html><html lang="' + (bn ? 'bn' : 'en') +
        '"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' +
        '<style>' + css + '</style></head><body>' +
        render(actions, bn) + render(main, bn) + '</body></html>';
    try {
        await page.setContent(html);
        await page.evaluate(() => {
            window.__printCount = 0;
            window.print = () => { window.__printCount++; };
        });
        await page.addScriptTag({ content: script });
        assert.equal(await page.evaluate(() => window.__printCount), 0,
            'Certificate must not print without user action');
        await page.locator('#lmsPrintNow').click();
        assert.equal(await page.evaluate(() => window.__printCount), 1,
            'Accessible print action must invoke browser print');
        assert.equal(await page.locator('main').isVisible(), true);
        assert.ok(await page.evaluate(() =>
            document.documentElement.scrollWidth <= window.innerWidth + 1),
            'Certificate must not overflow the mobile viewport');
        assert.ok((await page.locator('main').innerText()).includes(
            bn ? 'কোর্স সমাপ্তির সনদ' : 'Certificate of Course Completion'));
        await page.emulateMedia({ media: 'print' });
        assert.equal(await page.locator('.actions').isVisible(), false,
            'Print/PDF must omit action buttons');
        const pdf = await page.pdf({ format: 'A4', printBackground: true });
        assert.equal(pdf.subarray(0, 5).toString(), '%PDF-');
        assert.ok(pdf.length > 1500, 'A4 print/PDF output must not be empty');
        assert.deepEqual(errors, []);
        console.log('LMS A4 certificate ' + (bn ? 'Bangla' : 'English') +
            ' print/PDF Chromium fixture PASS');
    } finally {
        await page.close();
    }
}

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await check(browser, false, 390);
        await check(browser, true, 1280);
    } finally {
        await browser.close();
    }
})().catch(e => { console.error(e); process.exitCode = 1; });
