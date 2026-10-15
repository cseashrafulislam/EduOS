const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Transport/TransportService.CapacityReport.cs'), 'utf8');
const ui = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/transport-operations/capacity-report.js'), 'utf8');

test('bounded transport CSV checks page count and unique vehicle references', () => {
    assert.match(service, /TotalCount > 1000/);
    assert.match(service, /next\.Data\.TotalCount != first\.Data\.TotalCount/);
    assert.match(service, /rows\.Any\(x => !uniqueVehicles\.Add\(x\.VehicleReference\)\)/);
    assert.match(service, /var trimmed = value\.TrimStart\(\)/);
});
test('export does not download a stale filter result', () => {
    assert.match(ui, /const filters = query\(false\)\.toString\(\)/);
    assert.match(ui, /filters !== query\(false\)\.toString\(\)/);
    assert.match(ui, /textContent/);
    assert.doesNotMatch(ui, /innerHTML/);
    new Function(ui);
});
