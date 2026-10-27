const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('connectivity notice reacts to browser offline and online events without storing data', () => {
    const handlers = {};
    const notice = { hidden: true };
    const document = {
        querySelector: selector => selector === '[data-connectivity-notice]' ? notice : null,
        querySelectorAll: () => [],
        getElementById: () => null,
        body: { dataset: { workspacePreferences: 'disabled' } },
        documentElement: { dataset: {} },
        addEventListener: () => {}
    };
    const navigator = { onLine: true };
    const window = {
        fetch: () => Promise.resolve(),
        addEventListener: (name, handler) => { handlers[name] = handler; },
        matchMedia: () => ({ matches: false, addEventListener: () => {} }),
        location: { href: 'https://eduos.example.test/', origin: 'https://eduos.example.test' }
    };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/site.js'),
        { window, document, navigator, Request, URL, Headers, HTMLElement: class {} });
    assert.equal(notice.hidden, true);
    navigator.onLine = false;
    handlers.offline();
    assert.equal(notice.hidden, false);
    navigator.onLine = true;
    handlers.online();
    assert.equal(notice.hidden, true);
});

test('service worker excludes private, redirected, authenticated and non-static responses', () => {
    const source = read('EduOS.App/wwwroot/service-worker.js');
    assert.match(source, /response\.redirected/);
    assert.match(source, /request\.headers\.has\('Authorization'\)/);
    assert.match(source, /private\|no-store\|no-cache/);
    assert.match(source, /isPublicStaticResponse\(request, response\)/);
    assert.doesNotMatch(source, /caches\.open\(.*\)\.then\(.*cache\.put\(.*\)\s*\)/s);
});
