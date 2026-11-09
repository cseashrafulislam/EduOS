const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const { approvedManualUrl, mount } = require('../../EduOS.App/wwwroot/js/platform/page-help-loader.js');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');

function browser() {
    const handlers = [];
    const scripts = [];
    const attrs = {};
    const doc = {
        body: { dataset: { authTenantId: '55', authSessionReference: 's1' } },
        documentElement: { lang: 'bn-BD' },
        head: { append(script) { scripts.push(script); } },
        createElement(name) {
            assert.equal(name, 'script');
            return { remove() { this.removed = true; } };
        }
    };
    const trigger = {
        dataset: { manualSrc: '/js/platform/page-context-help.js?v=sha' },
        addEventListener(type, cb, capture) { handlers.push({ type, cb, capture }); },
        removeEventListener(type, cb, capture) {
            const index = handlers.findIndex(h => h.type === type && h.cb === cb && h.capture === capture);
            if (index >= 0) handlers.splice(index, 1);
        },
        setAttribute(key, value) { attrs[key] = value; },
        removeAttribute(key) { delete attrs[key]; },
        click() { this.replayed = (this.replayed || 0) + 1; }
    };
    const status = { textContent: '' };
    doc.getElementById = id => ({ pageContextHelp: trigger, pageHelpLoadStatus: status })[id];
    const host = { location: { href: 'https://school.example/Dashboard/Index' } };
    const fire = () => handlers.filter(h => h.type === 'click').forEach(h => h.cb({
        preventDefault() {}, stopImmediatePropagation() {}
    }));
    mount(doc, host);
    return { doc, trigger, status, scripts, attrs, handlers, fire };
}

test('on-demand manual keeps 592 KB static catalogue off the initial page-load path', () => {
    assert.match(layout, /page-help-loader\.js" asp-append-version="true"/);
    assert.doesNotMatch(layout, /<script src="~\/js\/platform\/page-context-help\.js"/);
    const size = fs.statSync(path.join(root, 'EduOS.App/wwwroot/js/platform/page-context-help.js')).size;
    assert.ok(size > 500000, 'must document actual uncompressed payload');
    const b = browser();
    assert.equal(b.scripts.length, 0);
    b.fire();
    assert.equal(b.scripts.length, 1);
    assert.equal(b.scripts[0].src, 'https://school.example/js/platform/page-context-help.js?v=sha');
    assert.equal(b.attrs['aria-busy'], 'true');
    b.fire();
    assert.equal(b.scripts.length, 1, 'concurrent clicks must not duplicate loading');
    b.scripts[0].onload();
    assert.equal(b.trigger.replayed, 1, 'existing reviewed Help Drawer receives user request');
    assert.equal(b.handlers.length, 0, 'loader relinquishes click listener after loading');
    assert.equal(b.attrs['aria-busy'], undefined);
});

test('missing catalogue retries without trapping the Help button', () => {
    const b = browser();
    b.fire();
    b.scripts[0].onerror();
    assert.equal(b.scripts[0].removed, true);
    assert.match(b.status.textContent, /আবার Info চাপুন/);
    assert.equal(b.attrs['aria-busy'], undefined);
    b.fire();
    assert.equal(b.scripts.length, 2);
    b.scripts[1].onload();
    assert.equal(b.trigger.replayed, 1);
});

test('tenant/session switch while loading does not reopen old help', () => {
    const b = browser();
    b.fire();
    b.doc.body.dataset.authTenantId = 'other';
    b.scripts[0].onload();
    assert.equal(b.trigger.replayed, undefined);
    assert.match(b.status.textContent, /সেশন বদলেছে/);
    b.fire();
    assert.equal(b.scripts.length, 1);
});

test('manual URL is same-origin and only the versioned reviewed script is allowed', () => {
    const base = 'https://school.example/Dashboard/Index';
    assert.equal(approvedManualUrl('/js/platform/page-context-help.js?v=abc', base),
        'https://school.example/js/platform/page-context-help.js?v=abc');
    for (const src of ['https://evil.example/js/platform/page-context-help.js',
        '//evil.example/js/platform/page-context-help.js',
        '/js/platform/page-context-help.js?tenant=2',
        '/js/platform/page-context-help.js#fragment',
        '/js/platform/../private.js']) assert.equal(approvedManualUrl(src, base), null, src);
});
