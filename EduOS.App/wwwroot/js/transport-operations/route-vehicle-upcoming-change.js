(() => {
'use strict';
const u=window.transportLink;if(!u)return;
const panel=u.el('transportUpcomingLinkVehiclePanel');
const search=u.el('transportUpcomingLinkVehicleSearch');
const select=u.el('transportUpcomingLinkVehicleSelect');
const prev=u.el('transportUpcomingLinkVehiclePrev');
const next=u.el('transportUpcomingLinkVehicleNext');
const save=u.el('transportUpcomingLinkVehicleSave');
let source=null,page=1,pages=1,choices=[],sequence=0,saving=false,pendingKey=null,pendingSignature=null;
function close(){source=null;choices=[];sequence++;panel.hidden=true;select.replaceChildren();pendingKey=null;pendingSignature=null;}
async function loadChoices(){
 if(!source)return;
 const current=++sequence,id=source.id,term=search.value.trim();
 const q=new URLSearchParams({page:String(page),pageSize:'20'});
 if(term)q.set('search',term);
 const data=await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(id)+'/replacement-vehicles?'+q);
 if(current!==sequence||!source||source.id!==id||search.value.trim()!==term)return;
 choices=data.items||[];
 pages=Math.max(1,Number(data.totalPages)||1);
 select.replaceChildren();u.option(select,'','Select available vehicle');
 for(const x of choices)u.option(select,x.vehicleId,x.vehicleNumber+' ('+x.capacity+' seats)');
 prev.disabled=page<=1;next.disabled=page>=pages;
 u.el('transportUpcomingLinkVehiclePageInfo').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' eligible vehicles';
 save.disabled=!choices.length;
}
u.changeUpcomingRouteVehicle=async item=>{
 if(!item||item.isCancelled||item.effectiveFrom<=u.today())return;
 source=item;page=1;search.value='';panel.hidden=false;pendingKey=null;pendingSignature=null;
 u.el('transportUpcomingLinkVehicleContext').textContent=
  item.routeName+' · '+item.vehicleNumber+' · '+item.effectiveFrom+' to '+(item.effectiveTo||'open');
 try{await loadChoices();}catch(e){u.notify(e.message);}
};
u.el('transportUpcomingLinkVehicleSearchForm').addEventListener('submit',e=>{
 e.preventDefault();page=1;loadChoices().catch(x=>u.notify(x.message));
});
prev.addEventListener('click',()=>{if(page>1){page--;loadChoices().catch(x=>u.notify(x.message));}});
next.addEventListener('click',()=>{if(page<pages){page++;loadChoices().catch(x=>u.notify(x.message));}});
u.el('transportUpcomingLinkVehicleCancel').addEventListener('click',close);
save.addEventListener('click',async()=>{
 if(!source||saving)return;
 const chosen=choices.find(x=>String(x.vehicleId)===select.value);
 if(!chosen){u.notify('Select an eligible vehicle.');return;}
 if(!window.crypto||!window.crypto.randomUUID){u.notify('A secure browser context is required to submit this change.');return;}
 const signature=[source.id,chosen.vehicleId,source.rowVersion,chosen.rowVersion].join('|');
 if(signature!==pendingSignature){pendingSignature=signature;pendingKey=window.crypto.randomUUID();}
 saving=true;save.disabled=true;
 try{
  await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(source.id)+'/change-upcoming-vehicle',
   'POST',{clientRequestId:pendingKey,newVehicleId:chosen.vehicleId,assignmentRowVersion:source.rowVersion,
    vehicleRowVersion:chosen.rowVersion});
  u.notify('Scheduled route vehicle changed.',true);close();await u.load();
 }catch(e){u.notify(e.message);}finally{saving=false;save.disabled=!source||!choices.length;}
});
})();
