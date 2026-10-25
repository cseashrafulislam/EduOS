(() => {
'use strict';
const u=window.vehicleDriverUI;if(!u)return;
u.rescheduleUpcomingDriver=async item=>{
 if(!item||!item.isScheduled||item.isCancelled||!item.rowVersion||item.effectiveFrom<=u.today())return;
 const start=window.prompt('New first effective date (YYYY-MM-DD)',item.effectiveFrom);
 if(start===null)return;
 const parsed=Date.parse(start+'T00:00:00Z');
 if(!/^\d{4}-\d{2}-\d{2}$/.test(start)||Number.isNaN(parsed)||
    new Date(parsed).toISOString().slice(0,10)!==start||
    start<=u.today()||(item.effectiveTo&&start>item.effectiveTo)||
    !item.licenseExpiresOn||start>item.licenseExpiresOn){
  u.notify('Choose a valid future start within the assignment end and driver license expiry.');return;
 }
 try{
  await u.api(u.endpoint+'/'+encodeURIComponent(item.id)+'/reschedule-start','POST',
   {effectiveFrom:start,rowVersion:item.rowVersion});
  u.notify('Upcoming driver assignment rescheduled.',true);
  await u.loadLinks();
 }catch(error){u.notify(error.message);}
};
})();
