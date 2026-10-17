(() => {
"use strict";
const $ = id => document.getElementById(id);
let page = 1, pages = 1, version = 0;
const notice = (message, success = false) => {
 const el = $("featureAuditNotice");
 el.textContent = message;
 el.className = "alert alert-" + (success ? "success" : "danger");
};
const cell = (tr, value) => {
 const td = document.createElement("td");
 td.textContent = value == null ? "" : String(value);
 tr.append(td);
};
async function load() {
 const requestVersion = ++version, requestedPage = page;
 const response = await fetch("/api/tenant-feature-audit?page=" + requestedPage + "&pageSize=20",
   { credentials: "same-origin", cache: "no-store", headers: { "Accept": "application/json" } });
 const result = await response.json();
 if (!response.ok || !result.success) throw Error(result.message || "Could not load feature audit.");
 if (requestVersion !== version || requestedPage !== page) return;
 const data = result.data, tbody = $("featureAuditRows");
 tbody.replaceChildren();
 for (const row of data.items || []) {
   const tr = document.createElement("tr");
   cell(tr, row.featureName); cell(tr, row.featureCode);
   cell(tr, row.includedInPlan ? "Yes" : "No");
   cell(tr, row.hasOverride ? "Yes" : "No");
   cell(tr, row.restrictedInConfiguration ? "Restricted in configuration" :
     row.hasOverride && !row.overrideCurrentlyEffective ? "Override outside effective period" : "No active restriction record");
   cell(tr, row.reason || "—");
   tbody.append(tr);
 }
 pages = data.totalPages || 1;
 $("featureAuditPage").textContent = "Page " + page + " / " + pages;
 $("featureAuditPrev").disabled = page <= 1;
 $("featureAuditNext").disabled = page >= pages;
}
function refresh() { load().catch(e => notice(e.message)); }
$("featureAuditPrev").addEventListener("click", () => { if (page > 1) { page--; refresh(); } });
$("featureAuditNext").addEventListener("click", () => { if (page < pages) { page++; refresh(); } });
refresh();
})();
