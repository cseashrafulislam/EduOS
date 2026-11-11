const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const fieldDraft = require('../../EduOS.App/wwwroot/js/platform/page-assistant-field-draft.js');
const assistant = require('../../EduOS.App/wwwroot/js/platform/page-assistant.js');

const eventHost = { Event: class { constructor(type, options) { this.type = type; this.bubbles = options.bubbles; } } };
function field(overrides = {}) {
    const events = [];
    return {
        dataset: { assistantDraft: 'true' }, form: { dataset: { assistantDraft: 'true' } },
        tagName: 'INPUT', type: 'text', name: 'PublicNote', id: 'PublicNote', autocomplete: 'off',
        disabled: false, readOnly: false, isConnected: true, value: '', maxLength: 120,
        minLength: 0, getAttribute: () => null, closest: () => null,
        getClientRects: () => [1], dispatchEvent: event => events.push(event), events,
        ...overrides
    };
}
test('bilingual field prefill is a draft only, never an ERP transaction command', () => {
    assert.deepEqual(fieldDraft.parseCommand('fill Public note with Class materials ready'),
        { label: 'Public note', value: 'Class materials ready' });
    assert.deepEqual(fieldDraft.parseCommand('বিবরণ লিখুন: আগামী সপ্তাহের পাঠ'),
        { label: 'বিবরণ', value: 'আগামী সপ্তাহের পাঠ' });
    for (const phrase of ['approve payment', 'submit attendance', 'post payroll', 'delete student',
        'fill Name with a\nb', 'fill Name with ' + 'x'.repeat(121)])
        assert.equal(fieldDraft.parseCommand(phrase), null, phrase);
});
test('opt-in both form and field; refuse sensitive, hidden, dependent, disabled and unapproved targets', () => {
    for (const changes of [
        { dataset: {} }, { form: { dataset: {} } }, { form: null }, { type: 'password' },
        { type: 'number' }, { tagName: 'SELECT' }, { name: 'BankAccount' },
        { id: 'GuardianMobile' }, { name: 'StudentId' }, { disabled: true },
        { readOnly: true }, { isConnected: false }, { closest: () => ({ hidden: true }) },
        { getClientRects: () => [] }
    ]) {
        const target = field(changes);
        assert.equal(fieldDraft.safeTarget(target), false, JSON.stringify(changes));
        assert.equal(fieldDraft.apply(target, 'New text', eventHost), false);
        assert.equal(target.value, '');
    }
});
test('draft obeys maxlength, minlength, pattern and input event; never calls submit', () => {
    const target = field({ maxLength: 15, minLength: 3, getAttribute: name => name === 'pattern' ? '[A-Z ]+' : null });
    assert.equal(fieldDraft.apply(target, 'x', eventHost), false);
    assert.equal(fieldDraft.apply(target, 'TOO LONG FIELD VALUE', eventHost), false);
    assert.equal(fieldDraft.apply(target, 'mixed Case', eventHost), false);
    assert.equal(fieldDraft.apply(target, 'PUBLIC NOTE', eventHost), true);
    assert.equal(target.value, 'PUBLIC NOTE');
    assert.deepEqual(target.events.map(x => x.type), ['input']);
    assert.equal(target.events[0].bubbles, true);
});
test('assistant preview requires explicit confirm and supports undo without navigation or submission', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/page-assistant.js'), 'utf8');
    const layout = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
    assert.match(source, /formDraft\?\.parseCommand\(requested\)/);
    assert.match(source, /Confirm field draft/);
    assert.match(source, /Undo field draft/);
    assert.match(source, /String\(target\.value \|\| ''\) !== previous/);
    assert.match(source, /!currentContext\(\)/);
    assert.match(layout, /page-assistant-field-draft\.js/);
    assert.doesNotMatch(source, /\bfetch\s*\(|\.submit\s*\(|\.requestSubmit\s*\(/);
    assert.doesNotMatch(source, /(?:target|field|node)\.value\s*=/);
    assert.equal(assistant.matchLabel([{ labels: ['Public note'] }], 'Public note').kind, 'match');
});

test('localized labels and placeholder cannot disguise sensitive fields behind generic names', () => {
    const cases = [
        { labels: [{ textContent: 'বেতন' }] },
        { labels: [{ textContent: 'Bank Account' }] },
        { getAttribute: key => key === 'aria-label' ? 'উপস্থিতি' : null },
        { getAttribute: key => key === 'placeholder' ? 'মোবাইল নম্বর' : null },
        { dataset: { assistantDraft: 'true', assistantLabel: 'Payment' } },
        { getAttribute: key => key === 'title' ? 'জাতীয় পরিচয়' : null }
    ];
    for (const change of cases) {
        const target = field(change);
        assert.equal(fieldDraft.safeTarget(target), false, JSON.stringify(change));
        assert.equal(fieldDraft.apply(target, 'draft', eventHost), false);
        assert.equal(target.value, '');
    }
    assert.equal(fieldDraft.safeTarget(field({ labels: [{ textContent: 'Public note / সাধারণ মন্তব্য' }] })), true);
});
test('explicit undo restores empty optional field with minlength/pattern without submitting', () => {
    const target = field({
        value: 'PUBLIC NOTE', minLength: 3, required: false, checkValidity: () => true,
        getAttribute: key => key === 'pattern' ? '[A-Z ]+' : null
    });
    assert.equal(fieldDraft.apply(target, '', eventHost), false, 'ordinary empty draft must fail');
    assert.equal(fieldDraft.apply(target, '', eventHost, { restoreEmpty: true }), true);
    assert.equal(target.value, '');
    assert.deepEqual(target.events.map(x => x.type), ['input']);
    const required = field({
        value: 'PUBLIC NOTE', minLength: 3, required: true, checkValidity: () => true,
        getAttribute: key => key === 'pattern' ? '[A-Z ]+' : null
    });
    assert.equal(fieldDraft.apply(required, '', eventHost, { restoreEmpty: true }), false);
    assert.equal(required.value, 'PUBLIC NOTE');
    const assistantSource = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/page-assistant.js'), 'utf8');
    assert.match(assistantSource, /restoreEmpty: previous === ''/);
});
