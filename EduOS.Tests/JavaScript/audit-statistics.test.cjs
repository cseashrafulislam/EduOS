const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/AuditLogs/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/audit-logs/statistics.js');

test('audit statistics are reachable on the existing authorized audit page', () => {
    for (const id of ['auditInsights', 'auditStatsStatus', 'auditTopEntities', 'auditTopUsers',
        'auditStatTotal', 'auditStatToday', 'auditStatWeek', 'auditStatMonth',
        'auditStatSuccess', 'auditStatFailed', 'auditStatCreated', 'auditStatUpdated', 'auditStatDeleted'])
        assert.ok(view.includes('id="' + id + '"'), 'Missing ' + id);
    assert.match(view, /audit-logs\/statistics\.js/);
    assert.match(js, /\/api\/v1\/AuditLog\/statistics\?/);
    assert.doesNotMatch(js, /innerHTML|outerHTML|insertAdjacentHTML/);
});

test('statistics respect filters, render safely and reject invalid user IDs', async () => {
    const nodes = new Map();
    function node(id) {
        if (!nodes.has(id)) nodes.set(id, {
            id, value: '', textContent: '', children: [], listeners: {},
            replaceChildren(...items) { this.children = items; },
            append(...items) { this.children.push(...items); },
            addEventListener(name, fn) { this.listeners[name] = fn; }
        });
        return nodes.get(id);
    }
    for (const id of ['auditInsights', 'auditFilterForm', 'auditStatsStatus', 'auditTopEntities',
        'auditTopUsers', 'auditFrom', 'auditTo', 'auditEntity', 'auditAction', 'auditUserId',
        'auditStatus', 'auditClear', 'auditStatTotal', 'auditStatToday', 'auditStatWeek',
        'auditStatMonth', 'auditStatSuccess', 'auditStatFailed', 'auditStatCreated',
        'auditStatUpdated', 'auditStatDeleted']) node(id);
    node('auditUserId').value = '123';
    node('auditFrom').value = '2026-10-01';
    const calls = [];
    const document = {
        getElementById: node,
        createElement: tag => ({
            tag, textContent: '', children: [],
            append(...items) { this.children.push(...items); }
        })
    };
    const fetch = async (url, options) => {
        calls.push({ url, options });
        return { ok: true, json: async () => ({
            success: true, data: {
                totalLogs: 3, todayLogs: 1, successfulOperations: 2, failedOperations: 1,
                topEntities: [{ entityName: '<b>Untrusted</b>', activityCount: 3 }],
                topUsers: [{ userName: 'Example', userId: 123, activityCount: 3 }]
            }
        }) };
    };
    vm.runInNewContext(js, { document, fetch, AbortController, URLSearchParams, queueMicrotask });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(calls.length, 1);
    const url = new URL(calls[0].url, 'http://localhost');
    assert.equal(url.searchParams.get('userId'), '123');
    assert.equal(url.searchParams.get('fromUtc'), '2026-10-01T00:00:00Z');
    assert.equal(calls[0].options.credentials, 'same-origin');
    assert.equal(calls[0].options.cache, 'no-store');
    assert.equal(node('auditStatTotal').textContent, '3');
    assert.equal(node('auditTopEntities').children[0].children[0].textContent, '<b>Untrusted</b>');
    node('auditUserId').value = 'abc';
    node('auditFilterForm').listeners.submit({ preventDefault() {} });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(calls.length, 1, 'Invalid user ID must not issue a request');
    assert.match(node('auditStatsStatus').textContent, /valid positive User ID/);
});
