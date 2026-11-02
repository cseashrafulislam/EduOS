const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('visitor UI is bilingual, scoped and safely renders server-provided PII', () => {
    const page = read('EduOS.App/Views/FacilityVisitors/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/facilities/visitors.js');
    const api = read('EduOS.App/Controllers/Api/FacilityVisitorsApiController.cs');
    assert.match(page, /CultureInfo\.CurrentUICulture/);
    assert.match(page, /@Html\.AntiForgeryToken\(\)/);
    assert.match(page, /প্রি-রেজিস্ট্রেশন/);
    assert.match(page, /retention deletion and export are not yet enabled/);
    assert.match(script, /credentials: 'same-origin'/);
    assert.match(script, /cache: 'no-store'/);
    assert.match(script, /RequestVerificationToken/);
    assert.match(script, /textContent = row\.visitorName/);
    assert.doesNotMatch(script, /\.innerHTML\s*=/);
    assert.match(script, /requestEpoch/);
    assert.match(script, /pendingCreateId/);
    assert.match(script, /commandIds/);
    assert.match(script, /lastHostCampus/);
    assert.match(script, /Report incident/);
    assert.match(script, /data-visitor-history/);
    assert.match(script, /event\.reason/);
    assert.match(api, /RequireModule\("FACILITIES"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /EnableRateLimiting/);
});

test('visitor transitions enforce independent host approval and active badge uniqueness', () => {
    const service = read('EduOS.Service/Services/Facilities/FacilityVisitorService.cs');
    const context = read('EduOS.Persistence/Context/EduOSDbContext.cs');
    const migration = read('EduOS.Persistence/Migrations/20261102173000_V3FacilityVisitorsInitial.cs');
    assert.match(service, /visit\.CreatedBy == user\.UserId/);
    assert.match(service, /ActiveHosts\(visit\.CampusId\)/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /ClientCommandId/);
    assert.match(service, /VisitStartUtc\.AddMinutes\(-30\)/);
    assert.match(service, /Badge is already checked in/);
    assert.match(service, /HostLiveAsync/);
    assert.match(service, /action == "incident"/);
    assert.match(service, /CheckedOutAtUtc\.Value\.AddHours\(24\)/);
    assert.match(service, /Manager && e\.ActionCode == "incident"/);
    assert.match(context, /IX_FacilityVisitorVisits|FacilityVisitorVisit/);
    assert.match(migration, /\[StateCode\] = N'CheckedIn'/);
    assert.match(migration, /FK_FacilityVisitorEvents_FacilityVisitorVisits/);
});
