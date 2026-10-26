const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname,'../..');
const read = file => fs.readFileSync(path.join(root,file),'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.PeakOccupancyForecast.cs');
const api = read('EduOS.App/Controllers/Api/HostelPeakOccupancyForecastController.cs');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/hostel-operations/peak-occupancy-forecast.js');

test('peak report uses canonical tenant/campus-scoped reservations and bounded reads',()=>{
    assert.match(service,/room\.TenantId == tenant && hostel\.TenantId == tenant/);
    assert.match(service,/AllowedCampusIds\(tenant\)/);
    assert.match(service,/allocation\.State != HostelAllocationState\.Cancelled/);
    assert.match(service,/allocation\.StartDate <= toDate/);
    assert.match(service,/allocation\.EndDate\.Value >= fromDate/);
    assert.match(service,/Take\(1001\)/);
    assert.match(service,/Take\(10001\)/);
    assert.match(service,/ProjectOccupancyForecastRoom\(/);
    assert.doesNotMatch(service,/StudentName|MonthlyRent|SecurityDeposit|PaymentAmount/);
});
test('authorized API, Razor panel, and filters are wired',()=>{
    assert.match(api,/Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)/);
    assert.match(api,/RequireModule\("HOSTEL"\)/);
    assert.match(api,/HttpGet\("export"\)/);
    assert.match(view,/PartialAsync\("_PeakOccupancyForecast"\)/);
    assert.match(view,/hostel-operations\/peak-occupancy-forecast\.js/);
    assert.match(js,/\/api\/hostel\/peak-occupancy-forecast/);
    assert.match(js,/fromDate:from\.value,toDate:to\.value/);
    assert.match(js,/request !== sequence/);
    assert.match(js,/textContent/);
    assert.doesNotMatch(js,/innerHTML/);
    new Function(js);
});
test('CSV includes peak date, interval and formula-protected text',()=>{
    assert.match(service,/Maximum Duplicate Bookings,Maximum Inactive Reserved Beds,Attention Days/);
    assert.match(service,/CsvCell\(row\.CampusName\)/);
    assert.match(service,/CsvCell\(row\.RoomNumber\)/);
    assert.match(service,/PeakDate\.ToString\("yyyy-MM-dd"/);
});
