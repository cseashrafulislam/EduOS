const cents=value=>{
 const s=String(value??'').trim()||'0';
 if(!/^(0|[1-9]\d{0,11})(\.\d{1,2})?$/.test(s))throw new Error('Amounts need at most two decimals and twelve integer digits.');
 const parts=s.split('.');
 return BigInt(parts[0])*100n+BigInt((parts[1]||'').padEnd(2,'0'));
};
const money=n=>(n/100n).toString()+'.'+(n%100n).toString().padStart(2,'0');
export async function setupJournalForm(ctx){
 if(!ctx.writable)return;
 const $=ctx.get, form=$('journalForm'), body=$('journalLineRows');
 let key=crypto.randomUUID();
 const periods=await ctx.setup('/periods');
 ctx.periods=periods.filter(p=>!p.isClosed);
 const periodSelect=$('journalPeriod');
 periodSelect.add(new Option('Select period',''));
 ctx.periods.forEach(p=>periodSelect.add(new Option(p.name+' ('+p.startDate+' to '+p.endDate+')',String(p.id))));
 let page=1,pages=1;
 do{
  const data=await ctx.setup('/accounts?'+new URLSearchParams({page,pageSize:100}));
  ctx.accounts.push(...(data.items||[]).filter(a=>a.isActive&&!a.isGroup));
  pages=data.totalPages||1;
  if(pages>100)throw new Error('Too many accounts; narrow the chart of accounts.');
  page++;
 }while(page<=pages);
 if(!ctx.accounts.length)ctx.notify('Create active posting accounts before journals.',true);
 function addLine(){
  if(body.children.length>=100)throw new Error('Maximum 100 lines.');
  const tr=document.createElement('tr');
  tr.innerHTML='<td><select class="form-select form-select-sm jl-account" required></select></td><td><input class="form-control form-control-sm jl-debit" type="text" inputmode="decimal" value="0" required /></td><td><input class="form-control form-control-sm jl-credit" type="text" inputmode="decimal" value="0" required /></td><td><input class="form-control form-control-sm jl-description" maxlength="500" /></td><td><button class="btn btn-sm btn-outline-danger jl-remove" type="button">Remove</button></td>';
  const select=tr.querySelector('select');
  select.add(new Option('Select account',''));
  ctx.accounts.forEach(a=>select.add(new Option(a.code+' - '+a.name,String(a.id))));
  body.appendChild(tr);
 }
 function totals(){
  let debit=0n,credit=0n;
  for(const row of body.children){
   debit+=cents(row.querySelector('.jl-debit').value);
   credit+=cents(row.querySelector('.jl-credit').value);
  }
  $('journalBalance').textContent='Debit '+money(debit)+' / Credit '+money(credit)+(debit===credit&&debit>0n?' ✓':' (not balanced)');
  return {debit,credit};
 }
 $('journalDate').value=ctx.today();
 $('journalAddLine').addEventListener('click',()=>ctx.run(async()=>{addLine();totals();}));
 body.addEventListener('click',e=>{if(e.target.closest('.jl-remove')&&body.children.length>2){e.target.closest('tr').remove();ctx.run(async()=>totals());}});
 body.addEventListener('input',()=>ctx.run(async()=>totals()));
 addLine();addLine();totals();
 form.addEventListener('submit',e=>{e.preventDefault();ctx.run(async()=>{
  const date=$('journalDate').value, periodId=Number(periodSelect.value);
  const period=ctx.periods.find(p=>p.id===periodId);
  if(!period||date<period.startDate||date>period.endDate)throw new Error('Choose an open period containing the journal date.');
  const lines=[...body.children].map((row,index)=>{
   const debit=cents(row.querySelector('.jl-debit').value);
   const credit=cents(row.querySelector('.jl-credit').value);
   if((debit>0n)===(credit>0n))throw new Error('Each line must have either debit or credit.');
   const accountId=Number(row.querySelector('.jl-account').value);
   if(!ctx.accounts.some(a=>a.id===accountId))throw new Error('Choose an active account for every line.');
   return {lineNo:index+1,accountId,debitAmount:Number(money(debit)),creditAmount:Number(money(credit)),description:row.querySelector('.jl-description').value.trim()};
  });
  const sum=totals();
  if(sum.debit<=0n||sum.debit!==sum.credit)throw new Error('Debits and credits must balance.');
  if(!confirm('Create this balanced journal draft?'))return;
  $('journalSave').disabled=true;
  try{
   const result=await ctx.api('', 'POST',{clientRequestId:key,journalDate:date,accountingPeriodId:periodId,description:$('journalDescription').value.trim(),lines});
   key=crypto.randomUUID();form.reset();body.replaceChildren();addLine();addLine();$('journalDate').value=ctx.today();totals();
   await ctx.reload();await ctx.open(result.reference);ctx.notify('Draft journal created.');
  }finally{$('journalSave').disabled=false;}
 });});
}
