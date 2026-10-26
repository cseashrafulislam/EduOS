(() => {
'use strict';
const u=window.transportLink;if(!u)return;
let requestKey=null,requestSignature=null,busy=false;
u.cancelUpcomingLink=async item=>{
 if(busy)return;
 if(!item||item.isCancelled)return;
 if(item.effectiveFrom<=u.today()){u.notify('Only future reservations may be cancelled.');return;}
 if(Number(item.activeRiders)>0){u.notify('Reassign or cancel reserved riders first.');return;}
 if(!window.confirm('Cancel this unused future route/vehicle assignment?'))return;
 if(!window.crypto?.randomUUID)return u.notify('Secure browser context required.');
 const signature=[item.id,item.rowVersion].join('|');
 if(signature!==requestSignature){requestSignature=signature;requestKey=window.crypto.randomUUID();}
 busy=true;
 try{
  await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(item.id)+'/cancel-upcoming','POST',
   {clientRequestId:requestKey,rowVersion:item.rowVersion});
  requestKey=null;requestSignature=null;
  u.notify('Upcoming route/vehicle assignment cancelled.',true);await u.load();
 }catch(error){u.notify(error.message);}finally{busy=false;}
};
})();
