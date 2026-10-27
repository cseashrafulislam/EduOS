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
