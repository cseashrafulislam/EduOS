(() => {
"use strict";
const root = document.getElementById("platformModuleAdministration");
if (!root) return;
const rows = document.getElementById("moduleRows");
const status = document.getElementById("moduleMessage");
const refresh = document.getElementById("moduleRefresh");
const editor = document.getElementById("moduleEditor");
const form = document.getElementById("moduleForm");
const name = document.getElementById("moduleEditName");
const description = document.getElementById("moduleEditDescription");
const order = document.getElementById("moduleEditOrder");
const active = document.getElementById("moduleEditActive");
const save = document.getElementById("moduleSave");
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content || "";
let busy = false;
let selected = null;
let requestId = null;
const cell = (tr, value) => {
    const td = document.createElement("td");
    td.textContent = value ?? "";
    tr.append(td);
    return td;
};
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
function edit(item) {
    if (busy) return;
    selected = item;
    requestId = crypto.randomUUID();
    document.getElementById("moduleEditCode").textContent = item.code;
    name.value = item.name;
    description.value = item.description ?? "";
    order.value = item.displayOrder;
    active.checked = item.isActive;
    active.disabled = item.isCore;
    document.getElementById("moduleCoreWarning").hidden = !item.isCore;
    editor.hidden = false;
    name.focus();
}
function render(items) {
    rows.replaceChildren();
    if (!Array.isArray(items) || !items.length) {
        const tr = document.createElement("tr");
        cell(tr, "No product modules found.").colSpan = 6;
        rows.append(tr);
        return;
    }
    for (const item of items) {
        const tr = document.createElement("tr");
        cell(tr, item.code); cell(tr, item.name);
        cell(tr, item.isCore ? "Yes" : "No");
        cell(tr, item.displayOrder);
        cell(tr, item.isActive ? "Active" : "Inactive");
        const action = cell(tr, "");
        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn btn-outline-primary btn-sm";
        button.textContent = "Edit";
        button.disabled = !item.rowVersion;
        button.addEventListener("click", () => edit(item));
        action.append(button);
        rows.append(tr);
    }
}
async function load() {
    refresh.disabled = true;
    try {
        render(await api("/api/platform/catalog/modules"));
        status.textContent = "Product modules loaded.";
    } catch (error) {
        status.textContent = error.message;
    } finally {
        if (!busy) refresh.disabled = false;
    }
}
form.addEventListener("submit", async event => {
    event.preventDefault();
    if (busy || !selected || !form.reportValidity()) return;
    if (!confirm("Save product module configuration? Existing tenant selections are preserved.")) return;
    busy = true;
    save.disabled = true;
    refresh.disabled = true;
    try {
        await api("/api/platform/catalog/modules/" + encodeURIComponent(selected.id) + "/configuration", "POST", {
            requestId, rowVersion: selected.rowVersion, name: name.value.trim(),
            description: description.value.trim(), displayOrder: Number(order.value),
            isActive: selected.isCore ? true : active.checked
        });
        selected = null;
        requestId = null;
        editor.hidden = true;
        status.textContent = "Product module updated.";
        await load();
    } catch (error) {
        status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
    } finally {
        busy = false;
        save.disabled = false;
        refresh.disabled = false;
    }
});
document.getElementById("moduleCancel").addEventListener("click", () => {
    if (!busy) { selected = null; requestId = null; editor.hidden = true; }
});
refresh.addEventListener("click", () => {
    if (!busy) { selected = null; requestId = null; editor.hidden = true; load(); }
});
load();
})();
