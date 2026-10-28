const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/platform/table-column-preferences.js'), 'utf8');

function harness(id, ids, initial) {
    const observers = [];
    const listeners = new Map();
    function row(cells) {
        return {
            cells,
            appendChild(cell) {
                const index = this.cells.indexOf(cell);
                if (index >= 0) this.cells.splice(index, 1);
                this.cells.push(cell);
            }
        };
    }
    function cells() { return ids.map(column => ({ dataset: {}, hidden: false, name: column })); }
    const header = row(ids.map(column => ({ dataset: { tableColumn: column }, hidden: false, name: column })));
    const body = { rows: [row(cells())] };
    const table = { dataset: { workspaceTable: id }, tHead: { rows: [header] }, tBodies: [body] };
    const window = {
        EduOSWorkspacePreferences: initial,
        addEventListener: (name, listener) => listeners.set(name, listener)
    };
    const document = {
        querySelectorAll: selector => selector === 'table[data-workspace-table]' ? [table] : []
    };
    class MutationObserver {
        constructor(callback) { this.callback = callback; observers.push(this); }
        observe(target, options) { assert.equal(target, body); assert.equal(options.childList, true); }
    }
    vm.runInNewContext(source, { window, document, MutationObserver });
    return {
        columns: () => body.rows[0].cells.map(cell => cell.name),
        header: () => header.cells.map(cell => cell.name),
        hidden: name => body.rows[0].cells.find(cell => cell.name === name).hidden,
        appendRow() {
            const added = row(cells());
            body.rows.push(added);
            observers.forEach(observer => observer.callback());
            return added;
        },
        emit(tablePreferences) {
            listeners.get('eduos:workspace-preferences')?.({ detail: { tablePreferences } });
        },
        body
    };
}

test('user table reorders only real columns, preserves actions and updates paged rows', () => {
    const h = harness('institution-users', ['user', 'membership', 'roles', 'action'], {
        tablePreferences: [
            { id: 'institution-users', columns: [
                { id: 'roles', visible: false }, { id: 'action', visible: true },
                { id: 'user', visible: true }, { id: 'membership', visible: false }
            ] }
        ]
    });
    assert.deepEqual(h.columns(), ['roles', 'action', 'user', 'membership']);
    assert.deepEqual(h.header(), ['roles', 'action', 'user', 'membership']);
    assert.equal(h.hidden('roles'), true);
    assert.equal(h.hidden('user'), false);
    assert.equal(h.hidden('action'), false);
    const added = h.appendRow();
    assert.deepEqual(added.cells.map(cell => cell.name), ['roles', 'action', 'user', 'membership']);
    h.emit([{ id: 'institution-users', columns: [
        { id: 'user', visible: true }, { id: 'membership', visible: true },
        { id: 'roles', visible: true }, { id: 'action', visible: true }
    ] }]);
    assert.deepEqual(h.columns(), ['user', 'membership', 'roles', 'action']);
    assert.equal(h.hidden('roles'), false);
});

test('unknown, duplicate and hidden mandatory columns fail closed to default layout', () => {
    const ids = ['email', 'role', 'campus', 'expires', 'status', 'action'];
    const h = harness('institution-invitations', ids, {});
    const all = ids.map(id => ({ id, visible: true }));
    for (const invalid of [
        [{ id: 'email', visible: true }],
        [...all.slice(0, 5), { id: 'status', visible: true }],
        all.map(column => column.id === 'action' ? { ...column, visible: false } : column),
        all.map(column => column.id === 'role' ? { id: 'script', visible: false } : column)
    ]) {
        h.emit([{ id: 'institution-invitations', columns: invalid }]);
        assert.deepEqual(h.columns(), ids);
        assert.equal(h.hidden('action'), false);
        assert.equal(h.hidden('email'), false);
        assert.equal(h.hidden('status'), false);
    }
    h.body.rows.push({ cells: [{ colSpan: 6, dataset: {}, hidden: false }] });
    h.appendRow();
    assert.equal(h.body.rows[1].cells[0].colSpan, 6);
});

test('feature audit columns apply to dynamically rendered pages and preserve restrictions', () => {
    const ids = ['feature', 'code', 'plan', 'override', 'restriction', 'reason'];
    const h = harness('institution-feature-audit', ids, {
        tablePreferences: [{ id: 'institution-feature-audit', columns: [
            { id: 'reason', visible: false }, { id: 'restriction', visible: true },
            { id: 'feature', visible: true }, { id: 'code', visible: true },
            { id: 'override', visible: false }, { id: 'plan', visible: false }
        ] }]
    });
    assert.deepEqual(h.header(), ['reason', 'restriction', 'feature', 'code', 'override', 'plan']);
    assert.equal(h.hidden('reason'), true);
    assert.equal(h.hidden('restriction'), false);
    const added = h.appendRow();
    assert.deepEqual(added.cells.map(cell => cell.name), h.header());
});

test('activity audit never hides actor, action, outcome or timestamp', () => {
    const ids = ['time', 'user', 'action', 'entity', 'record', 'ip', 'outcome'];
    const h = harness('institution-activity-audit', ids, {});
    const layout = ids.map(id => ({ id, visible: id !== 'ip' }));
    h.emit([{ id: 'institution-activity-audit', columns: layout }]);
    assert.equal(h.hidden('ip'), true);
    h.emit([{ id: 'institution-activity-audit', columns: layout.map(x =>
        x.id === 'user' ? { ...x, visible: false } : x) }]);
    assert.deepEqual(h.columns(), ids);
    assert.equal(h.hidden('user'), false);
    assert.equal(h.hidden('outcome'), false);
});

test('campus directory preserves campus identity and management actions across reloads', () => {
    const ids = ['campus', 'code', 'contact', 'type', 'action'];
    const h = harness('institution-campuses', ids, {});
    h.emit([{ id: 'institution-campuses', columns: [
        { id: 'contact', visible: false }, { id: 'action', visible: true },
        { id: 'campus', visible: true }, { id: 'type', visible: false },
        { id: 'code', visible: true }
    ] }]);
    assert.deepEqual(h.header(), ['contact', 'action', 'campus', 'type', 'code']);
    assert.equal(h.hidden('action'), false);
    assert.equal(h.hidden('contact'), true);
    assert.deepEqual(h.appendRow().cells.map(cell => cell.name), h.header());
    h.emit([{ id: 'institution-campuses', columns: ids.map(id => ({
        id, visible: id !== 'action'
    })) }]);
    assert.deepEqual(h.header(), ids);
    assert.equal(h.hidden('action'), false);
});

test('institution history never hides timestamp, action or actor', () => {
    const ids = ['when', 'action', 'fields', 'actor'];
    const h = harness('institution-change-history', ids, {});
    h.emit([{ id: 'institution-change-history', columns: [
        { id: 'actor', visible: true }, { id: 'fields', visible: false },
        { id: 'when', visible: true }, { id: 'action', visible: true }
    ] }]);
    assert.deepEqual(h.header(), ['actor', 'fields', 'when', 'action']);
    assert.equal(h.hidden('fields'), true);
    assert.deepEqual(h.appendRow().cells.map(cell => cell.name), h.header());
    h.emit([{ id: 'institution-change-history', columns: ids.map(id => ({
        id, visible: id !== 'actor'
    })) }]);
    assert.deepEqual(h.header(), ids);
    assert.equal(h.hidden('actor'), false);
});

test('institution settings table keeps key, protection and edit actions visible', () => {
    const ids = ['key', 'category', 'value', 'protection', 'action'];
    const h = harness('institution-settings', ids, {});
    h.emit([{ id: 'institution-settings', columns: [
        { id: 'value', visible: false }, { id: 'action', visible: true },
        { id: 'key', visible: true }, { id: 'category', visible: false },
        { id: 'protection', visible: true }
    ] }]);
    assert.deepEqual(h.header(), ['value', 'action', 'key', 'category', 'protection']);
    assert.equal(h.hidden('value'), true);
    assert.equal(h.hidden('action'), false);
    assert.deepEqual(h.appendRow().cells.map(cell => cell.name), h.header());
    h.emit([{ id: 'institution-settings', columns: ids.map(id => ({
        id, visible: id !== 'protection'
    })) }]);
    assert.deepEqual(h.header(), ids);
    assert.equal(h.hidden('protection'), false);
});

test('permission exceptions keep permission, mode, expiry and edit actions visible', () => {
    const ids = ['permission', 'mode', 'reason', 'expires', 'action'];
    const h = harness('institution-permission-exceptions', ids, {});
    h.emit([{ id: 'institution-permission-exceptions', columns: [
        { id: 'reason', visible: false }, { id: 'expires', visible: true },
        { id: 'permission', visible: true }, { id: 'action', visible: true },
        { id: 'mode', visible: true }
    ] }]);
    assert.deepEqual(h.header(), ['reason', 'expires', 'permission', 'action', 'mode']);
    assert.equal(h.hidden('reason'), true);
    assert.equal(h.hidden('expires'), false);
    assert.deepEqual(h.appendRow().cells.map(cell => cell.name), h.header());
    for (const mandatory of ['permission', 'mode', 'expires', 'action']) {
        h.emit([{ id: 'institution-permission-exceptions', columns: ids.map(id => ({
            id, visible: id !== mandatory
        })) }]);
        assert.deepEqual(h.header(), ids);
        assert.equal(h.hidden(mandatory), false);
    }
});
