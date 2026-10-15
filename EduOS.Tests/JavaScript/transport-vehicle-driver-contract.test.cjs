const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const base='EduOS.App/wwwroot/js/transport-operations/';
test('vehicle-driver scripts parse and remain in tenant-admin view',()=>{
 for(const name of ['core','options','list','create','init']){
  const file=path.join(root,base+'vehicle-driver-'+name+'.js');
  const result=spawnSync(process.execPath,['--check',file],{encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);
  assert.match(read('EduOS.App/Views/TransportOperations/Index.cshtml'),
   new RegExp('vehicle-driver-'+name+'\\.js'));
 }
});
test('vehicle-driver browser uses CSRF, version tokens and safe DOM writes',()=>{
 assert.match(read(base+'vehicle-driver-core.js'),/RequestVerificationToken/);
 assert.match(read(base+'vehicle-driver-list.js'),/textContent/);
 assert.match(read(base+'vehicle-driver-list.js'),/rowVersion/);
 assert.match(read(base+'vehicle-driver-create.js'),/driverRowVersion/);
 assert.match(read(base+'vehicle-driver-create.js'),/vehicleRowVersion/);
 assert.doesNotMatch(read(base+'vehicle-driver-list.js'),/innerHTML/);
});
