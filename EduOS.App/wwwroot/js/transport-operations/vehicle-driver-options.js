(() => {
const u=window.vehicleDriverUI;if(!u)return;
u.loadVehicles=async()=>{
 const all=await u.api('/api/transport/vehicles');
 const term=u.el('transportVehicleDriverVehicleSearch').value.trim().toLowerCase();
 u.vehicles=(all||[]).filter(x=>x.isActive&&x.vehicleNumber.toLowerCase().includes(term));
 const select=u.el('transportVehicleDriverVehicle');select.replaceChildren();u.option(select,'','Select vehicle');
 u.vehicles.forEach(x=>u.option(select,x.id,x.vehicleNumber+' · '+x.capacity+' seats'));
};
u.loadDrivers=async()=>{
 const search=u.el('transportVehicleDriverDriverSearch').value.trim();
 const q=new URLSearchParams({page:'1',pageSize:'100'});if(search)q.set('search',search);
 const data=await u.api('/api/transport/driver-registry?'+q);
 u.drivers=(data.items||[]).filter(x=>x.isActive&&x.licenseExpiresOn>=u.today());
 const select=u.el('transportVehicleDriverDriver');select.replaceChildren();u.option(select,'','Select licensed driver');
 u.drivers.forEach(x=>u.option(select,x.id,x.fullName+' · expires '+x.licenseExpiresOn));
 if(data.totalCount>100)u.notify('Showing 100 drivers. Refine the search for others.');
};
u.loadCampuses=async()=>{
 const rows=await u.api('/api/transport/route-setup/campuses');
 const select=u.el('transportVehicleDriverCampus');
 (rows||[]).forEach(x=>u.option(select,x.id,x.name));
};
u.el('transportVehicleDriverFindVehicles').onclick=()=>u.loadVehicles().catch(x=>u.notify(x.message));
u.el('transportVehicleDriverFindDrivers').onclick=()=>u.loadDrivers().catch(x=>u.notify(x.message));
})();
