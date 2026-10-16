const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

class Element {
    constructor(tag = 'div') { this.tag = tag; this.children = []; this.handlers = {}; this.textContent = ''; this.disabled = false; }
    append(...items) { this.children.push(...items); }
    replaceChildren(...items) { this.children = [...items]; }
    addEventListener(type, handler) { this.handlers[type] = handler; }
    querySelector() { return { value: 'verified-csrf-token' }; }
}
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

test('invitation history is rendered safely and revocation sends antiforgery plus RowVersion', async () => {
    const nodes = new Map();
    const get = id => nodes.get(id) || (nodes.set(id, new Element()), nodes.get(id));
    const rows = [{ reference: 'aaa-bbb', email: '<script>evil()</script>', roleId: 5,
        rowVersion: 'AQIDBAUGBwg=', expiresAt: '2099-01-01T00:00:00Z', acceptedAt: null, revokedAt: null }];
    const calls = [];
    const fetch = async (url, options) => {
        calls.push({ url, options });
        let data;
        if (url.endsWith('/roles')) data = [{ id: 5, name: 'Teacher' }];
        else if (url.includes('/revoke')) { rows[0].revokedAt = new Date().toISOString(); data = true; }
        else data = { totalCount: 1, items: rows };
        return { ok: true, status: 200, json: async () => ({ success: true, data }) };
    };
    const context = { document: { getElementById: get, createElement: tag => new Element(tag) },
        fetch, confirm: () => true, Date, encodeURIComponent, console };
    const source = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/tenant-users/invitations.js'), 'utf8');
    vm.runInNewContext(source, context);
    await sleep(10);
    const tr = get('invitationRows').children[0];
    assert.equal(tr.children[0].textContent, '<script>evil()</script>');
    assert.equal(tr.children[3].textContent, 'Pending');
    await tr.children[4].children[0].handlers.click();
    const post = calls.find(x => x.options.method === 'POST');
    assert.ok(post);
    assert.equal(post.options.headers.RequestVerificationToken, 'verified-csrf-token');
    assert.equal(JSON.parse(post.options.body).rowVersion, 'AQIDBAUGBwg=');
    assert.equal(get('invitationRows').children[0].children[3].textContent, 'Revoked');
});

test('SQL UTC expiry without timezone is not misinterpreted as browser-local time', async () => {
    const nodes = new Map();
    const get = id => nodes.get(id) || (nodes.set(id, new Element()), nodes.get(id));
    class DhakaDate extends Date {
        static now() { return Date.parse('2026-10-16T10:00:00Z'); }
        static parse(value) { return /Z$/i.test(value) ? Date.parse(value) : Date.parse(value + '+06:00'); }
    }
    const fetch = async url => ({ ok: true, status: 200, json: async () => ({ success: true,
        data: url.endsWith('/roles') ? [] : { totalCount: 1, items: [{
            reference: 'abc', email: 'student@example.edu', roleId: null,
            rowVersion: 'AQIDBAUGBwg=', expiresAt: '2026-10-16T12:00:00',
            acceptedAt: null, revokedAt: null
        }] } }) });
    const context = { document: { getElementById: get, createElement: tag => new Element(tag) },
        fetch, confirm: () => true, Date: DhakaDate, encodeURIComponent, console };
    const source = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/tenant-users/invitations.js'), 'utf8');
    vm.runInNewContext(source, context);
    await sleep(10);
    assert.equal(get('invitationRows').children[0].children[3].textContent, 'Pending');
});
