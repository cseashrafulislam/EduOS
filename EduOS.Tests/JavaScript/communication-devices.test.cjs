const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/CommunicationWorkspace/Index.cshtml'), 'utf8');
const entry = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/communication/workspace.js'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/communication/workspace-devices.js'), 'utf8');

test('registered device management is reachable from the existing workspace', () => {
    for (const id of ['cwDeviceRefresh', 'cwDeviceRows', 'cwDevicePrev', 'cwDeviceNext', 'cwDevicePage'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(entry, /workspace-devices\.js/);
    assert.match(entry, /initDevices\(\)/);
});
test('device actions use existing authenticated and antiforgery-aware API client', () => {
    assert.match(js, /api\(`\/api\/communication\/devices\?page=/);
    assert.match(js, /\/deactivate`, 'POST'/);
    assert.match(js, /window\.confirm/);
    assert.match(js, /cell\(row\.deviceId\)/);
    assert.doesNotMatch(js, /innerHTML|localStorage|sessionStorage/);
});
