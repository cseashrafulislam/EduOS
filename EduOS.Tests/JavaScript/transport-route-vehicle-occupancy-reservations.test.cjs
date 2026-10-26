'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const service=read('EduOS.Service/Services/Transport/TransportService.RouteVehicleQueries.cs');
const dto=read('EduOS.Core/DTOs/Transport/TransportRouteVehicleLinkDtos.cs');
const render=read('EduOS.App/wwwroot/js/transport-operations/route-vehicle-render.js');
const view=read('EduOS.App/Views/TransportOperations/_RouteVehicleLinks.cshtml');

test('route-vehicle occupancy uses effective dates without losing active reservations',()=>{
    assert.match(service,/x\.TenantId == tenant && !x\.IsDeleted && x\.State == TransportAssignmentState\.Active/);
    assert.match(service,/x\.RouteVehicleAssignmentId\.HasValue && ids\.Contains\(x\.RouteVehicleAssignmentId\.Value\)/);
    assert.match(service,/Reservations = g\.Count\(\)/);
    assert.match(service,/OccupiedToday = g\.Count\(a => a\.StartDate <= today &&/);
    assert.match(service,/!a\.EndDate\.HasValue \|\| a\.EndDate\.Value >= today/);
    assert.match(service,/ActiveRiders = riderCounts\.GetValueOrDefault\(x\.Id\)\?\.Reservations \?\? 0/);
    assert.match(service,/OccupiedToday = riderCounts\.GetValueOrDefault\(x\.Id\)\?\.OccupiedToday \?\? 0/);
});

test('route-vehicle list keeps tenant and campus authorization and paging',()=>{
    assert.match(service,/if \(!CanManage\(\)\)/);
    assert.match(service,/allowed = AllowedCampusIds\(tenant\)/);
    assert.match(service,/allowed\.Contains\(x\.CampusId\)/);
    assert.match(service,/pageSize is < 1 or > 100/);
    assert.match(service,/\.Skip\(\(page - 1\) \* pageSize\)\.Take\(pageSize\)/);
});

test('route-vehicle response and table expose both metrics',()=>{
    assert.match(dto,/public int ActiveRiders \{ get; set; \}/);
    assert.match(dto,/public int OccupiedToday \{ get; set; \}/);
    assert.match(view,/<th>Occupied today<\/th><th>Active reservations<\/th>/);
    assert.match(view,/colspan="10"/);
    assert.doesNotMatch(render,/innerHTML/);
});

test('route-vehicle row renders current riders separately from future reservations',()=>{
    const body={children:[],replaceChildren(){this.children=[];},append(child){this.children.push(child);}};
    const element=tag=>({tag,children:[],textContent:'',append(child){this.children.push(child);},addEventListener(){}});
    const u={el:()=>body,closeLink(){},today:()=>'2026-10-16'};
    vm.runInNewContext(render,{window:{transportLink:u},document:{createElement:element}});
    u.renderLinks([{campusName:'North',routeName:'R1',vehicleNumber:'BUS-1',capacity:40,
        occupiedToday:9,activeRiders:12,effectiveFrom:'2026-10-16',effectiveTo:null,isCurrent:true}]);
    assert.equal(body.children.length,1);
    const cells=body.children[0].children;
    assert.equal(cells.length,10);
    assert.deepEqual(cells.slice(0,6).map(c=>c.textContent),
        ['North','R1','BUS-1','40','9','12']);
    assert.equal(cells[9].children[0].textContent,'End');
});
