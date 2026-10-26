const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const read = file => fs.readFileSync(path.join(__dirname, '../..', file), 'utf8');

test('driver, route-vehicle and hostel fallback request IDs keep deployed hash domains', () => {
    const cases = [
        ['Transport/TransportService.CommandFingerprint.cs', 'DeriveDriverCommandRequestId', 'eduos:driver-extension:v1:'],
        ['Transport/TransportService.CommandFingerprint.cs', 'DeriveRouteVehicleCommandRequestId', 'eduos:route-vehicle-command:v1:'],
        ['Hostel/HostelService.CommandFingerprint.cs', 'DeriveHostelCommandRequestId', 'eduos:hostel-command:v1:']
    ];
    for (const [file, method, persistedHashDomain] of cases) {
        const source = read('EduOS.Service/Services/' + file);
        assert.ok(source.includes(`private static Guid ${method}(`), `${method} must remain available`);
        assert.ok(source.includes(persistedHashDomain), `${method} changed the deployed receipt key domain`);
        assert.match(source, /SHA256\.HashData\(Encoding\.UTF8\.GetBytes\(/);
        assert.match(source, /new Guid\(digest\.AsSpan\(0, 16\)\)/);
        assert.doesNotMatch(source, /\bLegacy\w*CommandRequestId\b/);
    }
});
