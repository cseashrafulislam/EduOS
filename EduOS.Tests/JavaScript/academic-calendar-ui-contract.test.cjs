'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const script = fs.readFileSync(path.join(app, 'wwwroot/js/academic/calendar.js'), 'utf8');
const view = fs.readFileSync(path.join(app, 'Views/AcademicCalendar/Index.cshtml'), 'utf8');

test('calendar mutations carry antiforgery and canonical DateOnly payloads', () => {
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(script, /RequestVerificationToken: csrf/);
    assert.match(script, /fromDate: s\.fromDate, toDate: s\.toDate/);
    assert.match(script, /startDate, endDate, isHoliday:/);
    assert.match(script, /academicYearId: selected\?\.academicYearId \|\| s\.academicYearId/);
    assert.match(script, /campusId: selected \? selected\.campusId : s\.campusId/);
    assert.doesNotMatch(script, /T00:00:00';/);
});

test('calendar policy writes match canonical weekend DTO and restrict institution-wide management', () => {
    assert.match(view, /data-tenant-admin=/);
    assert.match(script, /weekendDay1: days\[0\], weekendDay2: days\[1\] \?\? null/);
    assert.match(script, /!isTenantAdmin && !s\.campusId/);
    assert.match(script, /updateManageScope\(\)/);
    assert.doesNotMatch(script, /weekendDays: days/);
});

test('calendar reports preserve event readback when weekend policy is absent and escape CSV formulas', () => {
    assert.match(script, /Promise\.allSettled\(/);
    assert.match(script, /calendarExportEvents/);
    assert.match(script, /calendarExportWorking/);
    assert.match(script, /\[=\+@-\]/);
    assert.match(view, /id="calendarExportEvents"/);
    assert.match(view, /id="calendarExportWorking"/);
});
