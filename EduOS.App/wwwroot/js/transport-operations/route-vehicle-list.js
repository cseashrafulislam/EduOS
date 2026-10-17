(() => {
'use strict';
const u=window.transportLink;if(!u)return;
let page=1,pages=1,sequence=0,loadedFilter=null;
const exportButton=u.el('transportLinkExport');
const filters=()=>{
 const q=new URLSearchParams();
 const campus=u.el('transportLinkFilterCampus').value,search=u.el('transportLinkSearch').value.trim();
 if(campus)q.set('campusId',campus);
 if(search)q.set('search',search);
 return q.toString();
};
function invalidate(){loadedFilter=null;exportButton.disabled=true;}
u.load=async()=>{
 invalidate();
 const current=++sequence,snapshot=filters();
 const q=new URLSearchParams(snapshot);q.set('page',String(page));q.set('pageSize','15');
 const data=await u.api('/api/transport/route-vehicle-links?'+q);
 if(current!==sequence||snapshot!==filters())return;
 u.renderLinks(data.items||[]);
 pages=Math.max(1,Number(data.totalPages)||1);
 u.el('transportLinkPageInfo').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' records';
 u.el('transportLinkPrev').disabled=page<=1;u.el('transportLinkNext').disabled=page>=pages;
 loadedFilter=snapshot;exportButton.disabled=!(data.items||[]).length;
};
u.el('transportLinkSearchForm').addEventListener('submit',e=>{e.preventDefault();page=1;u.load().catch(x=>u.notify(x.message));});
u.el('transportLinkPrev').addEventListener('click',()=>{page=Math.max(1,page-1);u.load().catch(x=>u.notify(x.message));});
u.el('transportLinkNext').addEventListener('click',()=>{page=Math.min(pages,page+1);u.load().catch(x=>u.notify(x.message));});
u.el('transportLinkSearch').addEventListener('input',invalidate);
u.el('transportLinkFilterCampus').addEventListener('change',invalidate);
exportButton.addEventListener('click',async()=>{
 const snapshot=filters(),request=sequence;
 if(loadedFilter===null||loadedFilter!==snapshot)return;
 exportButton.disabled=true;
 try{
  const response=await fetch('/api/transport/route-vehicle-links/export?'+snapshot,{
   credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
  if(!response.ok){
   const error=await response.json().catch(()=>null);
   throw Error(error?.message||'Route/vehicle history export failed.');
  }
  const blob=await response.blob();
  if(request!==sequence||loadedFilter!==snapshot||filters()!==snapshot)
   throw Error('Filters changed during export. Apply filters and retry.');
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='route-vehicle-history.csv';document.body.append(link);link.click();link.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){u.notify(error.message);}
 finally{exportButton.disabled=loadedFilter===null||loadedFilter!==filters();}
});
u.load().catch(e=>u.notify(e.message));
})();
