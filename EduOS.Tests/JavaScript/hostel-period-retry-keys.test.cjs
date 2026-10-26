const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const date = days => {
    const d = new Date(); d.setDate(d.getDate() + days);
    return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
        String(d.getDate()).padStart(2, '0')].join('-');
};

const commands = [
    { file: 'no-charge-stay-extension.js', prefix: 'hostelNoChargeExtension',
        operation: 'hostelNoChargeStayExtension', capability: 'canExtendNoCharge',
        start: 1, end: 30 },
    { file: 'upcoming-no-charge-shortening.js', prefix: 'hostelNoChargeShortening',
        operation: 'hostelUpcomingNoChargeShortening', capability: 'canShortenNoCharge',
        start: 20, end: 60 }
];

for (const command of commands) {
    test(`${command.file} retries with a stable request ID`, async () => {
        const script = fs.readFileSync(path.join(__dirname,
            '../../EduOS.App/wwwroot/js/hostel-operations', command.file), 'utf8');
        const events = {}, requests = [], notices = [];
        let nonce = 0, attempt = 0;
        const element = () => ({ value: '', disabled: false, classList: { add() {}, remove() {} },
            focus() {}, scrollIntoView() {},
            addEventListener(name, callback) { this.click = callback; } });
        const elements = Object.fromEntries([
            command.prefix + 'Panel', command.prefix + 'End', command.prefix + 'Context',
            command.prefix + 'Save', command.prefix + 'Cancel', 'hostelAlert'
        ].map(id => [id, element()]));
        Object.defineProperty(elements.hostelAlert, 'textContent', { set(value) { notices.push(value); } });
        const window = { crypto: { randomUUID: () =>
            `00000000-0000-4000-8000-${String(++nonce).padStart(12, '0')}` },
            dispatchEvent() {} };
        const vm = require('node:vm');
        vm.runInNewContext(script, {
            window, document: { getElementById: id => elements[id],
                querySelector: () => ({ content: 'csrf' }) },
            Date, Number, confirm: () => true, encodeURIComponent,
            CustomEvent: function E(type) { this.type = type; },
            fetch: async (_url, args) => {
                requests.push(JSON.parse(args.body));
                if (++attempt === 1) throw new Error('network timeout');
                return { ok: true, json: async () => ({ success: true }) };
            }
        });
        window[command.operation].begin({ id: 81, startDate: date(command.start),
            endDate: date(command.end), rowVersion: 'rowversion',
            studentName: 'Student', hostelName: 'Hostel', roomNumber: '1', bedNumber: 'A',
            [command.capability]: true });
        await elements[command.prefix + 'Save'].click();
        await elements[command.prefix + 'Save'].click();
        assert.equal(requests.length, 2);
        assert.equal(nonce, 1);
        assert.equal(requests[0].clientRequestId, requests[1].clientRequestId);
        assert.equal(requests[0].rowVersion, 'rowversion');
        assert.ok(notices.some(message => message.includes('timeout')));
    });
}
