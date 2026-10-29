'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const controller = read('EduOS.App/Controllers/HostelOperationsController.cs');
const roster = read('EduOS.App/wwwroot/js/hostel-operations/resident-roster.js');
const history = read('EduOS.App/wwwroot/js/hostel-operations/allocation-history.js');
const rosterView = read('EduOS.App/Views/HostelOperations/ResidentRosterPrint.cshtml');
const historyView = read('EduOS.App/Views/HostelOperations/AllocationHistoryPrint.cshtml');
test('hostel print actions reauthorize bounded SQL pages and preserve module gate', () => {
    assert.match(controller, /\[RequireModule\("HOSTEL"\)\]/);
    for (const action of ['PrintResidentRoster', 'PrintAllocationHistory']) {
        const part = controller.slice(controller.indexOf('public async Task<IActionResult> ' + action) - 230);
        assert.match(part, /\[Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)\]/);
        assert.match(part, /\[ResponseCache\(NoStore = true, Location = ResponseCacheLocation.None\)\]/);
    }
    assert.match(controller, /const int pageSize = 25;/);
    assert.match(controller, /hostelService.GetResidentRosterAsync/);
    assert.match(controller, /hostelService.GetAllocationHistoryAsync/);
    assert.match(controller, /if \(!result.Success\) return StatusCode\(result.StatusCode, result\)/);
});
test('print pages render only privacy-minimized historical roster and lineage', () => {
    for (const view of [rosterView, historyView]) {
        assert.match(view, /Layout = null/);
        assert.match(view, /@@page \{ size: A4 landscape/);
        assert.match(view, /@Model.TotalPages/);
        assert.match(view, /@foreach \(var item in Model.Items\)/);
        assert.match(view, /noindex,nofollow/);
        for (const secret of ['MonthlyRent','SecurityDeposit','Phone','Email','Guardian','PreviousAllocationId'])
            assert.ok(!view.includes(secret), secret);
    }
    assert.match(rosterView, /not proof of physical check-in/);
    assert.match(historyView, /IsTransferSuccessor/);
});
test('print actions require an applied unchanged filter and use direct user gesture', () => {
    assert.doesNotThrow(() => new Function(roster));
    assert.doesNotThrow(() => new Function(history));
    assert.match(roster, /applied === null \|\| snapshot !== applied/);
    assert.match(history, /loadedFilter === null \|\| snapshot !== loadedFilter/);
    assert.match(roster, /window.open\('\/hostel\/residents\/print\?'/);
    assert.match(history, /window.open\('\/hostel\/allocations\/history\/print\?'/);
    for (const source of [roster, history]) {
        assert.match(source, /'_blank', 'noopener,noreferrer'/);
        assert.doesNotMatch(source, /innerHTML/);
    }
});
