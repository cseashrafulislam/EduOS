(() => {
"use strict";
const root=document.getElementById("institutionModulePresets");
if(!root)return;
const $=id=>document.getElementById(id);
const bn=root.dataset.bangla==="true",t=(en,bangla)=>bn?bangla:en;
const typeSelect=$("presetType"),rows=$("presetRows"),status=$("presetMessage"),retry=$("presetRetry");
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content||"";
let busy=false,sequence=0,loadedTypes=false;
const attemptIds=new Map();
const cell=(tr,v)=>{const td=document.createElement("td");td.textContent=String(v??"");tr.appendChild(td);return td;};
async function api(route,method="GET",body){
    const headers={Accept:"application/json"},options={method,headers,cache:"no-store",credentials:"same-origin"};
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
function checkbox(checked,disabled,label){
    const input=document.createElement("input");
    input.type="checkbox";input.className="form-check-input";input.checked=!!checked;
    input.disabled=!!disabled;input.setAttribute("aria-label",label);return input;
}
function render(data){
    rows.replaceChildren();
    const list=data.modules||[];
    for(const item of list){
        const tr=document.createElement("tr");
        cell(tr,item.moduleName+" ("+item.moduleCode+")");
        cell(tr,item.isCore?t("Yes","হ্যাঁ"):t("No","না"));
        cell(tr,item.isActive?t("Yes","হ্যাঁ"):t("No","না"));
        const locked=item.isCore||!item.isActive;
        const required=checkbox(item.isRequired,locked,t("Required ","আবশ্যক ")+item.moduleName);
        const enabled=checkbox(item.isDefaultEnabled,locked,t("Default enabled ","ডিফল্ট চালু ")+item.moduleName);
        required.addEventListener("change",()=>{if(required.checked)enabled.checked=true;});
        enabled.addEventListener("change",()=>{if(!enabled.checked)required.checked=false;});
        cell(tr,"").appendChild(required);cell(tr,"").appendChild(enabled);
        const button=document.createElement("button");
        button.type="button";button.className="btn btn-outline-primary btn-sm";
        button.textContent=t("Save","সংরক্ষণ");button.disabled=locked;
        button.addEventListener("click",async()=>{
            if(busy||!confirm(t("Save future onboarding defaults? Existing tenant choices stay unchanged.",
                "ভবিষ্যৎ নিবন্ধনের ডিফল্ট সংরক্ষণ করবেন? আগের প্রতিষ্ঠানের নির্বাচন বদলাবে না।")))return;
            const key=[data.institutionTypeId,item.moduleId,data.institutionTypeRowVersion,item.rowVersion,
                required.checked,enabled.checked].join("|");
            const requestId=attemptIds.get(key)||crypto.randomUUID();
            attemptIds.set(key,requestId);
            busy=true;button.disabled=true;typeSelect.disabled=true;retry.disabled=true;
            try{
                await api("/api/platform/catalog/institution-module-presets/"+
                    encodeURIComponent(data.institutionTypeId)+"/modules/"+encodeURIComponent(item.moduleId),"POST",{
                        requestId,institutionTypeRowVersion:data.institutionTypeRowVersion,
                        rowVersion:item.rowVersion,isRequired:required.checked,isDefaultEnabled:enabled.checked
                    });
                attemptIds.delete(key);
                const fresh=await load();
                status.textContent=fresh?t("Preset saved.","প্রিসেট সংরক্ষিত হয়েছে।"):
                    t("Saved but list reload failed. Try again.","সংরক্ষণ হয়েছে; তালিকা আনা যায়নি। আবার চেষ্টা করুন।");
            }catch(error){
                if(error.status===409){attemptIds.delete(key);await load();}
                status.textContent=error.message+(error.status===409?
                    t(" Review the latest preset before saving again.","আবার সংরক্ষণের আগে নতুন প্রিসেট দেখুন।"):"");
            }finally{busy=false;typeSelect.disabled=false;retry.disabled=false;}
        });
        cell(tr,"").appendChild(button);rows.appendChild(tr);
    }
    if(!list.length){
        const tr=document.createElement("tr");
        cell(tr,t("No modules found.","কোনো মডিউল পাওয়া যায়নি।")).colSpan=6;rows.appendChild(tr);
    }
}
async function load(){
    const selected=typeSelect.value,ticket=++sequence;
    if(!selected){
        rows.replaceChildren();const tr=document.createElement("tr");
        cell(tr,t("Choose an institution type.","প্রতিষ্ঠানের ধরন নির্বাচন করুন।")).colSpan=6;
        rows.appendChild(tr);status.textContent="";retry.hidden=true;return true;
    }
    retry.disabled=true;
    try{
        const data=await api("/api/platform/catalog/institution-module-presets/"+encodeURIComponent(selected));
        if(ticket!==sequence||selected!==typeSelect.value)return false;
        render(data);retry.hidden=true;status.textContent="";return true;
    }catch(error){
        if(ticket!==sequence)return false;
        status.textContent=error.message;retry.hidden=false;return false;
    }finally{if(ticket===sequence&&!busy)retry.disabled=false;}
}
async function initialize(){
    if(busy)return false;
    retry.disabled=true;
    try{
        const types=await api("/api/platform/catalog/institution-types");
        const oldValue=typeSelect.value;
        while(typeSelect.options.length>1)typeSelect.remove(1);
        for(const item of types||[]){
            const option=document.createElement("option");
            option.value=item.id;option.textContent=(bn&&item.nameBangla?item.nameBangla:item.name)+" ("+item.code+")";
            typeSelect.appendChild(option);
        }
        loadedTypes=true;retry.hidden=true;
        if(oldValue&&Array.from(typeSelect.options).some(o=>o.value===oldValue))typeSelect.value=oldValue;
        else if(typeSelect.options.length>1)typeSelect.selectedIndex=1;
        return await load();
    }catch(error){
        status.textContent=error.message;retry.hidden=false;return false;
    }finally{if(!busy)retry.disabled=false;}
}
typeSelect.addEventListener("change",()=>{if(!busy){attemptIds.clear();load();}});
retry.addEventListener("click",()=>{if(!busy)(loadedTypes?load():initialize());});
initialize();
})();
