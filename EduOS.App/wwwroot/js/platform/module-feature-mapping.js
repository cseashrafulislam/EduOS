(() => {
"use strict";
const root=document.getElementById("platformModuleFeatures");
if(!root)return;
const $=id=>document.getElementById(id);
const bn=root.dataset.bangla==="true",t=(en,bangla)=>bn?bangla:en;
const moduleSelect=$("moduleFeatureModule"),rows=$("moduleFeatureRows");
const status=$("moduleFeatureStatus"),retry=$("moduleFeatureRetry");
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content||"";
let busy=false,sequence=0,loadedModules=false;
const attemptIds=new Map();
const cell=(tr,value)=>{const td=document.createElement("td");td.textContent=String(value??"");tr.appendChild(td);return td;};
async function api(route,method="GET",body){
    const headers={Accept:"application/json"},options={method,headers,credentials:"same-origin",cache:"no-store"};
    if(method!=="GET"){
        if(!token)throw Error(t("Security token unavailable. Reload the page.","নিরাপত্তা টোকেন নেই। পৃষ্ঠা পুনরায় খুলুন।"));
        headers["Content-Type"]="application/json";headers.RequestVerificationToken=token;
        options.body=JSON.stringify(body);
    }
    const response=await fetch(route,options),payload=await response.json().catch(()=>null);
    if(!response.ok||!payload?.success){
        const error=Error(payload?.message||t("Request failed.","অনুরোধটি সম্পন্ন হয়নি।"));
        error.status=response.status;throw error;
    }
    return payload.data;
}
function render(directory){
    rows.replaceChildren();
    const features=directory.features||[];
    for(const item of features){
        const tr=document.createElement("tr");
        cell(tr,item.featureName+" ("+item.featureCode+")");
        cell(tr,item.isActive?t("Yes","হ্যাঁ"):t("No","না"));
        const locked=(directory.isCore&&item.isMapped)||
            ((!directory.isActive||!item.isActive)&&!item.isMapped);
        const checkbox=document.createElement("input");
        checkbox.type="checkbox";checkbox.className="form-check-input";
        checkbox.checked=!!item.isMapped;checkbox.disabled=!!locked;
        checkbox.setAttribute("aria-label",t("Mapped ","সংযুক্ত ")+item.featureName);
        cell(tr,"").appendChild(checkbox);
        const button=document.createElement("button");
        button.type="button";button.className="btn btn-sm btn-outline-primary";
        button.textContent=t("Save","সংরক্ষণ");button.disabled=locked;
        button.addEventListener("click",async()=>{
            if(busy||!confirm(t("Save this feature mapping? Existing plan entitlements will not be edited.",
                "এই ফিচার ম্যাপিং সংরক্ষণ করবেন? বিদ্যমান প্ল্যানের অধিকার অপরিবর্তিত থাকবে।")))return;
            const key=[directory.moduleId,item.featureId,directory.moduleRowVersion,
                item.featureRowVersion,item.mappingRowVersion,checkbox.checked].join("|");
            const requestId=attemptIds.get(key)||crypto.randomUUID();
            attemptIds.set(key,requestId);
            busy=true;button.disabled=true;moduleSelect.disabled=true;retry.disabled=true;
            try{
                await api("/api/platform/catalog/module-features/"+encodeURIComponent(directory.moduleId)+
                    "/features/"+encodeURIComponent(item.featureId),"POST",{
                        requestId,moduleRowVersion:directory.moduleRowVersion,
                        featureRowVersion:item.featureRowVersion,
                        mappingRowVersion:item.mappingRowVersion,isMapped:checkbox.checked
                    });
                attemptIds.delete(key);
                const fresh=await load();
                status.textContent=fresh?t("Mapping saved.","ম্যাপিং সংরক্ষিত হয়েছে।"):
                    t("Saved but list reload failed. Try again.","সংরক্ষণ হয়েছে, কিন্তু তালিকা আনা যায়নি। আবার চেষ্টা করুন।");
            }catch(error){
                if(error.status===409){attemptIds.delete(key);await load();}
                status.textContent=error.message+(error.status===409?
                    t(" Review the latest mapping before retrying.","আবার চেষ্টা করার আগে নতুন ম্যাপিং দেখুন।"):"");
            }finally{busy=false;moduleSelect.disabled=false;retry.disabled=false;}
        });
        cell(tr,"").appendChild(button);rows.appendChild(tr);
    }
    if(!features.length){
        const tr=document.createElement("tr");
        cell(tr,t("No features found.","কোনো ফিচার পাওয়া যায়নি।")).colSpan=4;
        rows.appendChild(tr);
    }
}
async function load(){
    const selected=moduleSelect.value,ticket=++sequence;
    if(!selected){
        rows.replaceChildren();const tr=document.createElement("tr");
        cell(tr,t("Choose a product module.","প্রোডাক্ট মডিউল নির্বাচন করুন।")).colSpan=4;
        rows.appendChild(tr);status.textContent="";retry.hidden=true;return true;
    }
    retry.disabled=true;
    try{
        const directory=await api("/api/platform/catalog/module-features/"+encodeURIComponent(selected));
        if(ticket!==sequence||selected!==moduleSelect.value)return false;
        render(directory);retry.hidden=true;status.textContent="";return true;
    }catch(error){
        if(ticket!==sequence)return false;
        status.textContent=error.message;retry.hidden=false;return false;
    }finally{if(ticket===sequence&&!busy)retry.disabled=false;}
}
async function initialize(){
    if(busy)return false;
    retry.disabled=true;
    try{
        const modules=await api("/api/platform/catalog/modules"),previous=moduleSelect.value;
        while(moduleSelect.options.length>1)moduleSelect.remove(1);
        for(const item of modules||[]){
            const option=document.createElement("option");option.value=item.id;
            option.textContent=item.name+" ("+item.code+")";moduleSelect.appendChild(option);
        }
        loadedModules=true;retry.hidden=true;
        if(previous&&Array.from(moduleSelect.options).some(o=>o.value===previous))moduleSelect.value=previous;
        else if(moduleSelect.options.length>1)moduleSelect.selectedIndex=1;
        return await load();
    }catch(error){status.textContent=error.message;retry.hidden=false;return false;}
    finally{if(!busy)retry.disabled=false;}
}
moduleSelect.addEventListener("change",()=>{if(!busy){attemptIds.clear();load();}});
retry.addEventListener("click",()=>{if(!busy)(loadedModules?load():initialize());});
initialize();
})();
