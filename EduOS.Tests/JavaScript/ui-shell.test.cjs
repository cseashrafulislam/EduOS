'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const css = read('EduOS.App/wwwroot/css/site.css');
const js = read('EduOS.App/wwwroot/js/platform/ui-shell.js');
const { breadcrumbParts, notificationData } = require('../../EduOS.App/wwwroot/js/platform/ui-shell.js');
const site = read('EduOS.App/wwwroot/js/site.js');

test('shared breadcrumb reduces duplicate levels and never creates navigation routes', () => {
    assert.deepEqual(breadcrumbParts(['Management', 'Fees & Accounting', 'Fees & Accounting', 'Fee collection']),
        ['Management', 'Fees & Accounting', 'Fee collection']);
    assert.deepEqual(breadcrumbParts(['', 'Admissions & Students', null, 'New application']),
        ['Admissions & Students', 'New application']);
    assert.deepEqual(breadcrumbParts(['Dashboard', 'dashboard', '  Dashboard  ']), ['Dashboard']);
    assert.match(layout, /id="pageBreadcrumbs"[\s\S]*?hidden><\/nav>/);
    assert.match(js, /nav\[data-sidebar-section\] a\.nav-link\[aria-current="page"\]/);
    assert.match(js, /item\.textContent = label/);
    assert.match(js, /crumbs\.hidden = true/);
    assert.doesNotMatch(js, /\.innerHTML|localStorage|fetch\(|window\.location\.assign/);
});

test('shared toast messages are bounded, text-only and explicitly typed', () => {
    assert.deepEqual(notificationData('  Done  ', 'success'), { message: 'Done', kind: 'success' });
    assert.deepEqual(notificationData('<img onerror=alert(1)>', 'error'),
        { message: '<img onerror=alert(1)>', kind: 'error' });
    assert.deepEqual(notificationData('Review required', 'financial'),
        { message: 'Review required', kind: 'info' });
    assert.equal(notificationData('', 'error'), null);
    assert.equal(notificationData(null, 'error'), null);
    assert.equal(notificationData('x'.repeat(1500), 'warning').message.length, 800);
    assert.match(layout, /id="eduToastStack"/);
    assert.match(js, /text\.textContent = data\.message/);
    assert.match(js, /if \(!host \|\| !data \|\| !sameSession\(\)\) return null/);
    assert.match(js, /if \(host\.children\.length >= 3\)/);
    assert.match(js, /data\.kind === 'error' \? 'alert' : 'status'/);
});

test('one shared shell uses opt-in status, skeleton, empty and responsive presentation', () => {
    assert.match(layout, /<h1>@pageTitle<\/h1>/);
    assert.doesNotMatch(layout, /<p>@T\["ManageInstitution"\]<\/p>/);
    assert.match(layout, /ui-shell\.js" asp-append-version="true"/);
    assert.match(layout, /sidebar-navigation\.js" asp-append-version="true"[\s\S]*ui-shell\.js" asp-append-version="true"/);
    for (const selector of ['.edu-surface', '.edu-section-heading', '.edu-field-label',
        '.edu-state-panel', '.edu-state-panel--error', '.edu-skeleton', '.edu-toast', '.page-breadcrumbs']) {
        assert.ok(css.includes(selector), selector);
    }
    assert.match(css, /prefers-reduced-motion: reduce\) \{ \.edu-skeleton \{ animation: none/);
    assert.match(css, /data-workspace-theme="dark"\] :is\(\.edu-surface, \.edu-state-panel\)/);
    assert.match(css, /\.page-breadcrumbs,\s*\.user-info/);
    assert.doesNotMatch(css, /\.edu-toast--error\s*\{[^}]*\.table/);
});

test('mobile navigation traps keyboard focus and keeps inactive content inert', () => {
    assert.match(site, /isOpen = Boolean\(isOpen && !desktopMedia\.matches\)/);
    assert.match(site, /mainArea\.inert = true/);
    assert.match(site, /sidebar\.setAttribute\('aria-modal', 'true'\)/);
    assert.match(site, /event\.key === 'Tab' && sidebar\?\.classList\.contains\('show'\)/);
    assert.match(site, /event\.shiftKey && \(document\.activeElement === first/);
    assert.match(site, /event\.preventDefault\(\); last\.focus\(\)/);
    assert.match(site, /event\.preventDefault\(\); first\.focus\(\)/);
    assert.match(site, /window\.EduOSSessionTransitionPending\?\.\(\)/);
    assert.match(site, /if \(lastFocusedElement instanceof HTMLElement\) lastFocusedElement\.focus\(\)/);
    assert.match(css, /body\.sidebar-open\s*\{\s*overflow: hidden;/);
});
