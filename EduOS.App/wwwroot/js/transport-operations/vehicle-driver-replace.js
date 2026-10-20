(() => {
'use strict';
const u=window.vehicleDriverUI;if(!u)return;
u.replaceDriver=item=>{
 const d=u.drivers.find(x=>String(x.id)===u.el('transportVehicleDriverDriver').value);
 if(!d||d.id===item.transportDriverId)return u.notify('Choose a different licensed driver in the assignment form above.');
 if(!confirm('Replace driver today? Existing riders remain assigned.'))return;
 return u.api(u.endpoint+'/'+item.id+'/replace-active','POST',{
  clientRequestId:u.requestId,newDriverId:d.id,
  assignmentRowVersion:item.rowVersion,driverRowVersion:d.rowVersion
 }).then(()=>{u.notify('Driver replaced; riders unchanged.',true);return u.loadLinks();})
 .catch(error=>u.notify(error.message));
};
})();
