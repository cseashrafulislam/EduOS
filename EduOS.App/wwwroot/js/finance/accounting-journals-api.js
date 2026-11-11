const session=[document.body?.dataset.authTenantId||'',document.body?.dataset.authSessionReference||''];
export const journalSessionLive=()=>session[0]===(document.body?.dataset.authTenantId||'')&&
 session[1]===(document.body?.dataset.authSessionReference||'')&&
 window.EduOSSessionTransitionPending?.()!==true;
export async function journalRequest(path,method='GET',body=null){
 if(!journalSessionLive())throw new Error('Institution session changed. Reload the page.');
 const headers={Accept:'application/json'};
 const options={method,credentials:'same-origin',cache:'no-store',headers};
 if(body!==null){headers['Content-Type']='application/json';options.body=JSON.stringify(body);}
 if(method!=='GET'){
  const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  if(!token)throw new Error('Missing antiforgery token. Reload the page.');
  headers.RequestVerificationToken=token;
 }
 const response=await fetch('/api/finance/accounting/journals'+path,options);
 const payload=await response.json();
 if(!journalSessionLive())throw new Error('Institution session changed. Reload the page.');
 if(!response.ok||!payload.success)throw new Error(payload.message||'Journal request failed.');
 return payload.data;
}
export async function journalSetup(path){
 if(!journalSessionLive())throw new Error('Institution session changed. Reload the page.');
 const response=await fetch('/api/finance/accounting/setup'+path,{credentials:'same-origin',cache:'no-store'});
 const payload=await response.json();
 if(!journalSessionLive())throw new Error('Institution session changed. Reload the page.');
 if(!response.ok||!payload.success)throw new Error(payload.message||'Accounting setup unavailable.');
 return payload.data;
}
