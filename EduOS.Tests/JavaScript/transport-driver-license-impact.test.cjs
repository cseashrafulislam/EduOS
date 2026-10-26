const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/transport-operations/driver-registry.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/Views/TransportOperations/_DriverRegistry.cshtml'), 'utf8');

test('license impact preview uses current rowversion and only the tenant-admin registry endpoint', () => {
    assert.match(script, /\/license-impact\?/);
    assert.match(script, /rowVersion: editing\.rowVersion/);
    assert.match(script, /proposedActive:/);
    assert.match(script, /query\.set\('proposedExpiry', expiry\)/);
    assert.match(view, /id="transportDriverLicenseImpactCheck"/);
});
test('impact display is safe text and invalidates stale responses', () => {
    assert.match(script, /impactBox\.textContent/);
    assert.doesNotMatch(script, /impactBox\.innerHTML/);
    assert.match(script, /current !== impactSequence/);
    assert.match(script, /addEventListener\('change', clearImpact\)/);
});
