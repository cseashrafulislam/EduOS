(() => {
const u=window.transportLink;if(!u)return;
let page=1,pages=1;
u.load=async()=>{
 const q=new URLSearchParams({page:String(page),pageSize:'15'});
 const campus=u.el('transportLinkFilterCampus').value,search=u.el('transportLinkSearch').value.trim();
 if(campus)q.set('campusId',campus);if(search)q.set('search',search);
 const data=await u.api('/api/transport/route-vehicle-links?'+q);
 u.renderLinks(data.items||[]);
 pages=Math.max(1,Number(data.totalPages)||1);
 u.el('transportLinkPageInfo').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' records';
 u.el('transportLinkPrev').disabled=page<=1;u.el('transportLinkNext').disabled=page>=pages;
};
u.el('transportLinkSearchForm').addEventListener('submit',e=>{e.preventDefault();page=1;u.load().catch(x=>u.notify(x.message));});
u.el('transportLinkPrev').addEventListener('click',()=>{page=Math.max(1,page-1);u.load().catch(x=>u.notify(x.message));});
u.el('transportLinkNext').addEventListener('click',()=>{page=Math.min(pages,page+1);u.load().catch(x=>u.notify(x.message));});
u.load().catch(e=>u.notify(e.message));
})();