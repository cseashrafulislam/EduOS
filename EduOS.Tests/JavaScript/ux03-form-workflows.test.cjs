'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const guardScript = read('EduOS.App/wwwroot/js/platform/form-guard.js');

function setup(lang = 'en-BD') {
    const listeners = new Map();
    const window = {
        document: { documentElement: { lang } },
        confirm: () => false,
        addEventListener: (type, callback) => {
            if (!listeners.has(type)) listeners.set(type, []);
            listeners.get(type).push(callback);
        }
    };
    vm.runInNewContext(guardScript, { window }, { filename: 'form-guard.js' });
    return { window, listeners, factory: window.EduOSFormGuard };
}
function field(id, value, type = 'text') {
    return { id, tagName: 'INPUT', type, value, checked: false };
}
function form(...elements) { return { elements }; }
test('dirty snapshots are stable, avoid false prompts, and markClean only after save', () => {
    const { factory, listeners } = setup();
    const title = field('bookTitle', 'Original');
    const guard = factory.create(form(title));
    assert.equal(guard.isDirty(), false);
    title.value = 'Updated';
    assert.equal(guard.isDirty(), true);
    guard.markClean();
    assert.equal(guard.isDirty(), false);
    assert.equal(listeners.get('beforeunload').length, 1);
});
test('cancelled confirmation never overwrites unsaved edits', () => {
    const { factory, window } = setup('bn-BD');
    const code = field('code', 'A');
    const guard = factory.create(form(code));
    code.value = 'B';
    let prompt;
    window.confirm = value => { prompt = value; return false; };
    assert.equal(guard.confirmDiscard(), false);
    assert.match(prompt, /সংরক্ষণ/);
    assert.equal(code.value, 'B');
    window.confirm = () => true;
    assert.equal(guard.confirmDiscard(), true);
    assert.equal(guard.isDirty(), true, 'confirmation does not silently reset the form');
});
test('checkbox and multiselect values are tracked without adding extra editable facts', () => {
    const { factory } = setup();
    const active = field('active', '', 'checkbox');
    const select = { id: 'campuses', tagName: 'SELECT', type: 'select-multiple', multiple: true,
        selectedOptions: [{ value: '1' }] };
    const guard = factory.create(form(active, select));
    assert.equal(guard.isDirty(), false);
    active.checked = true;
    assert.equal(guard.isDirty(), true);
    guard.markClean();
    select.selectedOptions = [{ value: '1' }, { value: '2' }];
    assert.equal(guard.isDirty(), true);
});
test('one beforeunload handler blocks dirty drafts and respects clean/disposed forms', () => {
    const { factory, listeners } = setup();
    const a = field('a', '1'), b = field('b', '2');
    const first = factory.create(form(a)), second = factory.create(form(b));
    assert.equal(listeners.get('beforeunload').length, 1);
    const event = { preventDefault() { this.prevented = true; }, returnValue: undefined };
    listeners.get('beforeunload')[0](event);
    assert.equal(event.prevented, undefined);
    b.value = '3';
    listeners.get('beforeunload')[0](event);
    assert.equal(event.prevented, true);
    assert.equal(event.returnValue, '');
    second.dispose();
    const cleanEvent = { preventDefault() { this.prevented = true; } };
    listeners.get('beforeunload')[0](cleanEvent);
    assert.equal(cleanEvent.prevented, undefined);
    a.value = '4';
    assert.equal(first.isDirty(), true);
});
test('Library guards three forms against overwrite, duplicates and in-flight edits', () => {
    const script = read('EduOS.App/wwwroot/js/library/index.js');
    const view = read('EduOS.App/Views/Library/Index.cshtml');
    assert.match(view, /form-guard\.js"[\s\S]*library\/index\.js"/);
    for (const guard of ['bookGuard', 'copyGuard', 'categoryGuard'])
        assert.match(script, new RegExp(guard + '\\.confirmDiscard\\(\\)'));
    for (const busy of ['bookSaving', 'copySaving', 'categorySaving'])
        assert.match(script, new RegExp('if \\(' + busy + ' \\|\\|'));
    for (const formId of ['libraryBookForm', 'libraryCopyForm', 'libraryCategoryForm'])
        assert.ok(script.includes("el('" + formId + "').inert = true"));
    assert.match(script, /payload\.replacementPrice < 0/);
});
test('Hostel validates fresh student and bed options and prevents duplicate allocate/move', () => {
    const script = read('EduOS.App/wwwroot/js/hostel-operations/index.js');
    const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    assert.match(view, /form-guard\.js"[\s\S]*hostel-operations\/index\.js"/);
    assert.match(script, /allocationGuard\.markClean\(\)/);
    assert.match(script, /if \(allocationBusy \|\| !studentsReady \|\| !bedsReady/);
    assert.match(script, /if \(bedMoveBusy \|\| !pendingBedMove\) return/);
    assert.match(script, /if \(lookup !== studentLookup\) return/);
    assert.match(script, /if \(lookup !== bedLookup\) return/);
    assert.match(script, /form\.inert = true/);
});
test('Transport rejects stale eligibility responses and preserves scoped route picks on reload', () => {
    const script = read('EduOS.App/wwwroot/js/transport-operations/index.js');
    const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
    assert.match(view, /form-guard\.js"[\s\S]*transport-operations\/index\.js"/);
    assert.match(script, /if \(request !== studentRequest\) return/);
    assert.match(script, /if \(request !== staticRequest\) return/);
    assert.match(script, /if \(request !== activeRequest\) return/);
    assert.match(script, /previousRoute = el\('transportRoute'\)\.value/);
    assert.match(script, /if \(assignmentBusy \|\| !studentsReady \|\| !routesReady/);
    assert.match(script, /if \(extensionBusy \|\| !pendingRiderExtension\) return/);
    assert.match(script, /if \(correctionBusy \|\| !pendingStopCorrection\) return/);
    assert.match(script, /assignmentGuard\.markClean\(\)/);
});
