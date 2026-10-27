'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const view = read('EduOS.App/Views/Admissions/Index.cshtml');
const script = read('EduOS.App/wwwroot/js/admissions/index.js');

test('staff intake uses persisted contact address and configurable custom fields', () => {
    assert.doesNotMatch(view, /id="permanentAddress"|id="sameAddress"|id="previousInstitution"/);
    assert.match(view, /institution-configured intake custom fields/);
    assert.match(script, /presentAddress: valueOf\('presentAddress'\) \|\| null/);
    assert.match(script, /permanentAddress: null/);
    assert.match(script, /previousInstitution: null/);
    assert.match(script, /customResponses: collectAdmissionResponses\(\)/);
    assert.doesNotMatch(script, /function copyAddress/);
});
test('staff intake guards double submission and rotates retry keys on changed payload', () => {
    assert.match(script, /state\.submitting \|\| !form\.reportValidity\(\)/);
    assert.match(script, /state\.pendingFingerprint !== fingerprint/);
    assert.match(script, /request\.clientRequestId = state\.pendingRequestId/);
    assert.match(script, /state\.submitting = true/);
    assert.match(script, /state\.submitting = false/);
    assert.match(script, /state\.pendingFingerprint = null/);
});
