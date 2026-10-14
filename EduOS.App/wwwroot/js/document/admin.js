(() => {
  "use strict";
  const el = id => document.getElementById(id), root = "/api/document-admin";
  const token = document.querySelector('meta[name="request-verification-token"]')?.content;
  const state = {types:[], templates:[], documents:[], page:1, total:0, pendingPage:1, pendingTotal:0, pending:[]};
  const val = id => el(id).value.trim(), set = (id,v) => el(id).value = v ?? "";
  const msg = (s,bad=false) => { const n=el("docAlert"); n.textContent=s; n.className="alert "+(bad?"alert-danger":"alert-success"); };
  async function api(path, method="GET", payload, multipart=false) {
    const headers = {Accept:"application/json"};
    if (method!=="GET") { if(!token) throw Error("Missing security token; reload."); headers.RequestVerificationToken=token; if(!multipart) headers["Content-Type"]="application/json"; }
    const response=await fetch(root+path,{method,headers,credentials:"same-origin",cache:"no-store",body:payload===undefined?undefined:multipart?payload:JSON.stringify(payload)});
    const result=await response.json().catch(()=>null);
    if(!response.ok || !result?.success) throw Error(result?.message || "Request failed ("+response.status+")");
    return result.data;
  }
  async function run(action) { try { await action(); } catch(e) { msg(e.message || "Request failed",true); } }
  function button(label, action, disabled=false) {
    const b=document.createElement("button"); b.type="button"; b.className="btn btn-outline-primary btn-sm me-1"; b.textContent=label; b.disabled=disabled;
    b.addEventListener("click",()=>run(action)); return b;
  }
  function row(target, cells, actions=[]) {
    const tr=document.createElement("tr"); cells.forEach(v=>{const td=document.createElement("td");td.textContent=String(v??"");tr.append(td);});
    const td=document.createElement("td");actions.forEach(b=>td.append(b));tr.append(td);target.append(tr);
  }
  const date = s => s ? new Date(s).toLocaleString() : "—";
  function localDate(s) { if(!s) return "";const d=new Date(s);return new Date(d.getTime()-d.getTimezoneOffset()*60000).toISOString().slice(0,16); }
  function resetDoc(){el("docForm").reset();set("docReference","");set("docVersion","");}
  function resetType(){el("typeForm").reset();set("typeId","");set("typeVersion","");el("typeCode").readOnly=false;el("typeActive").checked=true;}
  function resetTemplate(){el("templateForm").reset();set("templateId","");set("templateVersion","");el("templateCode").readOnly=false;el("templateActive").checked=true;}
  async function loadTypes() {
    state.types=await api("/types");
    const tbody=el("typeRows"),select=el("docType"),filter=el("docFilterDocumentType"),old=select.value;
    tbody.replaceChildren();select.replaceChildren();filter.replaceChildren();
    for(const [parent,label] of [[select,"Choose type"],[filter,"All types"]]){const o=new Option(label,"");parent.append(o);}
    for(const t of state.types){
      if(t.isActive)select.add(new Option(t.code+" — "+t.name,t.code));
      filter.add(new Option(t.code+" — "+t.name,t.code));
      row(tbody,[t.code,t.name,t.requiresVerification?"Yes":"No",t.isSensitive?"Yes":"No",t.isActive?"Active":"Inactive"],[
        button("Edit",async()=>{set("typeId",t.id);set("typeVersion",t.rowVersion);set("typeCode",t.code);el("typeCode").readOnly=true;set("typeName",t.name);set("typeDescription",t.description);el("typeVerify").checked=t.requiresVerification;el("typeSensitive").checked=t.isSensitive;el("typeActive").checked=t.isActive;el("typeCode").focus();})
      ]);
    } select.value=old;
  }
  async function loadTemplates() {
    state.templates=await api("/templates");const tbody=el("templateRows");tbody.replaceChildren();
    for(const t of state.templates)row(tbody,[t.code,t.name,t.format,t.isActive?"Active":"Inactive"],[
      button("Edit",async()=>{set("templateId",t.id);set("templateVersion",t.rowVersion);set("templateCode",t.code);el("templateCode").readOnly=true;set("templateName",t.name);set("templateFormat",t.format);set("templateContent",t.templateContent);el("templateActive").checked=t.isActive;el("templateCode").focus();})
    ]);
  }
  async function download(ref) {
    const r=await fetch(root+"/files/"+encodeURIComponent(ref)+"/download",{credentials:"same-origin",cache:"no-store"});
    if(!r.ok)throw Error("Download unavailable ("+r.status+")");
    const blob=await r.blob(),url=URL.createObjectURL(blob),a=document.createElement("a");
    a.href=url;a.download="document";document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
  }
  async function loadDocuments() {
    const q=new URLSearchParams({page:state.page,pageSize:20});
    for(const [field,id] of [["entityType","docFilterType"],["entityId","docFilterId"],["documentTypeCode","docFilterDocumentType"],["status","docFilterStatus"],["search","docFilterSearch"]])if(val(id))q.set(field,val(id));
    const d=await api("/documents?"+q);state.documents=d.items??[];state.total=d.totalCount??0;
    const tbody=el("docRows");tbody.replaceChildren();
    for(const item of state.documents){
      const expired=item.expiresAt && new Date(item.expiresAt)<=new Date();
      row(tbody,[item.title||item.reference,item.documentTypeCode,item.entityType||"Institution-wide",date(item.expiresAt),item.isActive?"Active":"Inactive"],[
        button("Edit",async()=>{set("docReference",item.reference);set("docVersion",item.rowVersion);set("docFile",item.fileReference);set("docType",item.documentTypeCode);set("docTitle",item.title);set("docEntityType",item.entityType||"");set("docEntityId",item.entityId);set("docExpiry",localDate(item.expiresAt));el("docActive").checked=item.isActive;}),
        button("Download",()=>download(item.fileReference),!item.isActive||expired),
        button("Deactivate",async()=>{if(!confirm("Deactivate document?"))return;await api("/documents/"+item.reference+"/deactivate","POST",{rowVersion:item.rowVersion});await loadDocuments();msg("Document deactivated.");},!item.isActive)
      ]);
    }
    const pages=Math.max(1,Math.ceil(state.total/20));el("docPageInfo").textContent="Page "+state.page+" of "+pages+" · "+state.total+" records";el("docPrev").disabled=state.page<=1;el("docNext").disabled=state.page>=pages;
  }
  async function loadPending() {
    const d=await api("/files/unattached?page="+state.pendingPage+"&pageSize=10");state.pending=d.items??[];state.pendingTotal=d.totalCount??0;
    const tbody=el("docPendingRows");tbody.replaceChildren();
    for(const f of state.pending)row(tbody,[f.originalFileName,f.sizeBytes+" bytes",date(f.createdAt)],[
      button("Select",async()=>{set("docFile",f.reference);el("docFile").focus();})
    ]);
    const pages=Math.max(1,Math.ceil(state.pendingTotal/10));el("docPendingInfo").textContent="Page "+state.pendingPage+" of "+pages;
    el("docPendingPrev").disabled=state.pendingPage<=1;el("docPendingNext").disabled=state.pendingPage>=pages;
  }
  el("docUploadForm").addEventListener("submit",e=>{e.preventDefault();run(async()=>{
    const file=el("docUploadFile").files[0];if(!file||file.size>5242880)throw Error("Choose a file up to 5 MB.");
    const data=new FormData();data.append("file",file);data.append("requestId",crypto.randomUUID());
    const f=await api("/files","POST",data,true);set("docFile",f.reference);await loadPending();msg("Security-verified file uploaded. Save its document metadata.");
  });});
  el("docForm").addEventListener("submit",e=>{e.preventDefault();run(async()=>{
    const ref=val("docReference");const payload={fileReference:val("docFile"),documentTypeCode:val("docType"),title:val("docTitle")||null,
      entityType:val("docEntityType")||null,expiresAt:val("docExpiry")?new Date(val("docExpiry")).toISOString():null,
      isActive:el("docActive").checked,rowVersion:val("docVersion")||null};
    await api("/documents"+(ref?"/"+encodeURIComponent(ref):""),ref?"PUT":"POST",payload);resetDoc();await loadDocuments();await loadPending();msg("Document saved.");
  });});
  el("typeForm").addEventListener("submit",e=>{e.preventDefault();run(async()=>{
    const id=val("typeId");await api("/types"+(id?"/"+id:""),id?"PUT":"POST",{code:val("typeCode"),name:val("typeName"),
      description:val("typeDescription"),requiresVerification:el("typeVerify").checked,isSensitive:el("typeSensitive").checked,
      isActive:el("typeActive").checked,rowVersion:val("typeVersion")||null});resetType();await loadTypes();msg("Document type saved.");
  });});
  el("templateForm").addEventListener("submit",e=>{e.preventDefault();run(async()=>{
    const id=val("templateId");await api("/templates"+(id?"/"+id:""),id?"PUT":"POST",{code:val("templateCode"),name:val("templateName"),
      templateContent:el("templateContent").value,format:val("templateFormat"),isActive:el("templateActive").checked,
      rowVersion:val("templateVersion")||null});resetTemplate();await loadTemplates();msg("Template saved.");
  });});
  el("docFilter").addEventListener("submit",e=>{e.preventDefault();state.page=1;run(loadDocuments);});
  el("docFilterClear").addEventListener("click",()=>{el("docFilter").reset();state.page=1;run(loadDocuments);});
  el("docRefresh").addEventListener("click",()=>run(async()=>{await Promise.all([loadTypes(),loadTemplates(),loadDocuments(),loadPending()]);msg("Refreshed.");}));
  el("docPendingRefresh").addEventListener("click",()=>run(loadPending));
  el("docPrev").addEventListener("click",()=>{state.page--;run(loadDocuments);});
  el("docNext").addEventListener("click",()=>{state.page++;run(loadDocuments);});
  el("docPendingPrev").addEventListener("click",()=>{state.pendingPage--;run(loadPending);});
  el("docPendingNext").addEventListener("click",()=>{state.pendingPage++;run(loadPending);});
  el("docCancel").addEventListener("click",resetDoc);el("typeCancel").addEventListener("click",resetType);el("templateCancel").addEventListener("click",resetTemplate);
  document.querySelectorAll("[data-doc-tab]").forEach(tab=>tab.addEventListener("click",()=>{
    document.querySelectorAll("[data-doc-tab]").forEach(x=>x.classList.toggle("active",x===tab));
    document.querySelectorAll("[data-doc-panel]").forEach(x=>x.hidden=x.dataset.docPanel!==tab.dataset.docTab);
  }));
  run(async()=>{await Promise.all([loadTypes(),loadTemplates(),loadDocuments(),loadPending()]);});
})();