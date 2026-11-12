const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const draft = require('../../EduOS.App/wwwroot/js/platform/page-assistant-filter-draft.js');

function input(overrides = {}) {
    const events = [];
    return { dataset: { assistantFilter: 'true' }, tagName: 'INPUT', type: 'search',
        disabled: false, readOnly: false, isConnected: true, maxLength: 100,
        value: '', closest: () => null, getClientRects: () => [1],
        dispatchEvent: event => events.push(event), events, ...overrides };
}
const host = { Event: class { constructor(type, options) { this.type = type; this.bubbles = options.bubbles; } } };

test('bilingual search drafts never interpret approval or posting requests', () => {
    assert.deepEqual(draft.parseCommand('filter Search Students by Rahim'),
        { label: 'Search Students', value: 'Rahim' });
    assert.deepEqual(draft.parseCommand('শিক্ষার্থী খুঁজুন: রহিম'),
        { label: 'শিক্ষার্থী', value: 'রহিম' });
    assert.equal(draft.parseCommand('approve student payment'), null);
    assert.equal(draft.parseCommand('delete invoice'), null);
    assert.equal(draft.parseCommand('filter Name by ' + 'x'.repeat(81)), null);
    assert.equal(draft.parseCommand('filter Name by a\nb'), null);
});
test('only visible opted-in search inputs can accept a draft', () => {
    for (const bad of [{ dataset: {} }, { type: 'file' }, { type: 'password' },
        { tagName: 'SELECT' }, { disabled: true }, { readOnly: true },
        { isConnected: false }, { closest: () => ({ hidden: true }) },
        { getClientRects: () => [] }]) {
        const target = input(bad);
        assert.equal(draft.isSafeTarget(target), false);
        assert.equal(draft.applyDraft(target, 'Rahim', host), false);
        assert.equal(target.value, '');
    }
});
test('search draft and undo dispatch input without form submission', () => {
    const target = input({ value: 'Previous', maxLength: 100 });
    assert.equal(draft.applyDraft(target, 'Rahim', host), true);
    assert.equal(target.value, 'Rahim');
    assert.equal(target.events[0].type, 'input');
    assert.equal(target.events[0].bubbles, true);
    assert.equal(draft.applyDraft(target, 'Previous', host), true);
    assert.equal(target.value, 'Previous');
    assert.equal(draft.applyDraft(target, 'x'.repeat(101), host), false);
    assert.equal(draft.applyDraft(target, 'Other', {}), false);
    assert.equal(target.value, 'Previous');
});
test('page assistant requires review, session proof and explicit undo with opt-in views', () => {
    const root = path.resolve(__dirname, '../..');
    const read = p => fs.readFileSync(path.join(root, p), 'utf8');
    const assistant = read('EduOS.App/wwwroot/js/platform/page-assistant.js');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(assistant, /filters\?\.parseCommand\(requested\)/);
    assert.match(assistant, /action\(t\('Apply search draft'/);
    assert.match(assistant, /action\(t\('Undo draft'/);
    assert.match(assistant, /!currentContext\(\) \|\| pending !== target/);
    assert.doesNotMatch(assistant, /\bfetch\s*\(|\.submit\s*\(|\.requestSubmit\s*\(/);
    assert.match(layout, /page-assistant-filter-draft\.js/);
    for (const page of ['EduOS.App/Views/Students/Index.cshtml', 'EduOS.App/Views/Admissions/Index.cshtml'])
        assert.match(read(page), /data-assistant-filter="true"/);
});

test('bilingual sensitive accessible names and dependent widgets cannot be voice-filtered', () => {
    const cases = [
        { name: 'BankAccount' },
        { labels: [{ textContent: 'বেতন হিসাব' }] },
        { getAttribute: key => key === 'aria-label' ? 'Payment reference' : null },
        { getAttribute: key => key === 'placeholder' ? 'মোবাইল' : null },
        { getAttribute: key => key === 'role' ? 'combobox' : null },
        { getAttribute: key => key === 'aria-controls' ? 'dependent-list' : null },
        { closest: selector => selector.includes('data-assistant-dependent') ? {} : null },
        { ownerDocument: { getElementById: () => ({ textContent: 'ব্যাংক' }) },
          getAttribute: key => key === 'aria-labelledby' ? 'protected-label' : null }
    ];
    for (const change of cases) {
        const target = input(change);
        assert.equal(draft.isSafeTarget(target), false);
        assert.equal(draft.applyDraft(target, 'Proposed', host), false);
        assert.equal(target.value, '');
    }
});
test('search draft rejects detached or reclassified targets after input listeners', () => {
    const removed = input({ dispatchEvent() { this.isConnected = false; } });
    assert.equal(draft.applyDraft(removed, 'New', host), false);
    const revoked = input({ dispatchEvent() { this.dataset.assistantFilter = 'false'; } });
    assert.equal(draft.applyDraft(revoked, 'New', host), false);
});
test('search draft supports controlled native setters and does not submit', () => {
    let nativeCalls = 0;
    const target = input();
    const controlledHost = {
        ...host,
        HTMLInputElement: { prototype: Object.defineProperty({}, 'value', {
            set(value) { nativeCalls++; this.value = value; }
        }) }
    };
    assert.equal(draft.applyDraft(target, 'Rahim', controlledHost), true);
    assert.equal(nativeCalls, 1);
    assert.deepEqual(target.events.map(x => x.type), ['input']);
});
