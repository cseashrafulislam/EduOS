(() => {
'use strict';
const u=window.vehicleDriverUI;if(!u)return;
u.extendUpcomingDriver=async item=>{
 if(!item||!item.isScheduled||item.isCancelled||!item.rowVersion||
    !item.effectiveTo||!item.licenseExpiresOn||item.effectiveFrom<=u.today()||
    item.licenseExpiresOn<=item.effectiveTo)return;
 const end=window.prompt('New final effective date (YYYY-MM-DD)',item.licenseExpiresOn);
 if(end===null)return;
 const parsed=Date.parse(end+'T00:00:00Z');
 if(!/^\d{4}-\d{2}-\d{2}$/.test(end)||Number.isNaN(parsed)||
    new Date(parsed).toISOString().slice(0,10)!==end||
    end<=item.effectiveTo||end>item.licenseExpiresOn){
  u.notify('Choose a valid date after the current end and no later than license expiry.');return;
 }
 if(!window.confirm('Extend this future driver reservation through '+end+'?'))return;
 try{
  await u.api(u.endpoint+'/'+encodeURIComponent(item.id)+'/extend-upcoming','POST',
   {effectiveTo:end,rowVersion:item.rowVersion});
  u.notify('Upcoming driver reservation extended.',true);
  await u.loadLinks();
 }catch(error){u.notify(error.message);}
};
})();
