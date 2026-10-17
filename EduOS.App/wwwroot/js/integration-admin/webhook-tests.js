document.querySelectorAll("[data-confirm-webhook]").forEach(form=>{
 form.addEventListener("submit",event=>{
  if(!confirm("Send a signed webhook test to the configured endpoint?"))event.preventDefault();
 });
});