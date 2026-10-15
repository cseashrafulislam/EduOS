document.addEventListener("DOMContentLoaded", () => {
  const table = document.getElementById("domainRows");
  if (!table) return;
  const token = document.querySelector('meta[name="request-verification-token"]')?.content;
  const notice = document.getElementById("domainNotice");
  async function send(path, method = "GET", body) {
    const headers = { Accept: "application/json" };
    if (method !== "GET") {
      headers["Content-Type"] = "application/json";
      headers.RequestVerificationToken = token;
    }
    const response = await fetch("/api/tenant-domains" + path, {
      method, headers, credentials: "same-origin",
      body: body ? JSON.stringify(body) : undefined
    });
    const data = await response.json();
    if (!response.ok || !data.success) throw Error(data.message || "Request failed");
    return data.data;
  }
  async function refresh() {
    const domains = await send("");
    table.replaceChildren();
    for (const domain of domains) {
      const row = document.createElement("tr");
      for (const value of [domain.hostName, domain.verificationStateCode,
        domain.isActive ? "Yes" : "No", domain.isPrimary ? "Yes" : "No",
        domain.verifiedAt || "-"]) {
        const cell = document.createElement("td");
        cell.textContent = value;
        row.append(cell);
      }
      const cell = document.createElement("td");
      for (const action of ["verify", "primary", "deactivate"]) {
        const button = document.createElement("button");
        button.type = "button"; button.textContent = action;
        button.className = "btn btn-sm btn-outline-primary";
        button.addEventListener("click", async () => {
          button.disabled = true;
          try {
            await send("/" + domain.id + "/" + action, "POST", { rowVersion: domain.rowVersion });
            await refresh();
          } catch (error) { notice.textContent = error.message; }
          finally { button.disabled = false; }
        });
        cell.append(button);
      }
      row.append(cell); table.append(row);
    }
  }
  document.getElementById("domainRegistration").addEventListener("submit", async event => {
    event.preventDefault();
    try {
      const data = await send("", "POST", {
        hostName: document.getElementById("domainHost").value.trim(),
        clientRequestId: crypto.randomUUID()
      });
      document.getElementById("domainRecordName").textContent = data.verificationRecordName;
      document.getElementById("domainRecordValue").textContent = data.verificationRecordValue;
      document.getElementById("domainChallenge").hidden = false;
      await refresh();
    } catch (error) { notice.textContent = error.message; }
  });
  document.getElementById("domainRefresh").addEventListener("click", refresh);
  refresh().catch(error => notice.textContent = error.message);
});