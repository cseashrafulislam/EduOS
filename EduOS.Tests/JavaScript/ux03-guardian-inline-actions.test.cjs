'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const moduleFunction = (file, name, context) => vm.runInNewContext(
    read('EduOS.App/wwwroot/js/students/' + file).replace('export function', 'function') + '\n' + name,
    context);
const studentReference = '00000000-0000-4000-8000-000000000001';
const guardianReference = '00000000-0000-4000-8000-000000000002';

function deferred() {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { resolve, reject, promise };
}
function fixture(file, name) {
    const messages = [], writes = [], renders = [], elements = new Map();
    let allowDiscard = true, session = true, throwRender = false;
    class Element {
        constructor(tag) {
            this.tagName = tag.toUpperCase(); this.children = []; this.listeners = {};
            this.className = ''; this.value = ''; this.checked = false; this.disabled = false;
            this.inert = false; this.dirty = false; this.textContent = '';
            this.classList = {
                contains: value => this.className.split(/\s+/).includes(value),
                add: value => { if (!this.classList.contains(value)) this.className += ' ' + value; },
                remove: value => { this.className = this.className.split(/\s+/).filter(x => x !== value).join(' '); }
            };
        }
        addEventListener(type, callback) { this.listeners[type] = callback; }
        append(...children) { this.children.push(...children); }
        replaceChildren(...children) { this.children = children; }
        querySelectorAll(tag) {
            const found = [];
            const visit = element => {
                for (const child of element.children) {
                    if (child.tagName === tag.toUpperCase()) found.push(child);
                    visit(child);
                }
            };
            visit(this); return found;
        }
        reportValidity() { return true; }
        focus() {}
    }
    const $ = id => { if (!elements.has(id)) elements.set(id, new Element('div')); return elements.get(id); };
    const context = { document: { createElement: tag => new Element(tag),
        getElementById: $ }, navigator: { clipboard: { writeText: async () => {} } },
        CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
        Date };
    context.document.dispatchEvent = () => {};
    const state = { student: { studentReference, guardians: [{
        linkId: 10, guardianReference, fullName: 'Parent One', relationCode: 'Father',
        isPrimary: false, canReceiveSms: false, canReceiveEmail: false,
        isAuthorizedPickup: false, isFinancialContact: false, isEmergencyContact: false,
        rowVersion: 'v1'
    }], revokedGuardians: [{ linkId: 12, fullName: 'Parent Two',
        relationCode: 'Mother', rowVersion: 'v2' }] },
        busy: false, selecting: false, requiresRefresh: false,
        sameSession: () => session, inline: { active: [], revoked: [] },
        hasPendingInlineDraft: () => false,
        refreshActions() {},
        registerInline(scope, form, close) {
            const guard = { isDirty: () => form.dirty, confirmDiscard: () => allowDiscard,
                markClean: () => { form.dirty = false; }, dispose() {} };
            const entry = { form, guard, close };
            state.inline[scope].push(entry); return entry;
        },
        discardInline(scope, except) {
            const entries = state.inline[scope].filter(entry => entry !== except &&
                !entry.form.classList.contains('d-none'));
            if (entries.some(entry => entry.guard.isDirty()) && !allowDiscard) return false;
            for (const entry of entries) entry.close();
            return true;
        }
    };
    const api = (url, method, payload) => {
        const pending = deferred(); writes.push({ url, method, payload, ...pending });
        return pending.promise;
    };
    const render = student => { renders.push({ student, busy: state.busy }); if (throwRender) throw Error('Broken list'); };
    const notice = (message, error) => messages.push({ message, error });
    moduleFunction(file, name, context)({ $, api, state, render, notice });
    const box = $(file === 'guardian-reactivation.js' ? 'guardianRevokedLinks' : 'guardianLinks');
    const buttons = box.querySelectorAll('button');
    const forms = box.querySelectorAll('form');
    return { state, elements, messages, writes, renders, box, buttons, forms,
        set allowDiscard(value) { allowDiscard = value; },
        set session(value) { session = value; },
        set throwRender(value) { throwRender = value; },
        flush: async () => { for (let i = 0; i < 25; i++) await Promise.resolve(); },
        submit: index => forms[index].listeners.submit({ preventDefault() {} }) };
}

test('inline relationship editor cancel confirms discard, resets values and does not keep a dirty navigation guard', () => {
    const h = fixture('guardian-link-edit.js', 'renderGuardianLinks');
    const [editForm] = h.forms;
    const edit = h.buttons.find(button => button.textContent === 'Edit relationship');
    const cancel = h.buttons.find(button => button.textContent === 'Cancel');
    edit.listeners.click();
    assert.equal(editForm.classList.contains('d-none'), false);
    const relation = editForm.querySelectorAll('select')[0];
    relation.value = 'Other'; editForm.dirty = true; h.allowDiscard = false;
    cancel.listeners.click();
    assert.equal(editForm.classList.contains('d-none'), false);
    h.allowDiscard = true; cancel.listeners.click();
    assert.equal(editForm.classList.contains('d-none'), true);
    assert.equal(relation.value, 'Father');
    assert.equal(editForm.dirty, false);
});

test('opening unlink does not discard a dirty relationship editor without confirmation', () => {
    const h = fixture('guardian-link-edit.js', 'renderGuardianLinks');
    const edit = h.buttons.find(button => button.textContent === 'Edit relationship');
    const unlink = h.buttons.find(button => button.textContent === 'Unlink guardian');
    edit.listeners.click(); h.forms[0].dirty = true; h.allowDiscard = false;
    unlink.listeners.click();
    assert.equal(h.forms[0].classList.contains('d-none'), false);
    assert.equal(h.forms[1].classList.contains('d-none'), true);
    h.allowDiscard = true; unlink.listeners.click();
    assert.equal(h.forms[0].classList.contains('d-none'), true);
    assert.equal(h.forms[0].dirty, false);
    assert.equal(h.forms[1].classList.contains('d-none'), false);
});

test('inline relationship PUT rejects concurrent submit and reports an acknowledged write if rendering fails', async () => {
    const h = fixture('guardian-link-edit.js', 'renderGuardianLinks');
    h.buttons.find(button => button.textContent === 'Edit relationship').listeners.click();
    h.forms[0].dirty = true;
    h.submit(0); h.submit(0); await h.flush();
    assert.equal(h.writes.length, 1); assert.equal(h.forms[0].inert, true);
    assert.equal(h.writes[0].payload.rowVersion, 'v1');
    h.throwRender = true;
    h.writes[0].resolve({ studentReference }); await h.flush();
    assert.equal(h.state.requiresRefresh, true);
    assert.equal(h.renders[0].busy, false);
    assert.match(h.messages.at(-1).message, /saved, but/i);
    assert.equal(h.forms[0].dirty, false);
});

test('failed unlink preserves reason, blocks duplicate POST and recommends reconciliation', async () => {
    const h = fixture('guardian-link-edit.js', 'renderGuardianLinks');
    h.buttons.find(button => button.textContent === 'Unlink guardian').listeners.click();
    const reason = h.forms[1].querySelectorAll('textarea')[0];
    reason.value = 'Wrong emergency contact'; h.forms[1].dirty = true;
    h.submit(1); h.submit(1); await h.flush();
    assert.equal(h.writes.length, 1);
    assert.equal(h.writes[0].method, 'POST');
    h.writes[0].reject(Error('Timeout')); await h.flush();
    assert.equal(reason.value, 'Wrong emergency contact');
    assert.equal(h.forms[1].dirty, true);
    assert.match(h.messages.at(-1).message, /Verify the link before retrying/i);
});

test('inline relationship write refuses to erase an unsaved action in another panel', async () => {
    const h = fixture('guardian-link-edit.js', 'renderGuardianLinks');
    h.buttons.find(button => button.textContent === 'Edit relationship').listeners.click();
    h.state.hasPendingInlineDraft = () => true;
    await h.submit(0);
    assert.equal(h.writes.length, 0);
    assert.match(h.messages.at(-1).message, /other unsaved guardian action/i);
});

test('revoked guardian editor starts with no inherited privileges and confirms explicit reactivation', async () => {
    const h = fixture('guardian-reactivation.js', 'renderRevokedGuardianLinks');
    const button = h.buttons.find(item => item.textContent === 'Review reactivation');
    button.listeners.click();
    for (const field of h.forms[0].querySelectorAll('input')) assert.equal(field.checked, false);
    h.forms[0].querySelectorAll('textarea')[0].value = 'Parent is eligible again';
    h.forms[0].dirty = true;
    h.submit(0); h.submit(0); await h.flush();
    assert.equal(h.writes.length, 1);
    assert.equal(h.writes[0].payload.relationship.canReceiveSms, false);
    assert.equal(h.writes[0].payload.relationship.rowVersion, 'v2');
    h.writes[0].resolve({ studentReference }); await h.flush();
    assert.equal(h.state.requiresRefresh, false);
    assert.equal(h.renders[0].busy, false);
    assert.match(h.messages.at(-1).message, /reactivated/i);
    assert.equal(h.forms[0].dirty, false);
});

test('revoked guardian action discards delayed response from a switched session', async () => {
    const h = fixture('guardian-reactivation.js', 'renderRevokedGuardianLinks');
    h.buttons.find(item => item.textContent === 'Review reactivation').listeners.click();
    h.submit(0); await h.flush();
    h.session = false; h.writes[0].resolve({ studentReference }); await h.flush();
    assert.equal(h.renders.length, 0);
    assert.equal(h.messages.length, 0);
});

test('inline guardian workflows preserve authorized versioned APIs and shared parent guard', () => {
    const parent = read('EduOS.App/wwwroot/js/students/guardians.js');
    const link = read('EduOS.App/wwwroot/js/students/guardian-link-edit.js');
    const reactivate = read('EduOS.App/wwwroot/js/students/guardian-reactivation.js');
    assert.match(parent, /state\.registerInline/);
    assert.match(parent, /state\.clearInline\('active'\)/);
    assert.match(parent, /state\.confirmStudentSwitch/);
    assert.match(link, /rowVersion: guardian\.rowVersion/);
    assert.match(reactivate, /rowVersion: guardian\.rowVersion/);
    assert.match(read('EduOS.App/Controllers/Api/StudentGuardiansController.cs'), /\[AutoValidateAntiforgeryToken\]/);
});
