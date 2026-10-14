(() => {
const u=window.transportLink;if(!u)return;
u.catalogRoutes=[];u.catalogVehicles=[];
u.loadCatalog=async()=>{
 const routes=await u.api('/api/transport/routes');
 const vehicles=await u.api('/api/transport/vehicles');
 u.catalogRoutes=routes||[];u.catalogVehicles=vehicles||[];
};
u.loadCatalog().catch(e=>u.notify(e.message));
})();