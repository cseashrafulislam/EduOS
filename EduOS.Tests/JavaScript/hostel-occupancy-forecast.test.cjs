const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname,'../..');
const read = file => fs.readFileSync(path.join(root,file),'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.OccupancyForecast.cs');
const api = read('EduOS.App/Controllers/Api/HostelOccupancyForecastController.cs');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/hostel-operations/occupancy-forecast.js');

test('future forecast reads canonical tenant/campus-scoped rooms and allocations',()=>{
    assert.match(service,/room\.TenantId == tenant && hostel\.TenantId == tenant/);
    assert.match(service,/AllowedCampusIds\(tenant\)/);
    assert.match(service,/allocation\.State != HostelAllocationState\.Cancelled/);
    assert.match(service,/allocation\.StartDate <= operatingDate/);
    assert.match(service,/allocation\.EndDate\.Value >= operatingDate/);
    assert.match(service,/Take\(1001\)/);
    assert.match(service,/Take\(10001\)/);
    assert.doesNotMatch(service,/StudentName|MonthlyRent|SecurityDeposit|PaymentAmount/);
});
test('authorized API, Razor panel and JS route are wired',()=>{
    assert.match(api,/Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)/);
    assert.match(api,/RequireModule\("HOSTEL"\)/);
    assert.match(api,/HttpGet\("export"\)/);
    assert.match(view,/PartialAsync\("_OccupancyForecast"\)/);
    assert.match(view,/hostel-operations\/occupancy-forecast\.js/);
    assert.match(js,/\/api\/hostel\/occupancy-forecast/);
    assert.match(js,/operatingDate:date\.value/);
    assert.match(js,/attentionOnly:String\(attention\.checked\)/);
    assert.match(js,/request !== sequence/);
    assert.match(js,/textContent/);
    assert.doesNotMatch(js,/innerHTML/);
    new Function(js);
});
test('CSV excludes identities and includes date and formula protection',()=>{
    assert.match(service,/CsvCell\(row\.HostelName\)/);
    assert.match(service,/CsvCell\(row\.RoomNumber\)/);
    assert.match(service,/Operating Date,Campus,Hostel,Room/);
    assert.match(service,/Forecast changed during export/);
});
