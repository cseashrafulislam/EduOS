(() => {
'use strict';
const u=window.vehicleDriverUI;if(!u)return;
let requestKey=null,requestSignature=null,busy=false;
u.replaceDriver=async item=>{
 if(busy)return;
 const d=u.drivers.find(x=>String(x.id)===u.el('transportVehicleDriverDriver').value);
 if(!d||d.id===item.transportDriverId)return u.notify('Choose a different licensed driver in the assignment form above.');
 if(!confirm('Replace driver today? Existing riders remain assigned.'))return;
 if(!window.crypto?.randomUUID)return u.notify('Secure browser context required.');
 const signature=[item.id,d.id,item.rowVersion,d.rowVersion].join('|');
 if(signature!==requestSignature){requestSignature=signature;requestKey=window.crypto.randomUUID();}
 busy=true;
 try{
  await u.api(u.endpoint+'/'+encodeURIComponent(item.id)+'/replace-active','POST',{
   clientRequestId:requestKey,newDriverId:d.id,
   assignmentRowVersion:item.rowVersion,driverRowVersion:d.rowVersion
  });
  requestKey=null;requestSignature=null;
  u.notify('Driver replaced; riders unchanged.',true);await u.loadLinks();
 }catch(error){u.notify(error.message);}finally{busy=false;}
};
})();
