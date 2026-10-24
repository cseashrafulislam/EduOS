const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/transport-operations/route-vehicle-reschedule.js'), 'utf8');

function harness(answer) {
    const calls = [], messages = [];
    const u = {
        today: () => '2026-10-25',
        api: async (...args) => { calls.push(args); return { success: true }; },
        notify: (...args) => messages.push(args),
        load: async () => { calls.push(['reload']); }
    };
    const context = { window: { transportLink: u, prompt: () => answer }, Date, Number, encodeURIComponent };
    vm.runInNewContext(source, context);
    return { u, calls, messages };
}

test('submits future reschedule with row version and refreshes', async () => {
    const h = harness('2026-11-08');
    await h.u.rescheduleUpcomingLink({ id: 12, effectiveFrom: '2026-11-01', effectiveTo: '2026-12-01',
        isCurrent: true, isCancelled: false, rowVersion: 'abc' });
    assert.equal(h.calls[0][0], '/api/transport/route-vehicle-links/12/reschedule-start');
    assert.equal(h.calls[0][1], 'POST');
    assert.equal(h.calls[0][2].effectiveFrom, '2026-11-08');
    assert.equal(h.calls[0][2].rowVersion, 'abc');
    assert.deepEqual(h.calls[1], ['reload']);
});

test('rejects date beyond assignment end without an API write', async () => {
    const h = harness('2027-01-01');
    await h.u.rescheduleUpcomingLink({ id: 12, effectiveFrom: '2026-11-01', effectiveTo: '2026-12-01',
        isCurrent: true, isCancelled: false, rowVersion: 'abc' });
    assert.equal(h.calls.length, 0);
    assert.equal(h.messages.length, 1);
});

test('does not edit operating or cancelled links', async () => {
    const h = harness('2026-11-08');
    await h.u.rescheduleUpcomingLink({ id: 1, effectiveFrom: '2026-10-20', isCurrent: true, isCancelled: false });
    await h.u.rescheduleUpcomingLink({ id: 2, effectiveFrom: '2026-11-01', isCurrent: false, isCancelled: true });
    assert.equal(h.calls.length, 0);
});
