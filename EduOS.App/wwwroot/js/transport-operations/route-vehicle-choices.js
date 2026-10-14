(() => {
const u=window.transportLink;if(!u)return;
u.loadChoices=async kind=>{
 const campus=u.el('transportLinkCampus').value;
 const select=u.el(kind==='route'?'transportLinkRoute':'transportLinkVehicle');
 select.replaceChildren();u.option(select,'','Select '+kind);
 if(!campus)return;
 await u.loadCatalog();
 const search=u.el(kind==='route'?'transportLinkRouteSearch':'transportLinkVehicleSearch').value.trim();
 const q=new URLSearchParams({campusId:campus,page:'1',pageSize:'100'});
 if(search)q.set('search',search);
 const data=await u.api('/api/transport/'+(kind==='route'?'route-setup':'vehicle-setup')+'?'+q);
 const catalog=kind==='route'?u.catalogRoutes:u.catalogVehicles;
 const rows=(data.items||[]).filter(x=>x.isActive).map(x=>({...x,id:catalog.find(y=>y.reference===x.reference)?.id})).filter(x=>x.id);
 if(kind==='route')u.routes=rows;else u.vehicles=rows;
 rows.forEach(x=>u.option(select,x.reference,kind==='route'?x.code+' — '+x.name:x.vehicleNumber+' ('+x.capacity+' seats)'));
 if(data.totalCount>100)u.notify('Refine search: more than 100 '+kind+' matches.');
};
u.el('transportLinkCampus').addEventListener('change',()=>Promise.all([u.loadChoices('route'),u.loadChoices('vehicle')]).catch(e=>u.notify(e.message)));
u.el('transportLinkFindRoutes').addEventListener('click',()=>u.loadChoices('route').catch(e=>u.notify(e.message)));
u.el('transportLinkFindVehicles').addEventListener('click',()=>u.loadChoices('vehicle').catch(e=>u.notify(e.message)));
})();