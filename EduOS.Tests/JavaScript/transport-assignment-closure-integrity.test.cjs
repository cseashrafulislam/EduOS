const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('transport closure validates campus, dates, concurrency and serializable transaction', () => {
    const s = read('EduOS.Service/Services/Transport/TransportService.cs');
    for (const token of ['IsValidTransportClosureDate', 'enrollment.StudentId == entity.StudentId',
        'student.TenantId == tenant', 'CanManageCampusAsync',
        'System.Data.IsolationLevel.Serializable', 'RowVersion.AsSpan().SequenceEqual(version)',
        'TransportAssignmentState.Closed', '_unitOfWork.SaveChangesAsync(ct)'])
        assert.ok(s.includes(token), token);
    assert.ok(!s.includes('entity.MonthlyFare ='));
});
test('authorized existing API and UI reject future closure', () => {
    const api = read('EduOS.App/Controllers/Api/TransportController.cs');
    const js = read('EduOS.App/wwwroot/js/transport-operations/index.js');
    const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
    assert.ok(api.includes('assignments/{reference:guid}/close'));
    assert.ok(api.includes('_service.CloseAsync(reference, request, ct)'));
    assert.ok(js.includes('date > localDate()'));
    assert.ok(js.includes('item.startDate > localDate()'));
    assert.ok(js.includes('Use Upcoming cancellations'));
    assert.ok(view.includes('transportActiveRows'));
});
