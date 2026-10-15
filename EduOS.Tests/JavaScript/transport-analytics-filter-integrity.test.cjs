const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const report = read('EduOS.App/wwwroot/js/transport-operations/capacity-report.js');
const fleet = read('EduOS.App/wwwroot/js/transport-operations/fleet-insights.js');
const view = read('EduOS.App/Views/TransportOperations/_CapacityReport.cshtml');

test('capacity report invalidates stale campus/search results before rendering', () => {
    assert.match(view, /id="transportCapacityCampus"/);
    assert.match(report, /transportCapacitySearch'\)\.addEventListener\('input', invalidate\)/);
    assert.match(report, /transportCapacityCampus'\)\.addEventListener\('change', invalidate\)/);
    assert.match(report, /current !== sequence \|\| snapshot !== query\(\)\.toString\(\)/);
    assert.match(report, /loadedFilter = null/);
    assert.match(report, /transportCapacityRows'\)\.replaceChildren\(\)/);
    assert.match(report, /transportCapacityExport'\)\.disabled = true/);
});
test('CSV export cannot deliver a stale campus or search selection', () => {
    assert.match(report, /loadedFilter !== query\(false\)\.toString\(\)/);
    assert.match(report, /requestSerial !== sequence \|\| filters !== query\(false\)\.toString\(\)/);
    assert.match(report, /credentials: 'same-origin'/);
});
test('fleet dashboard clears old-campus metrics and rejects late responses', () => {
    assert.match(fleet, /transportCapacitySearch'\)\?\.addEventListener\('input', invalidate\)/);
    assert.match(fleet, /transportCapacityCampus'\)\?\.addEventListener\('change', invalidate\)/);
    assert.match(fleet, /transportFleetInsightsMetrics'\)\.replaceChildren\(\)/);
    assert.match(fleet, /token !== sequence \|\| filterSnapshot !== filters\(\)/);
    assert.doesNotMatch(fleet, /innerHTML/);
    new Function(report); new Function(fleet);
});
