(()=>{
"use strict";
const $=id=>document.getElementById(id),base="/api/number-series";
const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value||
 document.querySelector('meta[name="request-verification-token"]')?.content||"";
let rows=[],page=1,pages=1,busy=false,version=0;
function notice(message,ok=false){const e=$("nsNotice");e.textContent=message;e.className="alert alert-"+(ok?"success":"danger");}
async function api(url,method="GET",body){
 const opt={method,credentials:"same-origin",cache:"no-store",headers:{Accept:"application/json"}};
 if(method!=="GET"){opt.headers["Content-Type"]="application/json";opt.headers["RequestVerificationToken"]=token;opt.body=JSON.stringify(body);}
 const response=await fetch(base+url,opt);
 const data=await response.json();
 if(!response.ok||!data.success)throw Error(data.message||"Request failed.");
 return data.data;
}
function cell(tr,value){const td=document.createElement("td");td.textContent=value==null?"":String(value);tr.append(td);return td;}
function reset(){$("nsForm").reset();$("nsId").value="";$("nsVersion").value="";$("nsKey").readOnly=false;}
function edit(row){
 $("nsId").value=row.id;$("nsVersion").value=row.rowVersion;
 $("nsKey").value=row.key;$("nsKey").readOnly=true;
 $("nsPrefix").value=row.prefix;$("nsSuffix").value=row.suffix||"";
 $("nsPadding").value=row.padding;$("nsActive").value=String(row.isActive);
 $("nsKey").focus();
}
async function load(){
 const n=++version,pg=page,search=$("nsSearch").value.trim();
 const d=await api("?page="+pg+"&pageSize=20&search="+encodeURIComponent(search));
 if(n!==version||pg!==page)return;
 rows=d.items||[];pages=d.totalPages||1;
 const body=$("nsRows");body.replaceChildren();
 for(const row of rows){
  const tr=document.createElement("tr");
  cell(tr,row.key);cell(tr,(row.prefix||"")+"{n:"+row.padding+"}"+(row.suffix||""));
  cell(tr,row.nextValue);cell(tr,row.isActive?"Active":"Inactive");
  const actions=cell(tr,""),btn=document.createElement("button");
  btn.type="button";btn.className="btn btn-outline-primary btn-sm";btn.textContent="Edit";
  btn.addEventListener("click",()=>edit(row));actions.append(btn);body.append(tr);
 }
 $("nsPage").textContent="Page "+page+" / "+pages;
 $("nsPrev").disabled=page<=1;$("nsNext").disabled=page>=pages;
}
$("nsForm").addEventListener("submit",async e=>{
 e.preventDefault();if(busy)return;
 const id=$("nsId").value,request={
  key:$("nsKey").value.trim(),prefix:$("nsPrefix").value.trim(),
  padding:Number($("nsPadding").value),suffix:$("nsSuffix").value.trim(),
  isActive:$("nsActive").value==="true",rowVersion:id?$("nsVersion").value:null
 };
 if(!/^[A-Za-z][A-Za-z0-9_.-]{1,99}$/.test(request.key)||
    request.padding<1||request.padding>30||
    request.prefix.length+request.suffix.length+request.padding>128){
  notice("Check series key, padding and number length.");return;
 }
 busy=true;
 try{await api(id?"/"+id:"",id?"PUT":"POST",request);reset();notice("Number series saved.",true);await load();}
 catch(err){notice(err.message);}finally{busy=false;}
});
$("nsReset").addEventListener("click",reset);
$("nsSearch").addEventListener("change",()=>{page=1;load().catch(e=>notice(e.message));});
$("nsPrev").addEventListener("click",()=>{if(page>1){page--;load().catch(e=>notice(e.message));}});
$("nsNext").addEventListener("click",()=>{if(page<pages){page++;load().catch(e=>notice(e.message));}});
load().catch(e=>notice(e.message));
})();
