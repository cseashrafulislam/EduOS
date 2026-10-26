(() => {
const u=window.vehicleDriverUI;if(!u)return;
let previousSignature=null;
u.el('transportVehicleDriverFrom').value=u.today();
u.el('transportVehicleDriverFrom').min=u.today();
u.form.addEventListener('submit',async event=>{
 event.preventDefault();if(!u.form.reportValidity())return;
 const v=u.vehicles.find(x=>String(x.id)===u.el('transportVehicleDriverVehicle').value);
 const d=u.drivers.find(x=>String(x.id)===u.el('transportVehicleDriverDriver').value);
 const from=u.el('transportVehicleDriverFrom').value,to=u.el('transportVehicleDriverTo').value;
 if(!v||!d||from<u.today()||(to&&to<from)||(to||from)>d.licenseExpiresOn)
  return u.notify('Invalid vehicle, driver or dates.');
 const signature=[v.id,d.id,from,to,v.rowVersion,d.rowVersion].join('|');
 if(signature!==previousSignature){previousSignature=signature;u.requestId=crypto.randomUUID();}
 const b=u.el('transportVehicleDriverAssign');b.disabled=true;
 try{
  await u.api(u.endpoint,'POST',{clientRequestId:u.requestId,vehicleId:v.id,transportDriverId:d.id,
   vehicleRowVersion:v.rowVersion,driverRowVersion:d.rowVersion,effectiveFrom:from,effectiveTo:to||null});
  u.requestId=crypto.randomUUID();previousSignature=null;u.notify('Assignment saved.',true);await u.loadLinks();
 }catch(error){u.notify(error.message);}finally{b.disabled=false;}
});
})();
