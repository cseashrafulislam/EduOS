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
const { breadcrumbParts, breadcrumbSelection, contextualSetupLink, notificationData, notificationDismissalMs } = require('../../EduOS.App/wwwroot/js/platform/ui-shell.js');
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

test('short transient notices never auto-dismiss while read and long/critical notices stay until closed', () => {
    assert.equal(notificationDismissalMs(notificationData('OK', 'success')), 5500);
    assert.equal(notificationDismissalMs(notificationData('Review required', 'info')), 5500);
    assert.ok(notificationDismissalMs(notificationData('x'.repeat(120), 'success')) >= 9000);
    assert.equal(notificationDismissalMs(notificationData('x'.repeat(121), 'success')), null);
    assert.equal(notificationDismissalMs(notificationData('Payment declined', 'error')), null);
    assert.equal(notificationDismissalMs(notificationData('Approval pending', 'warning')), null);
    assert.equal(notificationDismissalMs(null), null);
    assert.match(js, /toast\.addEventListener\('pointerenter', pauseTimer\)/);
    assert.match(js, /toast\.addEventListener\('focusin', pauseTimer\)/);
    assert.match(js, /toast\.contains\(document\.activeElement\)/);
    assert.match(js, /toast\.setAttribute\('aria-atomic', 'true'\)/);
});

test('shared public error pages expose localized titles and safe recovery for 403, 404 and 500', () => {
    const pages = [
        ['AccessDenied', '403', 'AccessDeniedTitle', 'AccessDeniedMessage'],
        ['NotFound', '404', 'PageNotFoundTitle', 'PageNotFoundMessage'],
        ['ServerError', '500', 'ServerErrorTitle', 'ServerErrorMessage']
    ];
    const publicLayout = read('EduOS.App/Views/Shared/_PublicLayout.cshtml');
    assert.match(publicLayout, /<main class="public-main" id="main-content" tabindex="-1">/);
    assert.match(publicLayout, /<a class="skip-link" href="#main-content">/);
    for (const [name, status, title, message] of pages) {
        const markup = read('EduOS.App/Views/Error/' + name + '.cshtml');
        assert.match(markup, /Layout = "_PublicLayout"/);
        assert.match(markup, /aria-labelledby="errorTitle" aria-describedby="errorDescription"/);
        assert.match(markup, /<p id="errorDescription">/);
        assert.ok(markup.includes('aria-hidden="true">' + status + '</div>'));
        assert.ok(markup.includes('@T["' + title + '"]'));
        assert.ok(markup.includes('@T["' + message + '"]'));
        assert.match(markup, /href="\/" class="btn/);
        assert.doesNotMatch(markup, /ViewBag\.Exception|StackTrace|Exception\.Message|Model\.Exception/);
    }
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

test('breadcrumbs select the server-authorized active destination without guessing URL segments', () => {
    const link = (current, active) => ({
        getAttribute: name => name === 'aria-current' ? current : null,
        classList: { contains: name => name === 'active' && active }
    });
    const inactive = link(null, false), serverActive = link(null, true), current = link('page', false);
    assert.equal(breadcrumbSelection([inactive, serverActive, current]), current);
    assert.equal(breadcrumbSelection([inactive, serverActive]), serverActive);
    assert.equal(breadcrumbSelection([inactive]), null);
    assert.equal(breadcrumbSelection([]), null);
    assert.match(js, /eduos:sidebar-navigation-ready/);
    assert.doesNotMatch(js, /location\.pathname\.split/);
});

test('contextual Smart Setup shortcut reuses only an authorized sidebar link, never a guessed route', () => {
    const link = (href, hidden = false) => ({
        getAttribute: key => key === 'href' ? href : null,
        closest: selector => selector === '[hidden]' && hidden ? {} : null
    });
    const student = link('/Students/Index');
    const setup = link('/SmartSetup/Index');
    const blocked = link('/SmartSetup/Index', true);
    assert.equal(contextualSetupLink([student, setup], student), setup);
    assert.equal(contextualSetupLink([student, setup], setup), null);
    assert.equal(contextualSetupLink([student, blocked], student), null);
    assert.equal(contextualSetupLink([student, link('https://evil.example/SmartSetup')], student), null);
    assert.equal(contextualSetupLink([student, link('//evil.example/SmartSetup')], student), null);
    assert.equal(contextualSetupLink([student, link('/SmartSetup/Index/../../Other')], student), null);
    assert.equal(contextualSetupLink([], null), null);
    assert.match(layout, /id="shellContextActions"[\\s\\S]*?hidden><\\/nav>/);
    assert.match(js, /contextual\\.replaceChildren\\(\\)/);
    assert.match(js, /contextual\\.hidden = true/);
    assert.match(js, /window\\.EduOSSessionTransitionPending/);
    assert.match(js, /shortcut\\.href = setup\\.getAttribute\\('href'\\)/);
    assert.match(js, /eduos:sidebar-navigation-ready/);
    assert.match(css, /\\.shell-context-shortcut:focus-visible/);
    assert.match(css, /max-width: 575\\.98px/);
    assert.match(css, /data-workspace-theme="dark"\\] \\.shell-context-shortcut/);
});
