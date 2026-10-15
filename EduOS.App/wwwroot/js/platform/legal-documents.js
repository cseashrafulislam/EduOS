(() => {
 "use strict";
 const root=document.querySelector("[data-legal-mode]");if(!root)return;
 const $=id=>document.getElementById(id), admin=root.dataset.legalMode==="admin";
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 function notice(message,error=false){const x=$("legalAlert");x.textContent=message;x.className="alert "+(error?"alert-danger":"alert-success");}
 function el(tag,text,cls){const x=document.createElement(tag);x.textContent=String(text??"");if(cls)x.className=cls;return x;}
 async function api(path,method="GET",body){
  const headers={Accept:"application/json"};
  if(method!=="GET"){if(!token)throw Error("Missing antiforgery token.");headers.RequestVerificationToken=token;headers["Content-Type"]="application/json";}
  const r=await fetch("/api/legal-documents"+path,{method,headers,credentials:"same-origin",cache:"no-store",body:body===undefined?undefined:JSON.stringify(body)});
  const data=await r.json().catch(()=>null);if(!r.ok||!data?.success)throw Error(data?.message||"Request failed.");return data.data;
 }
 function card(doc){
  const box=el("article","","border rounded-3 p-3"),detail=document.createElement("details");
  box.append(el("h3",doc.title+" ("+doc.version+")","h6"));
  box.append(el("p",doc.code+" | Effective: "+new Date(doc.effectiveAt).toLocaleString(),"small text-muted"));
  detail.append(el("summary","Read full text"));
  detail.append(el("pre",doc.content,"p-3 mt-2 bg-light text-wrap"));
  box.append(detail);return box;
 }
 async function loadAdmin(){
  const box=$("legalAdminRows");box.replaceChildren();
  const rows=await api("/admin");if(!rows.length)box.append(el("p","No documents published."));
  for(const row of rows)box.append(card(row));
 }
 async function loadUser(){
  const [docs,history]=await Promise.all([api(""),api("/mine")]);
  const accepted=new Set(history.map(x=>x.legalDocumentId)),box=$("legalCurrentRows");
  box.replaceChildren();if(!docs.length)box.append(el("p","No current documents."));
  for(const doc of docs){
   const item=card(doc);
   if(accepted.has(doc.id))item.append(el("span","Accepted","badge bg-success"));
   else{
    const button=el("button","Accept this version","btn btn-primary btn-sm mt-2");button.type="button";
    button.addEventListener("click",async()=>{
     if(!confirm("Confirm acceptance of "+doc.title+"?"))return;
     button.disabled=true;
     try{await api("/"+encodeURIComponent(doc.id)+"/accept","POST",{clientRequestId:crypto.randomUUID(),legalDocumentId:doc.id,accepted:true});await loadUser();notice("Acceptance recorded.");}
     catch(e){notice(e.message,true);button.disabled=false;}
    });item.append(button);
   }box.append(item);
  }
  const list=$("legalHistoryRows");list.replaceChildren();
  if(!history.length)list.append(el("p","No acceptances recorded."));
  for(const x of history)list.append(el("div","Document #"+x.legalDocumentId+" | "+x.version+" | "+new Date(x.acceptedAt).toLocaleString(),"small border-bottom pb-2"));
 }
 if(admin){
  const time=$("legalEffective"),now=new Date();
  time.value=new Date(now.getTime()-now.getTimezoneOffset()*60000).toISOString().slice(0,16);
  $("legalPublishForm").addEventListener("submit",async e=>{
   e.preventDefault();const button=$("legalPublishButton");if(button.disabled)return;
   if(!confirm("Publish immutable legal version?"))return;button.disabled=true;
   try{
    await api("/admin","POST",{clientRequestId:crypto.randomUUID(),code:$("legalCode").value.trim().toUpperCase(),version:$("legalVersion").value.trim(),title:$("legalTitle").value.trim(),content:$("legalContent").value.trim(),effectiveAt:new Date(time.value).toISOString()});
    $("legalPublishForm").reset();await loadAdmin();notice("Version published.");
   }catch(error){notice(error.message,true);}finally{button.disabled=false;}
  });loadAdmin().catch(e=>notice(e.message,true));
 }else loadUser().catch(e=>notice(e.message,true));
})();