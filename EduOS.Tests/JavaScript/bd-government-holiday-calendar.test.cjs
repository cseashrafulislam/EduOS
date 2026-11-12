'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const catalog = read('EduOS.Service/Services/Academic/BangladeshGovernmentHolidayCatalog.cs');
const service = read('EduOS.Service/Services/Academic/AcademicCalendarService.cs');
const dto = read('EduOS.Core/DTOs/Academic/AcademicDtos.cs');
const api = read('EduOS.App/Controllers/Api/AcademicCalendarsController.cs');
const view = read('EduOS.App/Views/AcademicCalendar/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/academic/government-holidays.js');

test('both government holiday gazettes are versioned, bounded and published by MoPA', () => {
    assert.match(catalog, /Version = "BD-MOPA-2026-2027-v2"/);
    assert.match(catalog, /mopa\.gov\.bd/);
    assert.match(catalog, /Day\(2026, 2, 21, "BD26-LANGUAGE"/);
    assert.match(catalog, /Day\(2026, 10, 21, "BD26-DURGA-DASHAMI"/);
    assert.match(catalog, /"BD26-REVOLUTION"/);
    assert.match(catalog, /Day\(2027, 11, 7, "BD27-REVOLUTION"/);
    assert.match(catalog, /Period\(2027, 3, 8, 12, "BD27-EID-FITR"/);
    assert.match(catalog, /Period\(2027, 5, 15, 20, "BD27-EID-ADHA"/);
    assert.match(catalog, /Day\(2027, 5, 20, "BD27-BUDDHA-PURNIMA"/);
    assert.doesNotMatch(catalog, /"Chaitra Sankranti"|"Optional leave"/);
    assert.match(catalog, /x\.Start >= from && x\.End <= to/);
});

test('server retains tenant/campus authorization and atomic idempotent imports', () => {
    assert.match(api, /HttpGet\("government-holidays\/preview"\)/);
    assert.match(api, /HttpPost\("government-holidays\/import"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /RequireModule\("ACADEMIC"\)/);
    assert.match(service, /if \(!CanManage\(\)\) return Denied<GovernmentHolidayPreviewDto>/);
    assert.match(service, /await CanManageCampusAsync\(campusId, ct\)/);
    assert.match(service, /string\.Equals\(tenant, "BD"/);
    assert.match(service, /request\.Confirmed/);
    assert.match(service, /SelectedCodes\.Distinct\(StringComparer\.Ordinal\)/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /sp_getapplock/);
    assert.match(service, /NewValue = JsonSerializer\.Serialize\(new HolidayImportReceipt/);
    assert.match(service, /existing.*calendar holidays conflict/is);
    assert.match(service, /x\.EventType == CalendarEventKind\.Holiday/);
    assert.match(service, /await _unitOfWork\.SaveChangesAsync\(token\)/);
    assert.match(service, /return Error<ImportGovernmentHolidaysResultDto>\("Calendar changed during import/);
    assert.doesNotMatch(service, /db\.Students\.Add|db\.AccountingJournals\.Add/);
});

test('client requires source-backed preview and a second explicit confirmation', () => {
    new Function(js);
    assert.match(view, /id="calendarGovernmentPreview"/);
    assert.match(view, /id="calendarGovernmentImport"/);
    assert.match(view, /id="calendarGovernmentConfirm"/);
    assert.match(view, /government-holidays\.js" asp-append-version/);
    assert.match(js, /calendarGovernmentPreview/);
    assert.match(js, /government-holidays\/preview/);
    assert.match(js, /government-holidays\/import/);
    assert.match(js, /RequestVerificationToken: csrf/);
    assert.match(js, /state\.preview\.sourceVersion/);
    assert.match(js, /getSelected\(\)\.sort\(\)/);
    assert.match(js, /clientRequestId: state\.requestId/);
    assert.match(js, /confirmed: true/);
    assert.match(js, /!confirm\.checked/);
    assert.match(js, /crypto\.randomUUID\(\)/);
    assert.match(js, /textContent = String\(text\)/);
    assert.doesNotMatch(js, /innerHTML|localStorage/);
});

test('governmental source and optional/religious holidays cannot change tenant defaults', () => {
    assert.match(view, /religion-specific|ঐচ্ছিক/);
    assert.match(service, /CampusId = request\.CampusId/);
    assert.match(service, /AcademicTermId = null/);
    assert.match(service, /IsHoliday = true/);
    assert.match(service, /IsPublicVisible = true/);
    assert.match(service, /Source: Bangladesh Ministry of Public Administration/);
    assert.match(service, /moon sighting and official amendments/);
    assert.doesNotMatch(service, /SavePolicyAsync\(new|WeekendDay1 = DayOfWeek/);
});
