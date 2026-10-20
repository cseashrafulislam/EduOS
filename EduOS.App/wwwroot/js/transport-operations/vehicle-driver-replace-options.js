(() => {
const u=window.vehicleDriverUI;if(!u)return;
const select=u.el('transportVehicleDriverReplaceSource');
u.replacementRows=[];
u.el('transportVehicleDriverReplaceFind').onclick=async()=>{
 const q=new URLSearchParams({page:'1',pageSize:'100'});
 const term=u.el('transportVehicleDriverReplaceSearch').value.trim();
 if(term)q.set('search',term);
 const data=await u.api(u.endpoint+'?'+q);
 u.replacementRows=(data.items||[]).filter(x=>x.isCurrent&&x.effectiveFrom<u.today());
 select.replaceChildren();u.option(select,'','Select active vehicle');
 u.replacementRows.forEach(x=>u.option(select,x.id,x.vehicleNumber+' · '+x.driverName));
 if(data.totalCount>100)u.notify('Refine search to see more assignments.');
};
})();
