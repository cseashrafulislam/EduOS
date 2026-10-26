(() => {
'use strict';
const u=window.transportLink;if(!u)return;
let requestKey=null,requestSignature=null,busy=false;
const validDate=s=>{
 if(!/^\d{4}-\d{2}-\d{2}$/.test(s))return false;
 const parsed=new Date(s+'T00:00:00Z');
 return !Number.isNaN(parsed.getTime())&&parsed.toISOString().slice(0,10)===s;
};
u.shortenUpcomingLink=async item=>{
 if(busy)return;
 if(!item||item.isCancelled||item.effectiveFrom<=u.today()||!item.effectiveTo)return;
 const end=window.prompt('New last effective date (YYYY-MM-DD)',item.effectiveTo);
 if(end===null)return;
 if(!validDate(end)||end<item.effectiveFrom||end>=item.effectiveTo){
  u.notify('Choose a valid end date from the scheduled start through the day before the existing end.');return;
 }
 if(!window.confirm('Shorten this scheduled route/vehicle reservation? Riders requiring removed days will block the change.'))return;
 if(!window.crypto?.randomUUID)return u.notify('Secure browser context required.');
 const signature=[item.id,end,item.rowVersion].join('|');
 if(signature!==requestSignature){requestSignature=signature;requestKey=window.crypto.randomUUID();}
 busy=true;
 try{
  await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(item.id)+'/shorten-upcoming',
   'POST',{clientRequestId:requestKey,effectiveTo:end,rowVersion:item.rowVersion});
  requestKey=null;requestSignature=null;
  u.notify('Upcoming route/vehicle reservation shortened.',true);
  await u.load();
 }catch(error){u.notify(error.message);}finally{busy=false;}
};
})();
