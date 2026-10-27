const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/platform/dashboard-panel-preferences.js'), 'utf8');

function harness(initial) {
    const children = ['capacity', 'quick-actions', 'setup-checklist'].map(id => ({
        dataset: { dashboardPanel: id }, hidden: false
    }));
    const mandatory = { hidden: false };
    const listeners = new Map();
    const container = {
        children,
        querySelectorAll: selector => selector === ':scope > [data-dashboard-panel]' ? [...children] : [],
        appendChild(element) {
            const index = children.indexOf(element);
            if (index >= 0) children.splice(index, 1);
            children.push(element);
        }
    };
    const window = {
        EduOSWorkspacePreferences: initial,
        addEventListener: (name, listener) => listeners.set(name, listener)
    };
    const document = {
        querySelector: selector => selector === '[data-dashboard-panels]' ? container : null
    };
    vm.runInNewContext(source, { window, document });
    return {
        order: () => children.map(element => element.dataset.dashboardPanel),
        hidden: id => children.find(element => element.dataset.dashboardPanel === id).hidden,
        mandatory,
        emit: panels => listeners.get('eduos:workspace-preferences')?.({ detail: { dashboardPanels: panels } })
    };
}

test('valid saved panels reorder DOM and toggle only optional sections', () => {
    const h = harness({ dashboardPanels: [
        { id: 'setup-checklist', visible: false },
        { id: 'capacity', visible: true },
        { id: 'quick-actions', visible: false }
    ] });
    assert.deepEqual(h.order(), ['setup-checklist', 'capacity', 'quick-actions']);
    assert.equal(h.hidden('setup-checklist'), true);
    assert.equal(h.hidden('capacity'), false);
    assert.equal(h.hidden('quick-actions'), true);
    assert.equal(h.mandatory.hidden, false);
    h.emit([
        { id: 'quick-actions', visible: true },
        { id: 'setup-checklist', visible: true },
        { id: 'capacity', visible: true }
    ]);
    assert.deepEqual(h.order(), ['quick-actions', 'setup-checklist', 'capacity']);
    assert.equal(h.hidden('setup-checklist'), false);
    assert.equal(h.mandatory.hidden, false);
});

test('missing, duplicated and unknown panel IDs cannot alter dashboard layout', () => {
    const h = harness();
    const original = h.order();
    h.emit([{ id: 'capacity', visible: false }]);
    h.emit([
        { id: 'capacity', visible: false },
        { id: 'capacity', visible: false },
        { id: 'quick-actions', visible: false }
    ]);
    h.emit([
        { id: 'capacity', visible: false },
        { id: 'quick-actions', visible: false },
        { id: 'unsafe', visible: false }
    ]);
    assert.deepEqual(h.order(), original);
    assert.equal(h.hidden('capacity'), false);
    assert.equal(h.hidden('quick-actions'), false);
    assert.equal(h.mandatory.hidden, false);
});
