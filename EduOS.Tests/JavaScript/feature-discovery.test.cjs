const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const styles = read('EduOS.App/wwwroot/css/site.css');
const script = read('EduOS.App/wwwroot/js/platform/feature-discovery.js');
const sidebarScript = read('EduOS.App/wwwroot/js/sidebar-navigation.js');
const { navigationLabelFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const { safeDestination, destinationIdentity, makeCatalog, filterCatalog, scopedState, remember, contextStatus } =
    require('../../EduOS.App/wwwroot/js/platform/feature-discovery.js');

const catalog = makeCatalog([
    { href: '/Admissions/Index', label: 'Admissions', section: 'Academics', group: 'Students' },
    { href: '/Students/Index', label: 'Students', section: 'Academics', group: 'Students' },
    { href: '/FeeOperations/Index', label: 'Fee collection', section: 'Management', group: 'Finance' },
    { href: '/Attendance/Index', label: 'Attendance', section: 'Academics', group: 'Classes' },
    { href: '/Admissions/Index', label: 'Duplicate link', section: 'Other' },
    { href: '//evil.example/phish', label: 'External', section: 'Other' },
    { href: '/Students/Index?private=123', label: 'Private query', section: 'Other' }
]);

test('feature catalog is deduplicated and built only from safe relative authorized links', () => {
    assert.equal(catalog.length, 4);
    assert.deepEqual(catalog.map(item => item.href),
        ['/Admissions/Index', '/Students/Index', '/FeeOperations/Index', '/Attendance/Index']);
    assert.equal(safeDestination('/StudentPortal/Index#learnerConsents'),
        '/StudentPortal/Index#learnerConsents');
    for (const bad of ['//evil.example', 'https://evil.example', '/?tenant=7',
        '/Students/Index?student=1', '/\\evil', '/Students/\nIndex', '/', '#fragment']) {
        assert.equal(safeDestination(bad), null, bad);
    }
});

test('search matches feature and module group with a stable bounded result set', () => {
    assert.deepEqual(filterCatalog(catalog, 'fee').map(x => x.href), ['/FeeOperations/Index']);
    assert.deepEqual(filterCatalog(catalog, 'finance').map(x => x.href), ['/FeeOperations/Index']);
    assert.deepEqual(filterCatalog(catalog, 'academics students').map(x => x.href),
        ['/Admissions/Index', '/Students/Index']);
    assert.equal(filterCatalog(catalog, 'unknown').length, 0);
    assert.equal(filterCatalog(catalog, '', 2).length, 2);
});

test('favorite and recent shortcuts never cross session, tenant or current permissions', () => {
    const saved = { version: 1, tenant: '22', session: 'session-a',
        favorites: ['/Admissions/Index', '/Admissions/Index', '/Secret/Index', '/FeeOperations/Index'],
        recent: ['/Students/Index', '/Secret/Index'] };
    const current = scopedState(saved, '22', 'session-a', catalog);
    assert.deepEqual(current.favorites, ['/Admissions/Index', '/FeeOperations/Index']);
    assert.deepEqual(current.recent, ['/Students/Index']);
    assert.deepEqual(scopedState(saved, '23', 'session-a', catalog).favorites, []);
    assert.deepEqual(scopedState(saved, '22', 'session-b', catalog).recent, []);
    assert.deepEqual(scopedState(saved, '', 'session-a', catalog).favorites, []);
    assert.deepEqual(remember(current.favorites, '/FeeOperations/Index'),
        ['/FeeOperations/Index', '/Admissions/Index']);
});

test('global feature search reuses server-rendered entitlement and role gated sidebar', () => {
    assert.match(layout, /OptionalModuleNavigation\.GetAvailableAsync/);
    assert.match(layout, /V2RolePortalNavigation\.GetLinks/);
    assert.match(script, /sidebar\.querySelectorAll\('nav\[data-sidebar-section\] a\.nav-link'\)/);
    assert.match(layout, /data-feature-discovery-link[^>]+href="\/workspace"/);
    assert.match(script, /document\.querySelectorAll\('a\[data-feature-discovery-link\]'\)/);
    assert.match(layout, /id="featureSearchOpen"/);
    assert.match(layout, /id="sidebarFeatureSearchOpen"/);
    assert.match(layout, /id="featureSearchDialog"/);
    assert.match(layout, /id="featureSearchInput"/);
    assert.match(layout, /feature-discovery\.js" asp-append-version="true"/);
    assert.match(layout, /aria-haspopup="dialog"/);
    assert.match(layout, /type="search"/);
    assert.match(layout, /@if \(User\?\.Identity\?\.IsAuthenticated == true\)/);
    assert.match(script, /for \(const button of triggers\) button\.addEventListener\('click'/);
    assert.match(script, /lastOpener\?\.focus\(\)/);
    assert.match(styles, /\.sidebar-feature-search:focus-visible/);
    assert.doesNotMatch(script, /innerHTML|eval\(|localStorage|fetch\(/);
});

test('discovery supports bilingual mobile, keyboard, safe text and theme-aware presentation', () => {
    assert.match(script, /'পছন্দের ফিচার'/);
    assert.match(script, /'Favorites'/);
    assert.match(script, /event\.ctrlKey \|\| event\.metaKey/);
    assert.match(script, /dialog\.showModal\(\)/);
    assert.match(script, /textContent = entry\.label/);
    assert.match(script, /results\.replaceChildren\(\)/);
    assert.match(script, /aria-pressed/);
    assert.match(script, /sessionStorage\.setItem\(key/);
    assert.match(styles, /\.feature-search-dialog::backdrop/);
    assert.match(styles, /data-workspace-theme="dark"\] \.feature-search-dialog/);
    assert.match(styles, /prefers-color-scheme: dark/);
    assert.match(styles, /max-width: 767\.98px/);
    assert.match(styles, /\.feature-search-row:focus-within/);
});

test('feature discovery fails closed on tenant/session change and temporary session shield', () => {
    assert.equal(contextStatus('22', 'A', '22', 'A', false, false), 'ready');
    assert.equal(contextStatus('22', 'A', '23', 'A', false, false), 'changed');
    assert.equal(contextStatus('22', 'A', '22', 'B', false, false), 'changed');
    assert.equal(contextStatus('22', 'A', '22', 'A', true, false), 'changed');
    assert.equal(contextStatus('22', 'A', '22', 'A', false, true), 'verifying');
    assert.equal(contextStatus('', 'A', '', 'A', false, false), 'changed');
    assert.equal(contextStatus('22', '', '22', '', false, false), 'changed');
    assert.match(script, /typeof window\.EduOSVerifySessionContext !== 'function'/);
    assert.match(script, /invalidated = true;\s*currentContext\(\);\s*return false;/);
    assert.match(layout, /session-context-guard\.js" asp-append-version="true"[\s\S]*feature-discovery\.js" asp-append-version="true"/);
    assert.match(script, /await verifiedContext\(\)/);
    assert.match(script, /window\.location\.assign\(entry\.href\)/);
    assert.match(script, /event\.metaKey \|\|/);
    assert.match(script, /sessionStorage\.removeItem\(key\)/);
    assert.match(script, /triggers\.forEach\(button => \{ button\.disabled = true; \}\)/);
    assert.match(script, /document\.addEventListener\('visibilitychange'/);
    assert.doesNotMatch(script, /window\.location\.replace\(entry\.href\)/);
});


test('feature search inherits the canonical bilingual sidebar labels, including icon-only links', () => {
    for (const [route, label] of [
        ['/Students/Index', 'শিক্ষার্থী তালিকা'],
        ['/Admissions/Index', 'ভর্তির আবেদন'],
        ['/PayrollOperations/Index', 'বেতন ব্যবস্থাপনা'],
        ['/AccountingReports/Index', 'জেনারেল লেজার ও ট্রায়াল ব্যালেন্স'],
        ['/AssetLifecycle/Index', 'স্থায়ী সম্পদ ও রক্ষণাবেক্ষণ'],
        ['/AuditLogs/Index', 'নিরীক্ষার লগ']
    ]) {
        assert.equal(navigationLabelFor(route, 'bn-BD'), label);
        assert.equal(navigationLabelFor(route, 'en-BD'), null);
    }
    assert.match(sidebarScript, /label\.textContent = translated/);
    assert.match(script, /link\.getAttribute\('aria-label'\)\?\.trim\(\)/);
    assert.doesNotMatch(script, /const translated = bangla/);
    assert.match(layout, /sidebar-navigation\.js" asp-append-version="true"[\s\S]*feature-discovery\.js" asp-append-version="true"/);
});

test('MVC Index aliases share one authorized search entry and saved shortcuts resolve only to current permissions', () => {
    assert.equal(destinationIdentity('/Students/Index'), destinationIdentity('/students/'));
    assert.notEqual(destinationIdentity('/Students/Edit'), destinationIdentity('/Students'));
    assert.notEqual(destinationIdentity('/Students#one'), destinationIdentity('/Students#two'));
    const entries = makeCatalog([
        { href: '/Students/Index', label: 'Students' },
        { href: '/Students', label: 'Duplicate alias' },
        { href: '/Students/Edit', label: 'Edit' }
    ]);
    assert.deepEqual(entries.map(x => x.href), ['/Students/Index', '/Students/Edit']);
    const saved = { version: 1, tenant: '22', session: 'A',
        favorites: ['/Students', '/Secret', '/Students/Index'], recent: ['/Students/'] };
    assert.deepEqual(scopedState(saved, '22', 'A', entries).favorites, ['/Students/Index']);
    assert.deepEqual(scopedState(saved, '22', 'A', entries).recent, ['/Students/Index']);
    assert.deepEqual(scopedState(saved, '23', 'A', entries).favorites, []);
    assert.deepEqual(scopedState(saved, '22', 'A', []).favorites, []);
});

test('sidebar favorites share the authorized session-scoped search catalogue, not a second route registry', () => {
    new Function(script);
    assert.match(layout, /id="sidebarFavoriteShortcuts"/);
    assert.match(script, /const sidebarFavorites = document\.getElementById\('sidebarFavoriteShortcuts'\)/);
    assert.match(script, /state\.favorites\.map\(href => byHref\.get\(href\)\)/);
    assert.match(script, /if \(!await verifiedContext\(\) \|\| !byHref\.has\(entry\.href\)\) return;/);
    assert.match(script, /sidebarFavorites\.replaceChildren\(\)/);
    assert.match(script, /link\.href = entry\.href/);
    assert.match(styles, /\.sidebar-favorites\[hidden\]/);
    assert.match(styles, /\.sidebar-favorite-link:focus-visible/);
    assert.doesNotMatch(script, /localStorage|innerHTML/);
});
