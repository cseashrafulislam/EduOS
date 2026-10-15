const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const report = read('EduOS.App/wwwroot/js/hostel-operations/capacity.js');
const insights = read('EduOS.App/wwwroot/js/hostel-operations/occupancy-insights.js');
const api = read('EduOS.App/Controllers/Api/HostelController.cs');
const facilities = read('EduOS.Service/Services/Hostel/HostelService.Facilities.cs');

test('campus options reuse existing tenant and campus-authorized endpoint', () => {
    assert.match(view, /id="hostelCapacityCampus"/);
    assert.match(report, /\/api\/hostel\/facility-campuses/);
    assert.match(api, /\[HttpGet\("facility-campuses"\)\]/);
    assert.match(facilities, /GetFacilityCampusesAsync/);
    assert.match(facilities, /AllowedCampusIds\(tenant\)/);
    assert.doesNotMatch(report, /innerHTML/);
});
test('hostel report and CSV share campus and search filters', () => {
    assert.match(report, /params\.set\('campusId',campus\)/);
    assert.match(report, /\/api\/hostel\/capacity\/export\?' \+ query\(false\)/);
    assert.match(report, /hostelCapacityCampus'\)\.addEventListener\('change',filterChanged\)/);
    assert.match(report, /requestSerial !== serial/);
});
test('occupancy insights use the same campus selection and clear stale data', () => {
    assert.match(insights, /q\.set\('campusId', campus\)/);
    assert.match(insights, /hostelCapacityCampus'\)\?\.addEventListener\('change', stale\)/);
    assert.match(insights, /snapshot !== filters\(\)/);
    assert.doesNotMatch(insights, /innerHTML/);
    new Function(report); new Function(insights);
});
