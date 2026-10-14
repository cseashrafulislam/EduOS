(() => {
const u=window.transportLink;if(!u)return;
const date=()=>new Date().toLocaleDateString('en-CA');
u.today=date;u.el('transportLinkFrom').value=date();
u.form.addEventListener('submit',async e=>{
 e.preventDefault();if(!u.form.reportValidity())return;
 const route=u.routes.find(x=>x.reference===u.el('transportLinkRoute').value);
 const vehicle=u.vehicles.find(x=>x.reference===u.el('transportLinkVehicle').value);
 const from=u.el('transportLinkFrom').value,to=u.el('transportLinkTo').value;
 if(!route||!vehicle||from<date()||(to&&to<from)){u.notify('Invalid route, vehicle or dates.');return;}
 const button=u.el('transportLinkCreate');button.disabled=true;
 try{
  await u.api('/api/transport/route-vehicle-links','POST',{clientRequestId:u.requestId,routeId:route.id,vehicleId:vehicle.id,routeRowVersion:route.rowVersion,vehicleRowVersion:vehicle.rowVersion,effectiveFrom:from,effectiveTo:to||null});
  u.requestId=crypto.randomUUID();u.notify('Assignment saved.',true);u.form.reset();u.el('transportLinkFrom').value=date();await u.load();
 }catch(err){u.notify(err.message);}finally{button.disabled=false;}
});
})();