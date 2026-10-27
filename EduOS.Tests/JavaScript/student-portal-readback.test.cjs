'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/student-portal/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/StudentPortal/Index.cshtml'), 'utf8');

test('student portal uses canonical DTO names instead of stale display aliases', () => {
    for (const field of ['rollNo', 'attendanceDate', 'checkInTime', 'checkOutTime', 'assessmentName',
        'obtainedMarks', 'totalMarks', 'gradeLetter', 'meritPosition', 'invoiceNumber',
        'totalAmount', 'paidAmount', 'dueAmount', 'vehicleNumber', 'maxMarks',
        'opensAt', 'dueAt', 'instructions']) assert.ok(source.includes(field), field);
    for (const legacy of ["['date','Date']", "['examName','Exam']", "['invoiceNo','Invoice']",
        "['vehicleNo','Vehicle']", "['totalMark','Full marks']"])
        assert.ok(!source.includes(legacy), legacy);
});

test('fee ledger and two separately paginated financial readbacks are requested', () => {
    assert.match(source, /api\(base \+ 'fees'\)/);
    assert.match(source, /api\(base \+ 'invoices' \+ queryPage\(/);
    assert.match(source, /api\(base \+ 'payments' \+ queryPage\(/);
    assert.match(source, /pageInfo\(invoices,/);
    assert.match(source, /pageInfo\(payments,/);
});

test('paged sections use items, totalCount, and separate page controls', () => {
    assert.match(source, /new Set\(\['attendance'/);
    assert.match(source, /data\.items/);
    assert.match(source, /totalCount/);
    assert.match(source, /resetPages\(\)/);
    for (const id of ['studentPortalPagination', 'studentPortalPrevious', 'studentPortalNext',
        'studentPortalPaymentsPagination', 'studentPortalPaymentsPrevious', 'studentPortalPaymentsNext'])
        assert.ok(view.includes('id="' + id + '"'), id);
});

test('portal renders untrusted data as text and discards stale responses', () => {
    assert.doesNotMatch(source, /innerHTML\s*=/);
    assert.match(source, /textContent\s*=/);
    assert.match(source, /token !== latestRequest/);
    assert.match(source, /encodeURIComponent\(student\)/);
});
