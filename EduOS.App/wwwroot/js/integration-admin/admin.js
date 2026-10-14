(() => {
"use strict";
const base="/api/integration-admin", $=id=>document.getElementById(id);
const token=document.querySelector('meta[name="request-verification-token"]')?.content || document.querySelector('input[name="__RequestVerificationToken"]')?.value;
let pendingCredential=null, creatingCredential=false, savingWebhook=false, deliveryLoadVersion=0, webhookLoadVersion=0, credentialLoadVersion=0;
const state={webhooks:[],credentials:[],webhookPage:1,credentialPage:1,deliveryPage:1,deliveryId:null,webhookPages:1,credentialPages:1,deliveryPages:1};
function notice(message,kind="danger"){const el=$("iaNotice");el.textContent=message;el.className="alert alert-"+kind;el.classList.remove("d-none");}
async function api(path,method="GET",body){
 const options={method,credentials:"same-origin",cache:"no-store",headers:{"Accept":"application/json"}};
 if(method!=="GET"){options.headers["Content-Type"]="application/json";options.headers["RequestVerificationToken"]=token||"";options.body=JSON.stringify(body??{});}
 let response;try{response=await fetch(base+path,options);}catch{throw Error("Network error. Check your connection.");}
 let result;try{result=await response.json();}catch{throw Error("Unexpected server response ("+response.status+").");}
 if(!response.ok || !result.success){const error=Error(result.message||"Request failed ("+response.status+").");error.status=response.status;throw error;}
 return result.data;
}
function cell(tr,value){const td=document.createElement("td");td.textContent=value==null?"":String(value);tr.append(td);return td;}
function button(label,handler,cls="btn btn-outline-primary btn-sm"){const b=document.createElement("button");b.type="button";b.className=cls;b.textContent=label;b.addEventListener("click",handler);return b;}
function text(v){return String(v??"").trim();}
function csv(v){return text(v).split(",").map(x=>x.trim()).filter(Boolean);}
function when(v){return v?new Date(v).toLocaleString():"—";}
function pagination(prefix,page,pages){$(prefix+"Page").textContent="Page "+page+" / "+Math.max(1,pages);$(prefix+"Prev").disabled=page<=1;$(prefix+"Next").disabled=page>=pages;}
function resetWebhook(){ $("iaWebhookForm").reset();$("iaWebhookId").value="";$("iaWebhookVersion").value="";$("iaWebhookSecret").value="";$("iaWebhookClear").checked=false; }
function editWebhook(row){$("iaWebhookId").value=row.id;$("iaWebhookVersion").value=row.rowVersion;$("iaWebhookName").value=row.name;$("iaWebhookUrl").value=row.url;$("iaWebhookEvents").value=(row.eventCodes||[]).join(", ");$("iaWebhookSecret").value="";$("iaWebhookActive").value=String(row.isActive);$("iaWebhookClear").checked=false;$("iaWebhookName").focus();}
async function loadWebhooks(){
 const version=++webhookLoadVersion, page=state.webhookPage;
 const d=await api("/webhooks?page="+page+"&pageSize=20");
 if(version!==webhookLoadVersion || page!==state.webhookPage)return;
 state.webhooks=d.items||[];state.webhookPages=d.totalPages||1;
 const tbody=$("iaWebhookRows");tbody.replaceChildren();
 for(const row of state.webhooks){
  const tr=document.createElement("tr");cell(tr,row.name);cell(tr,row.url);cell(tr,(row.eventCodes||[]).join(", "));
  cell(tr,row.hasSecret?"Configured":"Missing");cell(tr,row.isActive?"Active":"Inactive");
  const actions=cell(tr,"");actions.append(button("Edit",()=>editWebhook(row)));
  actions.append(" ",button("Deliveries",()=>{state.deliveryId=row.id;state.deliveryPage=1;$("iaDeliveryFor").textContent="— "+row.name;loadDeliveries().catch(e=>notice(e.message));}));
  tbody.append(tr);
 }
 pagination("iaWebhook",state.webhookPage,state.webhookPages);
}
async function loadDeliveries(){
 const version=++deliveryLoadVersion, endpointId=state.deliveryId, page=state.deliveryPage;
 const tbody=$("iaDeliveryRows");tbody.replaceChildren();
 if(!endpointId){pagination("iaDelivery",1,1);return;}
 const d=await api("/webhooks/"+endpointId+"/deliveries?page="+page+"&pageSize=20");
 if(version!==deliveryLoadVersion || endpointId!==state.deliveryId || page!==state.deliveryPage)return;
 state.deliveryPages=d.totalPages||1;
 for(const row of d.items||[]){
  const tr=document.createElement("tr");
  cell(tr,row.eventCode);cell(tr,row.eventId);cell(tr,row.state);cell(tr,row.attemptCount);
  cell(tr,when(row.lastAttemptAt));cell(tr,when(row.nextAttemptAt));cell(tr,row.failureReason);
  tbody.append(tr);
 }
 pagination("iaDelivery",state.deliveryPage,state.deliveryPages);
}
async function loadCredentials(){
 const version=++credentialLoadVersion, page=state.credentialPage;
 const d=await api("/credentials?page="+page+"&pageSize=20");
 if(version!==credentialLoadVersion || page!==state.credentialPage)return;
 state.credentials=d.items||[];state.credentialPages=d.totalPages||1;
 const tbody=$("iaCredentialRows");tbody.replaceChildren();
 for(const row of state.credentials){
  const tr=document.createElement("tr");cell(tr,row.name);cell(tr,row.keyPrefix);
  cell(tr,(row.allowedScopes||[]).join(", "));cell(tr,when(row.expiresAt));
  cell(tr,when(row.lastUsedAt));cell(tr,row.isActive?"Active":"Revoked");
  const actions=cell(tr,"");
  if(row.isActive)actions.append(button("Revoke",async()=>{
   if(!confirm("Revoke credential '"+row.name+"'? This cannot be undone."))return;
   try{await api("/credentials/"+row.id+"/revoke","POST",{rowVersion:row.rowVersion});notice("Credential revoked.","success");await loadCredentials();}
   catch(e){notice(e.message);}
  },"btn btn-outline-danger btn-sm"));
  tbody.append(tr);
 }
 pagination("iaCredential",state.credentialPage,state.credentialPages);
}
$("iaWebhookForm").addEventListener("submit",async e=>{
 e.preventDefault();if(savingWebhook)return;const id=$("iaWebhookId").value;
 const request={name:text($("iaWebhookName").value),url:text($("iaWebhookUrl").value),secret:$("iaWebhookSecret").value||null,clearSecret:$("iaWebhookClear").checked,eventCodes:csv($("iaWebhookEvents").value),isActive:$("iaWebhookActive").value==="true",rowVersion:id?$("iaWebhookVersion").value:null};
 if(!request.name||!request.url||!request.eventCodes.length){notice("Name, HTTPS URL and event codes are required.");return;}
 savingWebhook=true;
 try{await api(id?"/webhooks/"+id:"/webhooks",id?"PUT":"POST",request);resetWebhook();notice("Webhook configuration saved.","success");await loadWebhooks();}
 catch(err){notice(err.message);}
 finally{savingWebhook=false;}
});
$("iaWebhookReset").addEventListener("click",resetWebhook);
$("iaCredentialForm").addEventListener("submit",async e=>{
 e.preventDefault();if(creatingCredential)return;const expiry=$("iaCredentialExpiry").value;
 const expiryDate=expiry?new Date(expiry):null;
 if(expiryDate && !Number.isFinite(expiryDate.getTime())){notice("Enter a valid expiration date.");return;}
 const request={name:text($("iaCredentialName").value),allowedScopes:csv($("iaCredentialScopes").value),expiresAt:expiryDate?expiryDate.toISOString():null};
 if(!request.name||!request.allowedScopes.length){notice("Name and scopes are required.");return;}
 const fingerprint=JSON.stringify(request);
 if(!pendingCredential||pendingCredential.fingerprint!==fingerprint)pendingCredential={fingerprint,id:crypto.randomUUID()};
 request.clientRequestId=pendingCredential.id;
 creatingCredential=true;
 try{
  const created=await api("/credentials","POST",request);
  pendingCredential=null;
  $("iaOneTimeKey").textContent=created.plainTextKey;
  $("iaKeyPanel").classList.remove("d-none");
  $("iaCredentialForm").reset();notice("Credential created. Copy its key now.","success");await loadCredentials();
 }catch(err){
  if(err.status===409){pendingCredential=null;notice(err.message+" The original key cannot be recovered if already processed. Review the credential list before creating another.");}
  else notice(err.message);
 }finally{creatingCredential=false;}
});
$("iaHideKey").addEventListener("click",()=>{$("iaOneTimeKey").textContent="";$("iaKeyPanel").classList.add("d-none");});
for(const [prefix,kind,loader] of [["iaWebhook","webhook",loadWebhooks],["iaCredential","credential",loadCredentials],["iaDelivery","delivery",loadDeliveries]]){
 for(const [suffix,delta] of [["Prev",-1],["Next",1]]){
  $(prefix+suffix).addEventListener("click",()=>{
   const pageKey=kind+"Page",pagesKey=kind+"Pages",next=state[pageKey]+delta;
   if(next<1||next>state[pagesKey])return;
   state[pageKey]=next;loader().catch(e=>notice(e.message));
  });
 }
}
Promise.all([loadWebhooks(),loadCredentials()]).catch(e=>notice(e.message));
})();
