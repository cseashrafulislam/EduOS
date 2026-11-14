(() => {
"use strict";
const root=document.getElementById("platformModuleAdministration");
if(!root)return;
const $=id=>document.getElementById(id);
const bn=root.dataset.bangla==="true",t=(en,bangla)=>bn?bangla:en;
const rows=$("moduleRows"),status=$("moduleMessage"),retry=$("moduleRetry");
const editor=$("moduleEditor"),form=$("moduleForm"),name=$("moduleEditName");
const description=$("moduleEditDescription"),order=$("moduleEditOrder");
const active=$("moduleEditActive"),save=$("moduleSave");
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content||"";
let selected=null,requestId=null,busy=false,sequence=0;
const cell=(tr,v)=>{const td=document.createElement("td");td.textContent=String(v??"");tr.appendChild(td);return td;};
async function api(route,method="GET",body){
    const headers={Accept:"application/json"},options={method,credentials:"same-origin",cache:"no-store",headers};
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
function edit(item){
    if(busy)return;
    selected=item;requestId=crypto.randomUUID();
    $("moduleEditCode").textContent=item.code;
    name.value=item.name;description.value=item.description??"";
    order.value=item.displayOrder;
    active.checked=!!item.isActive;
    active.disabled=!!item.isCore;
    $("moduleCoreWarning").hidden=!item.isCore;
    editor.hidden=false;
    name.focus();
}
function close(){
    if(busy)return;
    selected=null;requestId=null;editor.hidden=true;form.reset();
}
function render(items){
    rows.replaceChildren();
    if(!Array.isArray(items)||!items.length){
        const tr=document.createElement("tr");cell(tr,t("No product modules found.","কোনো মডিউল পাওয়া যায়নি।")).colSpan=6;rows.appendChild(tr);return;
    }
    for(const item of items){
        const tr=document.createElement("tr");
        cell(tr,item.code);cell(tr,item.name);cell(tr,item.isCore?t("Yes","হ্যাঁ"):t("No","না"));
        cell(tr,item.displayOrder);cell(tr,item.isActive?t("Available","উপলব্ধ"):t("Unavailable","অনুপলব্ধ"));
        const td=cell(tr,""),button=document.createElement("button");
        button.type="button";button.className="btn btn-sm btn-outline-primary";
        button.textContent=t("Edit","সম্পাদনা");button.disabled=!item.rowVersion;
        button.addEventListener("click",()=>edit(item));td.appendChild(button);rows.appendChild(tr);
    }
}
async function load(){
    const ticket=++sequence;retry.disabled=true;
    try{
        const data=await api("/api/platform/catalog/modules");
        if(ticket!==sequence)return false;
        render(data);retry.hidden=true;status.textContent="";return true;
    }catch(error){
        if(ticket!==sequence)return false;
        status.textContent=error.message||t("Unable to load modules.","মডিউল আনা যায়নি।");
        retry.hidden=false;return false;
    }finally{if(ticket===sequence&&!busy)retry.disabled=false;}
}
form.addEventListener("submit",async event=>{
    event.preventDefault();if(busy||!selected||!form.reportValidity())return;
    const moduleName=name.value.trim(),n=Number(order.value);
    if(!moduleName){name.setCustomValidity(t("Enter a module name.","মডিউলের নাম লিখুন।"));name.reportValidity();return;}
    if(!Number.isInteger(n)||n<0||n>100000){status.textContent=t("Enter a valid display order.","সঠিক তালিকার ক্রম লিখুন।");return;}
    if(!confirm(t("Save this module configuration? Existing tenant selections remain unchanged.",
        "এই মডিউলের সেটিংস সংরক্ষণ করবেন? আগের প্রতিষ্ঠানের নির্বাচন অপরিবর্তিত থাকবে।")))return;
    busy=true;save.disabled=true;retry.disabled=true;
    try{
        await api("/api/platform/catalog/modules/"+encodeURIComponent(selected.id)+"/configuration","POST",{
            requestId,rowVersion:selected.rowVersion,name:moduleName,
            description:description.value.trim(),displayOrder:n,
            isActive:selected.isCore?true:active.checked
        });
        selected=null;requestId=null;editor.hidden=true;form.reset();
        const reloaded=await load();
        status.textContent=reloaded?t("Module updated.","মডিউল সংরক্ষিত হয়েছে।"):
            t("Saved, but the list could not reload. Try again.","সংরক্ষণ হয়েছে, কিন্তু তালিকা আনা যায়নি। আবার চেষ্টা করুন।");
    }catch(error){
        if(error.status===409){selected=null;requestId=null;editor.hidden=true;await load();}
        status.textContent=error.message+(error.status===409?
            t(" Review the latest row before editing.","আবার সম্পাদনার আগে সর্বশেষ তথ্য দেখুন।"):"");
    }finally{busy=false;save.disabled=false;retry.disabled=false;}
});
name.addEventListener("input",()=>name.setCustomValidity(""));
form.addEventListener("input",()=>{if(!busy&&selected)requestId=crypto.randomUUID();});
form.addEventListener("change",()=>{if(!busy&&selected)requestId=crypto.randomUUID();});
$("moduleCancel").addEventListener("click",close);
retry.addEventListener("click",()=>{if(!busy)load();});
load();
})();
