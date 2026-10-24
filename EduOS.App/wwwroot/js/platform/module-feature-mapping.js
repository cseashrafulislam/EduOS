(() => {
"use strict";
const root = document.getElementById("platformModuleFeatures");
if (!root) return;
const moduleSelect = document.getElementById("moduleFeatureModule");
const rows = document.getElementById("moduleFeatureRows");
const status = document.getElementById("moduleFeatureStatus");
const refresh = document.getElementById("moduleFeatureRefresh");
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content || "";
let busy = false;
const cell = (tr, value) => {
    const td = document.createElement("td");
    td.textContent = value ?? "";
    tr.append(td);
    return td;
};
async function api(path, method = "GET", body) {
    const headers = { Accept: "application/json" };
    const options = { method, headers, credentials: "same-origin", cache: "no-store" };
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
function render(directory) {
    rows.replaceChildren();
    for (const item of directory.features || []) {
        const tr = document.createElement("tr");
        cell(tr, item.featureName + " (" + item.featureCode + ")");
        cell(tr, item.isActive ? "Yes" : "No");
        const checkCell = cell(tr, "");
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.className = "form-check-input";
        checkbox.checked = item.isMapped;
        checkbox.disabled = (directory.isCore && item.isMapped) ||
            ((!directory.isActive || !item.isActive) && !item.isMapped);
        checkbox.setAttribute("aria-label", "Mapped " + item.featureName);
        checkCell.append(checkbox);
        const action = cell(tr, "");
        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn btn-outline-primary btn-sm";
        button.textContent = "Save";
        button.disabled = checkbox.disabled;
        button.addEventListener("click", async () => {
            if (busy || !confirm("Save module feature mapping? Existing plan entitlements are not edited.")) return;
            busy = true;
            button.disabled = true;
            refresh.disabled = true;
            moduleSelect.disabled = true;
            const requestId = crypto.randomUUID();
            try {
                await api("/api/platform/catalog/module-features/" +
                    encodeURIComponent(directory.moduleId) + "/features/" + encodeURIComponent(item.featureId),
                    "POST", {
                        requestId, moduleRowVersion: directory.moduleRowVersion,
                        featureRowVersion: item.featureRowVersion,
                        mappingRowVersion: item.mappingRowVersion, isMapped: checkbox.checked
                    });
                status.textContent = "Mapping saved.";
                await load();
            } catch (error) {
                status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
            } finally {
                busy = false;
                refresh.disabled = false;
                moduleSelect.disabled = false;
                button.disabled = checkbox.disabled;
            }
        });
        action.append(button);
        rows.append(tr);
    }
    if (!directory.features?.length) {
        const tr = document.createElement("tr");
        cell(tr, "No features found.").colSpan = 4;
        rows.append(tr);
    }
}
async function load() {
    if (!moduleSelect.value) {
        rows.replaceChildren();
        const tr = document.createElement("tr");
        cell(tr, "Choose a product module.").colSpan = 4;
        rows.append(tr);
        return;
    }
    refresh.disabled = true;
    try {
        const directory = await api("/api/platform/catalog/module-features/" +
            encodeURIComponent(moduleSelect.value));
        render(directory);
        status.textContent = "Mappings loaded for " + directory.moduleName + ".";
    } catch (error) {
        status.textContent = error.message;
    } finally { refresh.disabled = false; }
}
async function initialize() {
    try {
        const modules = await api("/api/platform/catalog/modules");
        for (const module of modules || []) {
            const option = document.createElement("option");
            option.value = module.id;
            option.textContent = module.name + " (" + module.code + ")";
            moduleSelect.append(option);
        }
        if (moduleSelect.options.length > 1) {
            moduleSelect.selectedIndex = 1;
            await load();
        }
    } catch (error) { status.textContent = error.message; }
}
moduleSelect.addEventListener("change", load);
refresh.addEventListener("click", load);
initialize();
})();
