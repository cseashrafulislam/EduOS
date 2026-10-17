(() => {
"use strict";
const $=id=>document.getElementById(id);
const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
let page=1,pages=1,loadVersion=0;
const busy=new Set();
function notice(message,kind="danger"){const el=$("tfNotice");el.textContent=message;el.className="alert alert-"+kind;el.classList.remove("d-none");}
async function api(path,method="GET",body){
 const options={method,credentials:"same-origin",cache:"no-store",headers:{"Accept":"application/json"}};
 if(method!=="GET"){options.headers["Content-Type"]="application/json";options.headers["RequestVerificationToken"]=token;options.body=JSON.stringify(body);}
 let response;try{response=await fetch("/api/tenant-features"+path,options);}catch{throw Error("Network error.");}
 let result;try{result=await response.json();}catch{throw Error("Unexpected server response.");}
 if(!response.ok||!result.success){const err=Error(result.message||"Request failed.");err.status=response.status;throw err;}
 return result.data;
}
function cell(tr,text){const td=document.createElement("td");td.textContent=text??"";tr.append(td);return td;}
function control(type,cls){const input=document.createElement("input");input.type=type;input.className=cls;return input;}
function utcInput(date){
 if(!date)return "";
 const d=new Date(date);
 return Number.isFinite(d.getTime())?d.toISOString().slice(0,16):"";
}
function toUtc(value){
 if(!value)return null;
 const date=new Date(value+":00Z");
 if(!Number.isFinite(date.getTime()))throw Error("Invalid UTC date.");
 return date.toISOString();
}
async function load(){
 const version=++loadVersion,requested=page;
 const data=await api("?page="+page+"&pageSize=20");
 if(version!==loadVersion||requested!==page)return;
 pages=data.totalPages||1;
 const tbody=$("tfRows");tbody.replaceChildren();
 for(const row of data.items||[]){
  const tr=document.createElement("tr");
  cell(tr,row.featureName+" ("+row.featureCode+")");
  cell(tr,row.includedInPlan?"Included":"Not included");
  const restricted=control("checkbox","form-check-input");restricted.checked=!!row.restrictionConfigured;
  cell(tr,"").append(restricted);
  const from=control("datetime-local","form-control form-control-sm");from.value=utcInput(row.effectiveFrom);
  cell(tr,"").append(from);
  const until=control("datetime-local","form-control form-control-sm");until.value=utcInput(row.effectiveTo);
  cell(tr,"").append(until);
  const reason=control("text","form-control form-control-sm");reason.maxLength=500;reason.value=row.reason||"";
  reason.placeholder="Reason for restriction";
  cell(tr,"").append(reason);
  const save=document.createElement("button");save.type="button";save.className="btn btn-primary btn-sm";save.textContent="Save";
  cell(tr,"").append(save);
  function toggle(){from.disabled=!restricted.checked;until.disabled=!restricted.checked;reason.disabled=!restricted.checked;}
  restricted.addEventListener("change",toggle);toggle();
  save.addEventListener("click",async()=>{
   if(busy.has(row.featureId))return;
   if(restricted.checked&&!reason.value.trim()){notice("A reason is required to restrict a feature.");reason.focus();return;}
   if(restricted.checked&&!confirm("Restrict feature '"+row.featureName+"' for this institution?"))return;
   let request;try{request={restricted:restricted.checked,effectiveFrom:restricted.checked?toUtc(from.value):null,
    effectiveTo:restricted.checked?toUtc(until.value):null,reason:restricted.checked?reason.value.trim():null,
    rowVersion:row.rowVersion||null};}catch(e){notice(e.message);return;}
   busy.add(row.featureId);save.disabled=true;
   try{await api("/"+row.featureId,"PUT",request);notice("Feature restriction saved.","success");await load();}
   catch(e){notice(e.message+(e.status===409?" Reload the page before retrying.":""));}
   finally{busy.delete(row.featureId);save.disabled=false;}
  });
  tbody.append(tr);
 }
 $("tfPage").textContent="Page "+page+" / "+pages;
 $("tfPrev").disabled=page<=1;$("tfNext").disabled=page>=pages;
}
for(const [id,delta] of [["tfPrev",-1],["tfNext",1]]){
 $(id).addEventListener("click",()=>{const next=page+delta;if(next<1||next>pages)return;page=next;load().catch(e=>notice(e.message));});
}
load().catch(e=>notice(e.message));
})();
