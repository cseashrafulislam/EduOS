const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const read = p => fs.readFileSync(path.join(app, p), 'utf8');

test('trip completion requires an authorized CSRF-protected server transition and rowversion', () => {
    const js = read('wwwroot/js/transport-operations/trips.js');
    const api = read('Controllers/Api/TransportTripsController.cs');
    assert.match(js, /item\.state === 'Dispatched'/);
    assert.match(js, /\/complete'/);
    assert.match(js, /rowVersion: item\.rowVersion/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /CompleteAsync/);
});
