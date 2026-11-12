'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/facilities/visitors.js'), 'utf8');

test('visitor action, create and policy writes keep their command ids until API success', () => {
    assert.match(source, /await api\('\/api\/facilities\/visitors\/' \+ row\.id \+ '\/' \+ action,[\s\S]*?commandIds\.delete\(key\)/);
    assert.match(source, /await api\('\/api\/facilities\/visitors', 'POST',[\s\S]*?pendingCreateId = crypto\.randomUUID\(\)/);
    assert.match(source, /await api\('\/api\/facilities\/visitors\/policies', 'POST',[\s\S]*?pendingPolicyId = crypto\.randomUUID\(\)/);
});
test('all three confirmed writes distinguish refresh failure from write failure', () => {
    assert.match(source, /const load = async \(propagateError = false\)/);
    assert.match(source, /const loadPolicies = async \(propagateError = false\)/);
    assert.equal((source.match(/await load\(true\);/g) || []).length, 2);
    assert.equal((source.match(/await loadPolicies\(true\);/g) || []).length, 1);
    assert.match(source, /Action saved, but the visit list could not refresh/);
    assert.match(source, /Visit saved, but the list could not refresh/);
    assert.match(source, /Policy request saved, but the list could not refresh/);
});
test('ordinary list reads continue reporting errors without throwing into event handlers', () => {
    assert.match(source, /if \(epoch === requestEpoch\) status\(e\.message\); if \(propagateError\) throw e;/);
    assert.match(source, /if \(epoch === policyEpoch\) status\(error\.message\); if \(propagateError\) throw error;/);
});
