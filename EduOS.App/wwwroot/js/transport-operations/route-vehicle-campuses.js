(() => {
const u=window.transportLink;if(!u)return;
u.loadCampuses=async()=>{
 const data=await u.api('/api/transport/route-setup/campuses');
 const create=u.el('transportLinkCampus'),filter=u.el('transportLinkFilterCampus');
 create.replaceChildren();filter.replaceChildren();
 u.option(create,'','Select campus');u.option(filter,'','All authorized campuses');
 (data||[]).forEach(c=>{u.option(create,c.id,c.name);u.option(filter,c.id,c.name);});
};
u.loadCampuses().catch(e=>u.notify(e.message));
})();