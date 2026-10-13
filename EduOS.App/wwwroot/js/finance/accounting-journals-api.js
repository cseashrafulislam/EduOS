export async function journalRequest(path,method='GET',body=null){
 const headers={Accept:'application/json'};
 const options={method,credentials:'same-origin',headers};
 if(body!==null){headers['Content-Type']='application/json';options.body=JSON.stringify(body);}
 if(method!=='GET'){
  headers.RequestVerificationToken=document.querySelector('input[name="__RequestVerificationToken"]')?.value||'';
 }
 const response=await fetch('/api/finance/accounting/journals'+path,options);
 const payload=await response.json();
 if(!response.ok||!payload.success)throw new Error(payload.message||'Journal request failed.');
 return payload.data;
}
export async function journalSetup(path){
 const response=await fetch('/api/finance/accounting/setup'+path,{credentials:'same-origin'});
 const payload=await response.json();
 if(!response.ok||!payload.success)throw new Error(payload.message||'Accounting setup unavailable.');
 return payload.data;
}
