'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Transport/TransportVehicleCaseService.cs');
const api = read('EduOS.App/Controllers/Api/TransportVehicleCasesController.cs');
const view = read('EduOS.App/Views/TransportOperations/_VehicleCases.cshtml');
const index = read('EduOS.App/Views/TransportOperations/Index.cshtml');
const script = read('EduOS.App/wwwroot/js/transport-operations/vehicle-cases.js');
const config = read('EduOS.Persistence/Configurations/TransportVehicleCaseConfiguration.cs');
for(const marker of ['item.TenantId == tenant','vehicle.TenantId == tenant',
 'campus.TenantId == tenant','AllowedCampuses()','ClientRequestId',
 'IsolationLevel.Serializable','RowVersion','Case changed. Reload before resolving.',
 'TotalCount > 500','Cell(row.Summary)'])
 assert.ok(service.includes(marker), marker);
for(const marker of ['[RequireModule("TRANSPORT")]','[AutoValidateAntiforgeryToken]',
 '[Authorize(Roles = "TenantAdmin,Principal,TransportManager")]','NoStore = true'])
 assert.ok(api.includes(marker), marker);
for(const marker of ['transportVehicleCaseDialog','transportVehicleCaseResolution',
 'transportVehicleCaseCreate','AntiForgeryToken']) assert.ok(view.includes(marker),marker);
assert.ok(index.includes('_VehicleCases'));
assert.ok(index.includes('vehicle-cases.js'));
for(const marker of ['RequestVerificationToken','credentials:\'same-origin\'',
 'cache:\'no-store\'','showModal()','textContent','crypto?.randomUUID'])
 assert.ok(script.includes(marker),marker);
assert.ok(!script.includes('innerHTML'));
assert.ok(!script.includes('window.prompt'));
for(const marker of ['TenantId, x.ClientRequestId','TenantId, x.VehicleId','TenantId, x.CampusId',
 'HasCheckConstraint']) assert.ok(config.includes(marker),marker);
new Function(script);
console.log('Transport vehicle case API, UI, migration and JS contracts: PASS');
