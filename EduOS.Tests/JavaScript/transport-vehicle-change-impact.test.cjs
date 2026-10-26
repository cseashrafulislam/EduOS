const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/transport-operations/vehicle-setup.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/Views/TransportOperations/_VehicleSetup.cshtml'), 'utf8');

test('vehicle change impact uses canonical reference, rowversion and bounded query', () => {
    assert.match(script, /\/change-impact\?/);
    assert.match(script, /rowVersion: item\.rowVersion/);
    assert.match(script, /capacity: String\(value\)/);
    assert.match(script, /isActive: String\(active\)/);
    assert.match(view, /Vehicle inventory and capacity control/);
});
test('preview is safe, stale responses are discarded, and save rechecks preflight', () => {
    assert.match(script, /impact\.textContent/);
    assert.doesNotMatch(script, /impact\.innerHTML/);
    assert.match(script, /stamp !== impactSequence/);
    assert.match(script, /await checkImpact\(\)/);
    assert.match(script, /if \(!preview\.canApply\)/);
    assert.match(script, /RequestVerificationToken/);
});
