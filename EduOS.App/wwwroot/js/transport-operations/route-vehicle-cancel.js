(() => {
'use strict';
const u=window.transportLink;if(!u)return;
u.cancelUpcomingLink=async item=>{
 if(!item||item.isCancelled)return;
 if(item.effectiveFrom<=u.today()){u.notify('Only future reservations may be cancelled.');return;}
 if(Number(item.activeRiders)>0){u.notify('Reassign or cancel reserved riders first.');return;}
 if(!window.confirm('Cancel this unused future route/vehicle assignment?'))return;
 try{
  await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(item.id)+'/cancel-upcoming','POST',{rowVersion:item.rowVersion});
  u.notify('Upcoming route/vehicle assignment cancelled.',true);await u.load();
 }catch(error){u.notify(error.message);}
};
})();
