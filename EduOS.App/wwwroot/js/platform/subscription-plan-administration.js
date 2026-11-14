(() => {
"use strict";
const root=document.getElementById("subscriptionPlanAdministration");
if(!root)return;
const $=id=>document.getElementById(id);
const bn=root.dataset.bangla==="true",t=(en,bangla)=>bn?bangla:en;
const rows=$("planRows"),status=$("planMessage"),retry=$("planRetry");
const editor=$("planConfiguration"),title=$("planConfigurationTitle");
const form=$("planConfigurationForm"),featureRows=$("planFeatureRows"),save=$("planSave");
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content||"";
const configFields={
    monthlyPrice:"planMonthly",yearlyPrice:"planYearly",trialDays:"planTrial",
    maxStudents:"planStudents",maxEmployees:"planEmployees",maxUsers:"planUsers",
    maxCampuses:"planCampuses",maxStorageMb:"planStorage"
};
let busy=false,editing=null,editRequestId=null,sequence=0;
const availabilityRequests=new Map();
const cell=(tr,value)=>{const td=document.createElement("td");td.textContent=String(value??"");tr.appendChild(td);return td;};
async function api(route,method="GET",body){
    const headers={Accept:"application/json"},options={method,headers,cache:"no-store",credentials:"same-origin"};
    if(method!=="GET"){
        if(!token)throw Error(t("Security token unavailable. Reload this page.","নিরাপত্তা টোকেন নেই। পৃষ্ঠা পুনরায় খুলুন।"));
        headers["Content-Type"]="application/json";
        headers["RequestVerificationToken"]=token;
        options.body=JSON.stringify(body);
    }
    const response=await fetch(route,options),payload=await response.json().catch(()=>null);
    if(!response.ok||!payload?.success){
        const error=Error(payload?.message||t("Request failed.","অনুরোধটি সম্পন্ন হয়নি।"));
        error.status=response.status;throw error;
    }
    return payload.data;
}
async function load(){
    const ticket=++sequence;retry.disabled=true;
    try{
        const data=await api("/api/platform/subscription-plans");
        if(ticket!==sequence)return false;
        render(data);retry.hidden=true;status.textContent="";return true;
    }catch(error){
        if(ticket!==sequence)return false;
        status.textContent=error.message;retry.hidden=false;return false;
    }finally{if(ticket===sequence&&!busy)retry.disabled=false;}
}
async function change(item,isActive,isPublic){
    if(busy||!confirm(t("Change availability for "+item.name+"? Existing subscriptions remain unchanged.",
        item.name+"-এর উপলব্ধতা বদলাবেন? আগের সাবস্ক্রিপশন অপরিবর্তিত থাকবে।")))return;
    const key=[item.id,item.rowVersion,isActive,isPublic].join("|");
    const requestId=availabilityRequests.get(key)||crypto.randomUUID();
    availabilityRequests.set(key,requestId);
    busy=true;retry.disabled=true;
    try{
        await api("/api/platform/subscription-plans/"+encodeURIComponent(item.id)+"/availability","POST",{
            requestId,isActive,isPublic,rowVersion:item.rowVersion
        });
        availabilityRequests.delete(key);
        const fresh=await load();
        status.textContent=fresh?t("Plan availability updated.","প্ল্যানের উপলব্ধতা সংরক্ষিত হয়েছে।"):
            t("Saved but the list could not reload. Try again.","সংরক্ষণ হয়েছে, কিন্তু তালিকা আনা যায়নি। আবার চেষ্টা করুন।");
    }catch(error){
        if(error.status===409){availabilityRequests.delete(key);await load();}
        status.textContent=error.message+(error.status===409?
            t(" Review the latest plan before editing.","পরিবর্তনের আগে সর্বশেষ প্ল্যান দেখুন।"):"");
    }finally{busy=false;retry.disabled=false;}
}
async function edit(item){
    if(busy)return;
    busy=true;retry.disabled=true;
    try{
        const config=await api("/api/platform/subscription-plans/"+encodeURIComponent(item.id)+"/configuration");
        editing=config;editRequestId=crypto.randomUUID();
        title.textContent=t("Edit ","সম্পাদনা: ")+config.name+" ("+config.currencyCode+")";
        for(const [key,id] of Object.entries(configFields))$(id).value=config[key];
        featureRows.replaceChildren();
        for(const feature of config.features||[]){
            const wrap=document.createElement("div");wrap.className="col-12 col-md-6 col-xl-4";
            wrap.dataset.featureId=String(feature.featureId);
            const box=document.createElement("div");box.className="border rounded-3 p-2 h-100";
            const label=document.createElement("label");label.className="form-check-label d-flex align-items-center gap-2";
            const check=document.createElement("input");check.type="checkbox";
            check.className="form-check-input m-0";check.checked=!!feature.isEnabled;
            label.append(check,document.createTextNode(feature.name+" ("+feature.code+")"));
            const limit=document.createElement("input");limit.type="number";
            limit.className="form-control form-control-sm mt-2";limit.min="0";limit.step="1";
            limit.placeholder=t("Optional limit • blank = unlimited","ঐচ্ছিক সীমা • ফাঁকা = সীমাহীন");
            limit.setAttribute("aria-label",t("Limit for ","সীমা: ")+feature.name);
            limit.value=feature.limitValue??"";limit.disabled=!check.checked;
            check.addEventListener("change",()=>{limit.disabled=!check.checked;if(!check.checked)limit.value="";});
            box.append(label,limit);wrap.appendChild(box);featureRows.appendChild(wrap);
        }
        editor.hidden=false;
        editor.scrollIntoView({behavior:"smooth",block:"nearest"});
    }catch(error){status.textContent=error.message;}
    finally{busy=false;retry.disabled=false;}
}
function render(items){
    rows.replaceChildren();
    if(!Array.isArray(items)||!items.length){
        const tr=document.createElement("tr");cell(tr,t("No plans found.","কোনো প্ল্যান পাওয়া যায়নি।")).colSpan=7;
        rows.appendChild(tr);return;
    }
    for(const item of items){
        const tr=document.createElement("tr");
        cell(tr,item.name);cell(tr,item.code);
        cell(tr,item.monthlyPrice+" "+item.currencyCode);
        cell(tr,item.yearlyPrice+" "+item.currencyCode);
        cell(tr,item.isActive?t("Accepting subscriptions","সাবস্ক্রিপশন চালু"):t("Inactive","নিষ্ক্রিয়"));
        cell(tr,item.isPublic?t("Visible","দৃশ্যমান"):t("Hidden","লুকানো"));
        const actions=cell(tr,"");
        const action=(label,css,handler,disabled=false)=>{
            const button=document.createElement("button");
            button.type="button";button.className="btn btn-sm "+css+" me-1 mb-1";
            button.textContent=label;button.disabled=disabled;button.addEventListener("click",handler);
            actions.appendChild(button);
        };
        action(item.isActive?t("Deactivate","নিষ্ক্রিয় করুন"):t("Activate","সক্রিয় করুন"),
            "btn-outline-primary",()=>change(item,!item.isActive,item.isPublic),!item.rowVersion);
        action(item.isPublic?t("Hide pricing","মূল্য লুকান"):t("Publish pricing","মূল্য প্রকাশ করুন"),
            "btn-outline-secondary",()=>change(item,item.isActive,!item.isPublic),!item.rowVersion);
        action(t("Edit pricing & features","মূল্য ও ফিচার সম্পাদনা"),
            "btn-outline-success",()=>edit(item));
        rows.appendChild(tr);
    }
}
form.addEventListener("submit",async event=>{
    event.preventDefault();
    if(!editing||busy||!form.reportValidity())return;
    const request={requestId:editRequestId,rowVersion:editing.rowVersion,features:[]};
    for(const [key,id] of Object.entries(configFields))request[key]=Number($(id).value);
    for(const wrap of featureRows.children){
        const check=wrap.querySelector('input[type="checkbox"]');
        const limit=wrap.querySelector('input[type="number"]');
        request.features.push({featureId:Number(wrap.dataset.featureId),
            isEnabled:check.checked,limitValue:check.checked&&limit.value!==""?Number(limit.value):null});
    }
    if(!confirm(t("Save pricing, limits and features? Existing subscription price snapshots will remain unchanged.",
        "মূল্য, সীমা ও ফিচার সংরক্ষণ করবেন? আগের সাবস্ক্রিপশনের মূল্য অপরিবর্তিত থাকবে।")))return;
    busy=true;save.disabled=true;retry.disabled=true;
    try{
        await api("/api/platform/subscription-plans/"+encodeURIComponent(editing.id)+"/configuration","POST",request);
        editing=null;editRequestId=null;editor.hidden=true;
        const fresh=await load();
        status.textContent=fresh?t("Plan configuration saved.","প্ল্যানের সেটিংস সংরক্ষিত হয়েছে।"):
            t("Saved but list reload failed. Try again.","সংরক্ষণ হয়েছে, কিন্তু তালিকা আনা যায়নি। আবার চেষ্টা করুন।");
    }catch(error){
        if(error.status===409){editing=null;editRequestId=null;editor.hidden=true;await load();}
        status.textContent=error.message+(error.status===409?
            t(" Review current plan values before another edit.","আবার সম্পাদনার আগে নতুন প্ল্যানের তথ্য দেখুন।"):"");
    }finally{busy=false;save.disabled=false;retry.disabled=false;}
});
form.addEventListener("input",()=>{if(!busy&&editing)editRequestId=crypto.randomUUID();});
form.addEventListener("change",()=>{if(!busy&&editing)editRequestId=crypto.randomUUID();});
$("planCancel").addEventListener("click",()=>{
    if(!busy){editing=null;editRequestId=null;editor.hidden=true;}
});
retry.addEventListener("click",()=>{if(!busy)load();});
load();
})();
