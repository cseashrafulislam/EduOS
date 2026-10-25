const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/hostel-operations/bed-label-correction.js'), 'utf8');
const inventory = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/hostel-operations/bed-inventory.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/HostelOperations/Index.cshtml'), 'utf8');

test('bed label correction has an authenticated, antiforgery-protected PUT and row version', () => {
    new Function(js);
    assert.match(js, /\/api\/hostel\/beds\//);
    assert.match(js, /\/number/);
    assert.match(js, /method: 'PUT'/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /rowVersion: snapshot\.rowVersion/);
    assert.match(js, /credentials: 'same-origin'/);
});
test('historical beds cannot open correction and inventory refreshes on success', () => {
    assert.match(js, /bed\.hasAllocationHistory/);
    assert.match(inventory, /correct\.disabled = item\.hasAllocationHistory/);
    assert.match(inventory, /hostel:bed-number-corrected/);
    assert.match(view, /id="hostelBedNumberForm"/);
    assert.match(view, /bed-label-correction\.js/);
});
