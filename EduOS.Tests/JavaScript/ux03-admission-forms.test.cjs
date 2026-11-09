'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

test('Admission create, intake, review and enrollment use shared draft/busy protection', () => {
    const view = read('EduOS.App/Views/Admissions/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/admissions/index.js');
    assert.match(view, /form-guard\.js"[\s\S]*admissions\/index\.js"/);
    for (const id of ['admissionForm', 'intakeFormEditor', 'reviewForm', 'enrollmentForm'])
        assert.ok(script.includes(`'${id}'`), `form ${id} must participate in the guard`);
    for (const modal of ['intakeFormModal', 'reviewModal', 'enrollmentModal'])
        assert.ok(script.includes(`'${modal}'`), `modal ${modal} must block unsafe close`);
    assert.match(script, /EduOSFormGuard\.protectModal/);
    assert.match(script, /guards\.intake\?\.markClean\(\)/);
    assert.match(script, /guards\.review\?\.markClean\(\)/);
    assert.match(script, /guards\.enrollment\?\.markClean\(\)/);
    assert.match(script, /if \(guards\.application\?\.isDirty\(\) && !guards\.application\.confirmDiscard\(\)\) return/);
    assert.match(script, /if \(guards\.review\?\.isDirty\(\) && !guards\.review\.confirmDiscard\(\)\) return/);
    assert.match(script, /editor\.inert = true/);
    assert.match(script, /form\.inert = true/);
    assert.match(script, /document\.getElementById\('reviewModal'\)\.inert = true/);
    assert.match(script, /document\.getElementById\('enrollmentModal'\)\.inert = true/);
    assert.match(script, /state\.pendingFingerprint !== fingerprint/);
    assert.match(script, /state\.intakeFingerprint !== fingerprint/);
});

test('modal can close normally when clean, but not while saving or after refused discard', () => {
    const callbacks = new Map();
    const modal = {
        addEventListener(name, callback) { callbacks.set(name, callback); },
        removeEventListener(name, callback) {
            if (callbacks.get(name) === callback) callbacks.delete(name);
        }
    };
    const item = { tagName: 'INPUT', type: 'text', id: 'note', value: '' };
    let saving = false;
    let accept = false;
    let confirms = 0;
    const window = {
        document: { documentElement: { lang: 'bn-BD' } },
        addEventListener() {},
        confirm(message) { confirms++; assert.match(message, /সংরক্ষণ/); return accept; }
    };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/platform/form-guard.js'), { window });
    const guard = window.EduOSFormGuard.create({ elements: [item] });
    const dispose = window.EduOSFormGuard.protectModal(modal, guard, () => saving);
    function attemptClose() {
        let prevented = false;
        callbacks.get('hide.bs.modal')({ preventDefault() { prevented = true; } });
        return prevented;
    }
    assert.equal(attemptClose(), false, 'pristine editor closes without confirmation');
    item.value = 'Important unsaved review note';
    saving = true;
    assert.equal(attemptClose(), true, 'cannot close while pending POST');
    assert.equal(confirms, 0, 'in-flight write must not offer discard');
    saving = false;
    assert.equal(attemptClose(), true, 'Cancel must keep entered note if user declines');
    assert.equal(guard.isDirty(), true);
    accept = true;
    assert.equal(attemptClose(), false, 'explicit discard permits close');
    assert.equal(guard.isDirty(), false, 'accepted dismissal does not warn beforeunload');
    dispose();
    assert.equal(callbacks.has('hide.bs.modal'), false, 'event can be unregistered');
});

test('modal guards reject bad setup instead of silently dropping close protection', () => {
    const window = { document: { documentElement: { lang: 'en-BD' } }, addEventListener() {} };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/platform/form-guard.js'), { window });
    assert.throws(() => window.EduOSFormGuard.protectModal(null, {}), /modal and form guard/i);
    assert.throws(() => window.EduOSFormGuard.protectModal({}, {}), /modal and form guard/i);
});
