(() => {
"use strict";
const root = document.getElementById("subscriptionPlanAdministration");
if (!root) return;
const rows = document.getElementById("planRows");
const status = document.getElementById("planMessage");
const refresh = document.getElementById("planRefresh");
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content || "";
let busy = false;
const cell = (tr, value) => { const td = document.createElement("td"); td.textContent = value; tr.append(td); return td; };
async function api(path, method = "GET", body) {
    const headers = { Accept: "application/json" };
    const options = { method, credentials: "same-origin", cache: "no-store", headers };
    if (method !== "GET") {
        headers["Content-Type"] = "application/json";
        headers["RequestVerificationToken"] = token;
        options.body = JSON.stringify(body);
    }
    const response = await fetch(path, options);
    const payload = await response.json().catch(() => null);
    if (!response.ok || !payload?.success) {
        const error = new Error(payload?.message || "Request failed.");
        error.status = response.status;
        throw error;
    }
    return payload.data;
}
async function change(item, isActive, isPublic) {
    if (busy || !confirm("Update availability for " + item.name + "? Existing subscriptions will not be changed.")) return;
    busy = true;
    refresh.disabled = true;
    try {
        await api("/api/platform/subscription-plans/" + encodeURIComponent(item.id) + "/availability", "POST", {
            requestId: crypto.randomUUID(), isActive, isPublic, rowVersion: item.rowVersion
        });
        status.textContent = "Plan availability updated.";
        await load();
    } catch (error) {
        status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
    } finally { busy = false; refresh.disabled = false; }
}
function render(items) {
    rows.replaceChildren();
    if (!Array.isArray(items) || !items.length) {
        const tr = document.createElement("tr"); const td = cell(tr, "No plans found."); td.colSpan = 7; rows.append(tr); return;
    }
    for (const item of items) {
        const tr = document.createElement("tr");
        cell(tr, item.name); cell(tr, item.code);
        cell(tr, String(item.monthlyPrice) + " " + item.currencyCode);
        cell(tr, String(item.yearlyPrice) + " " + item.currencyCode);
        cell(tr, item.isActive ? "Accepting subscriptions" : "Inactive");
        cell(tr, item.isPublic ? "Visible" : "Hidden");
        const actions = cell(tr, "");
        const activeButton = document.createElement("button");
        activeButton.type = "button"; activeButton.className = "btn btn-outline-primary btn-sm me-2";
        activeButton.textContent = item.isActive ? "Deactivate" : "Activate";
        activeButton.disabled = !item.rowVersion;
        activeButton.addEventListener("click", () => change(item, !item.isActive, item.isPublic));
        const publicButton = document.createElement("button");
        publicButton.type = "button"; publicButton.className = "btn btn-outline-secondary btn-sm";
        publicButton.textContent = item.isPublic ? "Hide pricing" : "Publish pricing";
        publicButton.disabled = !item.rowVersion;
        publicButton.addEventListener("click", () => change(item, item.isActive, !item.isPublic));
        actions.append(activeButton, publicButton); rows.append(tr);
    }
}
async function load() {
    refresh.disabled = true;
    try { render(await api("/api/platform/subscription-plans")); status.textContent = "Plans loaded."; }
    catch (error) { status.textContent = error.message; }
    finally { if (!busy) refresh.disabled = false; }
}
refresh.addEventListener("click", () => { if (!busy) load(); });
load();
})();
