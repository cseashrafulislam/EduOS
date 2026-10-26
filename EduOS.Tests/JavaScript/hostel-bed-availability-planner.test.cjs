const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const read=p=>fs.readFileSync(path.join(__dirname,'../..',p),'utf8');
const service=read('EduOS.Service/Services/Hostel/HostelService.BedAvailability.cs');
const dto=read('EduOS.Core/DTOs/Hostel/HostelBedAvailabilityRowDto.cs');
const contract=read('EduOS.Core/Interfaces/IServices/IHostelService.cs');
const api=read('EduOS.App/Controllers/Api/HostelController.cs');
const view=read('EduOS.App/Views/HostelOperations/_AllocationReadiness.cshtml');
const partial=read('EduOS.App/Views/HostelOperations/_BedAvailability.cshtml');
const js=read('EduOS.App/wwwroot/js/hostel-operations/bed-availability.js');
test('canonical, tenant/campus-authorized future interval planning with room capacity',()=>{
    for(const fragment of ['CanManage()','AllowedCampusIds(tenant)','roomIds.Contains(bed.HostelRoomId)','HostelAllocationState.Cancelled','allocation.StartDate <= toDate','allocation.EndDate.Value >= fromDate','capacity - peakOccupied','PeakOccupiedBeds(','candidateCount > 1000'])
        assert.ok(service.includes(fragment),fragment);
    assert.match(service,/var blockedBeds = bookings\?\.Select\(x => x\.BedId\)\.ToHashSet\(\)/);
    assert.match(service,/\.Select\(x => x\.BedId\)\.Distinct\(\)\.Count\(\)/);
    assert.match(service,/pageSize is < 1 or > 100/);
    assert.match(service,/toDate.DayNumber - fromDate.DayNumber > 89/);
    assert.match(service,/!allocation\.IsDeleted && roomIds\.Contains\(bed\.HostelRoomId\)/);
    assert.doesNotMatch(dto,/StudentName|StudentCode|MonthlyRent|SecurityDeposit/);
});
test('authorized API, MVC view, JS, and bounded CSV share filters',()=>{
    assert.match(contract,/GetBedAvailabilityAsync/);
    assert.match(contract,/ExportBedAvailabilityCsvAsync/);
    assert.match(api,/HttpGet\("bed-availability"\)/);
    assert.match(api,/HttpGet\("bed-availability\/export"\)/);
    assert.match(api,/Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)/);
    assert.match(view,/PartialAsync\("_BedAvailability"\)/);
    assert.match(view,/bed-availability.js/);
    assert.match(partial,/id="hostelAvailabilityForm"/);
    assert.match(js,/requestSerial|n!==serial/);
    assert.match(js,/params\(false\)/);
    assert.doesNotMatch(js,/innerHTML/);
    assert.match(service,/rows.Count != first.Data.TotalCount/);
    assert.match(service,/!seen.Add\(x.BedId\)/);
    assert.match(service,/CsvCell\(row.HostelName\)/);
    new Function(js);
});
