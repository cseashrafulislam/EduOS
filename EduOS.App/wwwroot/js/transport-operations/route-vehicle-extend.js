(() => {
'use strict';
const u=window.transportLink;if(!u)return;
u.extendLink=async item=>{
 if(!item||item.isCancelled||!item.isCurrent||!item.effectiveTo)return;
 const end=window.prompt('New last effective date (YYYY-MM-DD)',item.effectiveTo);
 if(!end)return;
 if(!/^\d{4}-\d{2}-\d{2}$/.test(end)||end<=item.effectiveTo){
  u.notify('Extension date must be later than the current end date.');return;
 }
 try{
  await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(item.id)+'/extend','POST',
   {effectiveTo:end,rowVersion:item.rowVersion});
  u.notify('Route/vehicle assignment extended.',true);
  await u.load();
 }catch(error){u.notify(error.message);}
};
})();
