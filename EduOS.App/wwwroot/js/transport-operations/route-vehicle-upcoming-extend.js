(() => {
'use strict';
const u=window.transportLink;if(!u)return;
let requestKey=null,requestSignature=null,busy=false;
u.extendUpcomingLink=async item=>{
 if(busy)return;
 if(!item||item.isCancelled||item.effectiveFrom<=u.today()||!item.effectiveTo)return;
 const end=window.prompt('New last effective date (YYYY-MM-DD)',item.effectiveTo);
 if(end===null)return;
 if(!/^\d{4}-\d{2}-\d{2}$/.test(end)||Number.isNaN(Date.parse(end+'T00:00:00Z'))||
    end<=item.effectiveTo){
  u.notify('Choose a valid end date later than the existing end.');return;
 }
 if(!window.confirm('Extend this scheduled route/vehicle reservation?'))return;
 if(!window.crypto?.randomUUID)return u.notify('Secure browser context required.');
 const signature=[item.id,end,item.rowVersion].join('|');
 if(signature!==requestSignature){requestSignature=signature;requestKey=window.crypto.randomUUID();}
 busy=true;
 try{
  await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(item.id)+'/extend-upcoming',
   'POST',{clientRequestId:requestKey,effectiveTo:end,rowVersion:item.rowVersion});
  requestKey=null;requestSignature=null;
  u.notify('Upcoming route/vehicle reservation extended.',true);
  await u.load();
 }catch(error){u.notify(error.message);}finally{busy=false;}
};
})();
