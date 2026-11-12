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
const { breadcrumbParts, breadcrumbSelection, contextualSetupLink, contextualNextLink, canonicalShellRoute, shellLinkVisible, notificationData, notificationDismissalMs } = require('../../EduOS.App/wwwroot/js/platform/ui-shell.js');
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
        closest: selector => selector.includes('[hidden]') && hidden ? {} : null
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

test('hidden, inert and disabled sidebar links cannot become active breadcrumb or setup action', () => {
    const link = (href, current, active, blocked) => ({
        getAttribute: name => name === 'href' ? href :
            name === 'aria-current' ? (current ? 'page' : null) :
            name === 'aria-disabled' ? (blocked === 'disabled' ? 'true' : null) : null,
        classList: { contains: name => name === 'active' && active },
        closest: selector => blocked && selector.includes('[hidden]') ? {} : null
    });
    const hidden = link('/Students', true, true, 'hidden');
    const inert = link('/SmartSetup/Index', false, false, 'inert');
    const disabled = link('/SmartSetup/Index', false, false, 'disabled');
    const visible = link('/Dashboard/Index', false, true, null);
    assert.equal(breadcrumbSelection([hidden, visible]), visible);
    assert.equal(breadcrumbSelection([hidden]), null);
    assert.equal(contextualSetupLink([visible, inert, disabled], visible), null);
    assert.match(js, /shellSessionValid/);
    assert.match(js, /window.addEventListener\\('pageshow'/);
    assert.match(js, /document.addEventListener\\('visibilitychange'/);
});

test('shared notices preserve critical and keyboard-focused messages when toast stack is full', () => {
    assert.match(js, /node.matches\\('\\.edu-toast--error, \\.edu-toast--warning'\\)/);
    assert.match(js, /node.contains\\(document.activeElement\\)/);
    assert.match(js, /if \\(!removable\\) return null/);
    assert.match(js, /shellSessionValid\\(\\)/);
});

test('opt-in next-page shortcut never invents routes or crosses authorized menu sections', () => {
    function fixture(hrefs, currentIndex, optIn = true) {
        const nav = { querySelectorAll: () => links };
        const links = hrefs.map((href, index) => ({
            getAttribute: key => key === 'href' ? href :
                key === 'aria-current' ? (index === currentIndex ? 'page' : null) : null,
            closest: selector => selector.includes('nav[data-sidebar-section]')
                ? (optIn ? nav : null) : null,
            querySelector: () => null
        }));
        return links;
    }
    const allowed = fixture(['/Students', '/Students/Index', '/Students/Details'], 0);
    assert.equal(contextualNextLink(allowed[0]), allowed[2]);
    assert.equal(contextualNextLink(allowed[2]), null);
    assert.equal(contextualNextLink(fixture(['/Students', '//evil.test/redirect', '/Admissions'], 0)[0]).getAttribute('href'), '/Admissions');
    assert.equal(contextualNextLink(fixture(['/Students', '/Students/../Admin', '/Admissions'], 0)[0]).getAttribute('href'), '/Admissions');
    assert.equal(contextualNextLink(fixture(['/Students', '/Admissions'], 0, false)[0]), null);
    assert.equal(contextualNextLink(fixture(['/Students', '/Admissions'], 1)[0]), null);
    assert.match(js, /contextualNextLink\(current\)/);
    assert.match(js, /shortcut\.href = next\.getAttribute\('href'\)/);
    assert.match(css, /shell-context-shortcut--next/);
});

test('shared shell rejects URL normalization tricks and skips MVC Index alias next steps', () => {
    assert.equal(canonicalShellRoute('/Students'), '/students');
    assert.equal(canonicalShellRoute('/Students/Index?campus=2'), '/students');
    assert.equal(canonicalShellRoute('/SmartSetup/Index'), '/smartsetup');
    for (const value of ['//evil.test', 'https://evil.test/Students', '/Students/../Admin',
        '/Students/%2e%2e/Admin', '/Students/%2fAdmin', '/Students\\\\Admin', '/Students/Index%0a']) {
        assert.equal(canonicalShellRoute(value), null, value);
    }
    const nav = { querySelectorAll: () => links };
    const links = ['/Students', '/Students/Index', '/Students/Details', '/Admissions'].map((href, i) => ({
        getAttribute: key => key === 'href' ? href : key === 'aria-current' ? (i === 0 ? 'page' : null) : null,
        closest: selector => selector.includes('nav[data-sidebar-section]') ? nav : null
    }));
    assert.equal(contextualNextLink(links[0]), links[2]);
    assert.match(js, /eduos:workspace-appearance-changed/);
});

test('shared link visibility excludes hidden, inert, aria-hidden and disabled shell destinations', () => {
    const link = (attributes = {}, ancestor = null, rects = [1]) => ({
        getAttribute: key => Object.hasOwn(attributes, key) ? attributes[key] : null,
        closest: () => ancestor,
        getClientRects: () => rects
    });
    assert.equal(shellLinkVisible(link()), true);
    assert.equal(shellLinkVisible(link({ 'aria-disabled': 'true' })), false);
    assert.equal(shellLinkVisible(link({ hidden: '' })), false);
    assert.equal(shellLinkVisible(link({ inert: '' })), false);
    assert.equal(shellLinkVisible(link({}, {})), false);
    assert.equal(shellLinkVisible(link({}, null, [])), false);
    assert.equal(shellLinkVisible(null), false);
    assert.match(js, /\[aria-hidden="true"\]/);
});

test('shared navigation excludes CSS-hidden and aria-disabled destinations before breadcrumb/next-step selection', () => {
    const link = (attrs = {}, ancestor = null) => ({
        nodeType: 1,
        getAttribute: key => Object.hasOwn(attrs, key) ? attrs[key] : null,
        closest: () => ancestor,
        getClientRects: () => [1]
    });
    assert.equal(shellLinkVisible(link({ 'aria-hidden': 'true' })), false);
    assert.equal(shellLinkVisible(link({ disabled: '' })), false);
    assert.equal(shellLinkVisible(link({}, {})), false);
    const original = global.getComputedStyle;
    try {
        global.getComputedStyle = () => ({ visibility: 'hidden', display: 'block' });
        assert.equal(shellLinkVisible(link()), false);
        global.getComputedStyle = () => ({ visibility: 'visible', display: 'none' });
        assert.equal(shellLinkVisible(link()), false);
        global.getComputedStyle = () => ({ visibility: 'visible', display: 'block' });
        assert.equal(shellLinkVisible(link()), true);
    } finally {
        if (original === undefined) delete global.getComputedStyle;
        else global.getComputedStyle = original;
    }
    assert.match(js, /getComputedStyle\(link\)/);
    assert.match(js, /\[aria-disabled="true"\]/);
});

test('shared field-state tokens cover validated pending/invalid/valid states across appearance modes', () => {
    for (const state of ['invalid', 'valid', 'pending']) {
        assert.ok(css.includes('.edu-field-state[data-state="' + state + '"]'));
    }
    assert.match(css, /:root\[data-workspace-theme="dark"\] \.edu-field-state/);
    assert.match(css, /:root\[data-workspace-theme="system"\] \.edu-field-state/);
    assert.match(css, /\.edu-field-state \{[^}]*overflow-wrap: anywhere/);
    assert.match(js, /const focused = toast\.contains\(document\.activeElement\)/);
    assert.match(js, /if \(focused && sameSession\(\)\)/);
});
