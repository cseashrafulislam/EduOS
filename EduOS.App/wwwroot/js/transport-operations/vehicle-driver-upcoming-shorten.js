(() => {
'use strict';
const u=window.vehicleDriverUI;if(!u)return;
u.shortenUpcomingDriver=async item=>{
 if(!item||!item.isScheduled||item.isCancelled||!item.rowVersion||
    !item.effectiveTo||item.effectiveFrom<=u.today())return;
 const end=window.prompt('New final effective date (YYYY-MM-DD)',item.effectiveFrom);
 if(end===null)return;
 const parsed=Date.parse(end+'T00:00:00Z');
 if(!/^\d{4}-\d{2}-\d{2}$/.test(end)||Number.isNaN(parsed)||
    new Date(parsed).toISOString().slice(0,10)!==end||
    end<item.effectiveFrom||end>=item.effectiveTo){
  u.notify('Choose a valid date from scheduled start through the day before current end.');return;
 }
 if(!window.confirm('Shorten this future driver reservation through '+end+'?'))return;
 try{
  await u.api(u.endpoint+'/'+encodeURIComponent(item.id)+'/shorten-upcoming','POST',
   {effectiveTo:end,rowVersion:item.rowVersion});
  u.notify('Upcoming driver reservation shortened.',true);
  await u.loadLinks();
 }catch(error){u.notify(error.message);}
};
})();
