const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/hostel-operations/upcoming-start-reschedule.js'), 'utf8');

const date = offset => {
    const d = new Date();
    d.setDate(d.getDate() + offset);
    return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
        String(d.getDate()).padStart(2, '0')].join('-');
};

function harness() {
    const sent = [], messages = [], events = {};
    let attempts = 0, nonce = 0;
    const element = id => ({
        id, value: '', disabled: false,
        classList: { add() {}, remove() {} },
        focus() {}, scrollIntoView() {},
        addEventListener(name, callback) { events[id + ':' + name] = callback; }
    });
    const elements = Object.fromEntries([
        'hostelStartReschedulePanel', 'hostelRescheduleStart', 'hostelRescheduleContext',
        'hostelRescheduleFinanceClearance', 'hostelRescheduleSave', 'hostelRescheduleCancel', 'hostelAlert'
    ].map(id => [id, element(id)]));
    Object.defineProperty(elements.hostelAlert, 'textContent', { set(value) { messages.push(value); } });
    const window = {
        crypto: { randomUUID() { return `00000000-0000-4000-8000-${String(++nonce).padStart(12, '0')}`; } },
        dispatchEvent() {}
    };
    vm.runInNewContext(source, {
        window, document: {
            getElementById: id => elements[id],
            querySelector: () => ({ content: 'csrf-token' })
        }, Date, Number,
        confirm: () => true,
        CustomEvent: function Event(name) { this.type = name; },
        fetch: async (_url, options) => {
            sent.push(JSON.parse(options.body));
            attempts++;
            if (attempts === 1) throw new Error('network failure');
            return { ok: true, json: async () => ({ success: true }) };
        }
    });
    return { window, elements, messages, sent, events, get nonce() { return nonce; } };
}

test('hostel start reschedule preserves one request key across a failed network attempt', async () => {
    const h = harness();
    h.window.hostelUpcomingStartReschedule.begin({
        id: 8, startDate: date(30), monthlyRent: 0,
        studentName: 'Student', hostelName: 'Hostel', roomNumber: '1', bedNumber: 'A', rowVersion: 'old-version'
    });
    h.elements.hostelRescheduleStart.value = date(25);
    await h.events['hostelRescheduleSave:click']();
    await h.events['hostelRescheduleSave:click']();
    assert.equal(h.sent.length, 2);
    assert.equal(h.nonce, 1);
    assert.equal(h.sent[0].clientRequestId, h.sent[1].clientRequestId);
    assert.equal(h.sent[0].startDate, date(25));
    assert.equal(h.sent[0].rowVersion, 'old-version');
    assert.ok(h.messages.some(message => message.includes('rescheduled')));
});

test('chargeable reservation requires approved Finance clearance before any mutation', async () => {
    const h = harness();
    h.window.hostelUpcomingStartReschedule.begin({
        id: 8, startDate: date(30), monthlyRent: 1500,
        studentName: 'Student', hostelName: 'Hostel', roomNumber: '1', bedNumber: 'A', rowVersion: 'old-version'
    });
    h.elements.hostelRescheduleStart.value = date(25);
    await h.events['hostelRescheduleSave:click']();
    assert.ok(h.messages.some(message => message.includes('approved Finance clearance')));
    assert.equal(h.sent.length, 0);
    h.elements.hostelRescheduleFinanceClearance.value = '127';
    await h.events['hostelRescheduleSave:click']();
    assert.equal(h.sent.length, 1);
    assert.equal(h.sent[0].financeClearanceId, 127);
});
