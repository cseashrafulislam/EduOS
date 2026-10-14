(() => {
 "use strict";
 const api=window.documentVerification, $=api.$;
 let page=1,total=0,requestVersion=0;
 async function load(){
  const current=++requestVersion,status=$("verificationStatus").value;
  const q=new URLSearchParams({page,pageSize:20});if(status)q.set("status",status);
  const data=await api.request("?"+q);if(current!==requestVersion)return;total=data.totalCount||0;
  const rows=$("verificationRows");rows.replaceChildren();
  for(const d of data.items||[]){
   const tr=document.createElement("tr");
   for(const v of [d.title||d.reference,d.documentTypeCode,d.verificationStatusCode,d.expiresAt||"—",d.createdBy??"Unknown",d.verifiedAt||"—"]){
    const td=document.createElement("td");td.textContent=String(v);tr.append(td);
   }
   const td=document.createElement("td");
   const button=(label,fn)=>{const x=document.createElement("button");x.type="button";x.textContent=label;x.className="btn btn-outline-primary btn-sm me-1";x.addEventListener("click",async()=>{x.disabled=true;try{await fn();}catch(e){api.msg(e.message,true);}finally{x.disabled=false;}});return x;};
   if(d.verificationStatusCode==="Pending"){
    td.append(button("Preview",()=>{const form=$("verificationPreviewForm");form.action="/api/document-verification/"+encodeURIComponent(d.reference)+"/preview";form.submit();}));
    td.append(button("Approve",async()=>{if(!confirm("Approve this document?"))return;await api.request("/"+d.reference+"/review","POST",{rowVersion:d.rowVersion,approve:true});await load();api.msg("Approved.");}));
    td.append(button("Reject",async()=>{const note=prompt("Rejection reason:");if(note===null)return;if(!note.trim()||note.length>500)throw Error("Valid reason required.");await api.request("/"+d.reference+"/review","POST",{rowVersion:d.rowVersion,approve:false,note:note.trim()});await load();api.msg("Rejected.");}));
   }
   if(d.verificationStatusCode==="Approved"&&d.isActive){
    const link=document.createElement("a");link.href="/api/document-verification/"+encodeURIComponent(d.reference)+"/download";link.textContent="Download";link.className="btn btn-outline-primary btn-sm";td.append(link);
   }
   tr.append(td);rows.append(tr);
  }
  const pages=Math.max(1,Math.ceil(total/20));$("verificationPageInfo").textContent="Page "+page+" of "+pages;
  $("verificationPrev").disabled=page<=1;$("verificationNext").disabled=page>=pages;
 }
 $("verificationFilter").addEventListener("submit",e=>{e.preventDefault();page=1;load().catch(e=>api.msg(e.message,true));});
 $("verificationPrev").addEventListener("click",()=>{page--;load().catch(e=>api.msg(e.message,true));});
 $("verificationNext").addEventListener("click",()=>{page++;load().catch(e=>api.msg(e.message,true));});
 load().catch(e=>api.msg(e.message,true));
})();