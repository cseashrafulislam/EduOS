(() => {
'use strict';
const form=document.getElementById('transportRouteVehicleForm');
if(!form)return;
const el=id=>document.getElementById(id);
const token=form.querySelector('[name="__RequestVerificationToken"]')?.value||'';
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(body!==undefined){headers['Content-Type']='application/json';headers.RequestVerificationToken=token;}
 const response=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success)throw Error(result?.message||'Transport request failed.');
 return result.data;
}
function notify(message,ok=false){
 const box=el('transportRouteVehicleAlert');box.className='alert alert-'+(ok?'success':'danger');box.textContent=message;box.focus();
}
function option(select,value,label){
 const node=document.createElement('option');node.value=String(value);node.textContent=label;select.append(node);
}
window.transportLink={el,form,api,notify,option,routes:[],vehicles:[],requestId:crypto.randomUUID()};
})();