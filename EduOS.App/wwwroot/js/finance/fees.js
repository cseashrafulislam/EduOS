(() => {
 'use strict';const root=document.getElementById('feeApp');if(!root)return;
 const el=id=>document.getElementById(id),state={options:null,ledger:null,invoice:null,generation:null,generationKey:null,payment:null,paymentKey:null};
 const opt=(value,label)=>{const x=document.createElement('option');x.value=String(value);x.textContent=label;return x;};
 const td=value=>{const x=document.createElement('td');x.textContent=value==null?'':String(value);return x;};
 const date=value=>value?String(value).slice(0,10):'';
 function msg(type,text){const x=el('feeAlert');x.className='alert alert-'+type;x.textContent=text;x.focus();}
 async function api(url,method='GET',body){
   const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json',...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
   const data=await response.json().catch(()=>null);if(!response.ok||!data?.success)throw new Error(data?.message||'Request failed.');return data.data;
 }
 function options(id,items,empty){
   const box=el(id),prev=box.value;box.replaceChildren(opt('',empty));
   for(const row of items)box.append(opt(row.id,row.name));
   if(items.some(x=>String(x.id)===prev))box.value=prev;
 }
 async function init(){
   const now=new Date();el('feeMonth').value=String(now.getMonth()+1);el('feeBillingYear').value=String(now.getFullYear());
   el('feeDueDate').value=now.getFullYear()+'-'+String(now.getMonth()+1).padStart(2,'0')+'-28';
   try{const data=await api('/api/finance/fees/options');state.options=data;
     options('feeYear',data.academicYears,'Choose academic year');options('feeLevel',data.academicLevels,'Choose academic level');
     options('feeHead',data.feeHeads,'Choose fee head');options('feeBatch',data.academicBatches.map(x=>({...x,name:x.name+' / Year '+x.academicYearId})),'Choose active batch');
   }catch(e){msg('danger',e.message);}
 }
 async function saveStructure(e){
   e.preventDefault();if(!e.currentTarget.reportValidity())return;
   const amount=Number(el('feeAmount').value);if(!Number.isFinite(amount)||amount<0)return msg('danger','Invalid fee amount.');
   if(!confirm('Save this fee amount? Existing invoiced transactions are not recalculated.'))return;
   const btn=el('feeSaveStructure');btn.disabled=true;
   try{await api('/api/finance/fees/structure','PUT',{academicYearId:Number(el('feeYear').value),
     classId:Number(el('feeLevel').value),feeHeadId:Number(el('feeHead').value),amount});msg('success','Fee structure saved.');}
   catch(e){msg('danger',e.message);}finally{btn.disabled=false;}
 }
 async function generate(e){
   e.preventDefault();if(!e.currentTarget.reportValidity())return;
   const batch=state.options?.academicBatches.find(x=>String(x.id)===el('feeBatch').value);
   if(!batch)return msg('danger','Select an active batch.');
   const month=Number(el('feeMonth').value),year=Number(el('feeBillingYear').value),dueDate=el('feeDueDate').value;
   const sig=[batch.id,month,year,dueDate].join(':');
   if(state.generationKey!==sig){state.generation=crypto.randomUUID();state.generationKey=sig;}
   if(!confirm('Generate '+month+'/'+year+' invoices for '+batch.name+'? This is a financial operation.'))return;
   const btn=el('feeGenerate');btn.disabled=true;
   try{const result=await api('/api/finance/fees/invoices/generate','POST',{clientRequestId:state.generation,
     academicYearId:batch.academicYearId,classId:batch.academicLevelId,sectionId:batch.id,month,year,dueDate:dueDate+'T00:00:00'});
     el('feeGenerationInfo').textContent='Generated: '+result.generated+'; existing: '+result.existing;
     state.generation=null;state.generationKey=null;msg('success','Invoice generation completed.');}
   catch(e){msg('danger',e.message+' Request ID retained for a safe retry.');}
   finally{btn.disabled=false;}
 }
 async function findStudent(){
   const search=el('feeStudentSearch').value.trim();
   if(search.length<2||search.length>100)return msg('warning','Search requires 2–100 characters.');
   try{const rows=await api('/api/finance/fees/students/search?search='+encodeURIComponent(search));
     options('feeStudent',(rows||[]).map(x=>({id:x.reference,name:x.studentCode+' — '+x.name})),'Choose student');
     if(rows?.length===25)msg('warning','Showing first 25 matching students. Refine the search.');}
   catch(e){msg('danger',e.message);}
 }
 async function ledger(){
   const ref=el('feeStudent').value;if(!ref)return msg('warning','Select a student.');
   try{state.ledger=await api('/api/finance/fees/students/'+encodeURIComponent(ref)+'/ledger');
     el('feeLedgerSummary').textContent=state.ledger.studentName+' ('+state.ledger.studentCode+') — Billed: '+state.ledger.totalBilled+
        ', Paid: '+state.ledger.totalPaid+', Due: '+state.ledger.totalDue+' BDT';
     const body=el('feeInvoices');body.replaceChildren();
     for(const row of state.ledger.invoices||[]){
       const tr=document.createElement('tr'),action=document.createElement('td');
       if(Number(row.dueAmount)>0){
         const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary';b.textContent='Collect cash';b.onclick=()=>selectInvoice(row);action.append(b);
       }
       tr.append(td(row.invoiceNumber),td(date(row.invoiceDate)),td(date(row.dueDate)),td(row.totalAmount),td(row.paidAmount),td(row.dueAmount),action);body.append(tr);
     }
     if(!body.childNodes.length){const tr=document.createElement('tr'),c=td('No invoices.');c.colSpan=7;tr.append(c);body.append(tr);}
   }catch(e){msg('danger',e.message);}
 }
 function selectInvoice(row){
   state.invoice=row;el('feePaymentPanel').classList.remove('d-none');el('feePaymentInvoice').value=row.invoiceNumber;
   el('feePaymentAmount').max=String(row.dueAmount);el('feePaymentAmount').value=String(row.dueAmount);
   state.payment=null;state.paymentKey=null;el('feePaymentAmount').focus();
 }
 async function collect(e){
   e.preventDefault();if(!state.invoice||!e.currentTarget.reportValidity())return;
   const amount=Number(el('feePaymentAmount').value),invoice=state.invoice;
   if(!Number.isFinite(amount)||amount<=0||amount>Number(invoice.dueAmount))return msg('danger','Cash amount exceeds outstanding balance.');
   const sig=invoice.reference+':'+amount;if(state.paymentKey!==sig){state.payment=crypto.randomUUID();state.paymentKey=sig;}
   if(!confirm('Record '+amount+' BDT cash payment against '+invoice.invoiceNumber+'?'))return;
   el('feeCollect').disabled=true;
   try{const payment=await api('/api/finance/fees/payments','POST',{clientRequestId:state.payment,invoiceReference:invoice.reference,
     amount,paymentMethod:'Cash',transactionId:null,note:null,bankAccountId:null,invoiceRowVersion:invoice.rowVersion});
     state.payment=null;state.paymentKey=null;el('feePaymentPanel').classList.add('d-none');
     await ledger();msg('success','Cash receipt recorded: '+payment.receiptNumber);}
   catch(e){msg('danger',e.message+' If status is uncertain, retry using the unchanged form.');}
   finally{el('feeCollect').disabled=false;}
 }
 el('feeStructureForm').addEventListener('submit',saveStructure);
 el('feeInvoiceForm').addEventListener('submit',generate);
 el('feeFindStudent').addEventListener('click',findStudent);
 el('feeLoadLedger').addEventListener('click',ledger);
 el('feePaymentForm').addEventListener('submit',collect);
 init();
})();
