(() => {
"use strict";
const root = document.getElementById("institutionTypeAdministration");
if (!root) return;
const rows = document.getElementById("catalogRows");
const status = document.getElementById("catalogMessage");
const refresh = document.getElementById("catalogRefresh");
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
function render(items) {
    rows.replaceChildren();
    if (!Array.isArray(items) || !items.length) {
        const tr = document.createElement("tr"); const td = cell(tr, "No institution types found."); td.colSpan = 5; rows.append(tr); return;
    }
    for (const item of items) {
        const tr = document.createElement("tr");
        cell(tr, item.name); cell(tr, item.code); cell(tr, item.academicCycleType);
        cell(tr, item.isActive ? "Available" : "Hidden");
        const action = cell(tr, "");
        const button = document.createElement("button");
        button.type = "button"; button.className = "btn btn-outline-primary btn-sm";
        button.textContent = item.isActive ? "Hide from signup" : "Enable signup";
        button.disabled = !item.rowVersion;
        button.addEventListener("click", async () => {
            if (busy || !confirm((item.isActive ? "Hide " : "Enable ") + item.name + " for new registrations?")) return;
            busy = true; button.disabled = true; refresh.disabled = true;
            try {
                await api("/api/platform/catalog/institution-types/" + encodeURIComponent(item.id) + "/activation", "POST", {
                    requestId: crypto.randomUUID(), isActive: !item.isActive, rowVersion: item.rowVersion
                });
                status.textContent = "Registration availability updated.";
                await load();
            } catch (error) {
                status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
            } finally { busy = false; button.disabled = false; refresh.disabled = false; }
        });
        action.append(button); rows.append(tr);
    }
}
async function load() {
    refresh.disabled = true;
    try { render(await api("/api/platform/catalog/institution-types")); status.textContent = "Catalog loaded."; }
    catch (error) { status.textContent = error.message; }
    finally { if (!busy) refresh.disabled = false; }
}
refresh.addEventListener("click", () => { if (!busy) load(); });
load();
})();
