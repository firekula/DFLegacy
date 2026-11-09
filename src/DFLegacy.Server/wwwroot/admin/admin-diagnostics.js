"use strict";

viewCopy.diagnostics = ["客户端故障", "查看脱敏基本报告及处理状态"];
let diagnosticPage = 1;
const diagnosticForm = document.getElementById("diagnosticFilters");
const diagnosticSummary = document.getElementById("diagnosticSummary");
const diagnosticGroups = document.getElementById("diagnosticGroups");
const diagnosticDetail = document.getElementById("diagnosticDetail");
const diagnosticActions = document.getElementById("diagnosticActions");

function diagnosticButton(label, action) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "secondary-button";
  button.textContent = label;
  button.addEventListener("click", async () => {
    button.disabled = true;
    try { await action(); } catch (error) { showToast(errorMessage(error), true); }
    finally { button.disabled = false; }
  });
  return button;
}

async function loadDiagnostics() {
  const query = new URLSearchParams({ page: String(diagnosticPage), pageSize: "25" });
  for (const [key, value] of new FormData(diagnosticForm)) {
    if (value) query.set(key, key === "from" || key === "to" ? new Date(value).toISOString() : value);
  }
  const result = await api("/api/admin/diagnostics/groups?" + query);
  diagnosticGroups.replaceChildren();
  diagnosticSummary.textContent = `${result.total} 个故障组，第 ${result.page} 页`;
  document.getElementById("diagnosticPrevious").disabled = diagnosticPage <= 1;
  document.getElementById("diagnosticNext").disabled = diagnosticPage * result.pageSize >= result.total;
  for (const group of result.items) {
    const section = document.createElement("section");
    const heading = document.createElement("p");
    heading.textContent = `${group.kind} · ${group.module} · ${group.occurrences} 个合并事故 / ${group.reportCount} 份报告 · ${group.clientVersion} · ${group.evidence} · ${group.status} · ${new Date(group.lastSeen).toLocaleString()}`;
    section.append(heading);
    const fingerprint = document.createElement("code"); fingerprint.textContent = group.id; section.append(fingerprint);
    for (const [value, label] of [["new", "新问题"], ["investigating", "处理中"], ["resolved", "已处理"]]) {
      section.append(diagnosticButton(label, async () => {
        await api("/api/admin/diagnostics/groups/" + encodeURIComponent(group.id), { method: "PATCH", body: JSON.stringify({ status: value }) });
        await loadDiagnostics();
      }));
    }
    for (const id of group.reportIds) section.append(diagnosticButton("查看 " + id.slice(0, 8), () => showDiagnostic(id)));
    diagnosticGroups.append(section);
  }
}

async function showDiagnostic(id) {
  const path = "/api/admin/diagnostics/reports/" + encodeURIComponent(id);
  const detail = await api(path);
  diagnosticDetail.textContent = JSON.stringify(detail, null, 2);
  diagnosticActions.replaceChildren(
    diagnosticButton("下载脱敏 JSON", async () => {
      // api() carries the same login/refresh handling as the rest of this page.
      const report = await api(path + "/download");
      const url = URL.createObjectURL(new Blob([JSON.stringify(report, null, 2)], { type: "application/json" }));
      const link = document.createElement("a"); link.href = url; link.download = "diagnostic-" + id + ".json"; link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    }),
    diagnosticButton("删除报告", async () => {
      if (!confirm("确认删除这份故障报告？删除后无法恢复。")) return;
      await api(path, { method: "DELETE" });
      diagnosticDetail.textContent = "报告已删除。";
      diagnosticActions.replaceChildren();
      await loadDiagnostics();
    })
  );
}

diagnosticForm.addEventListener("submit", event => {
  event.preventDefault(); diagnosticPage = 1;
  loadDiagnostics().catch(error => showToast(errorMessage(error), true));
});
document.getElementById("diagnosticPrevious").addEventListener("click", () => {
  diagnosticPage = Math.max(1, diagnosticPage - 1); loadDiagnostics().catch(error => showToast(errorMessage(error), true));
});
document.getElementById("diagnosticNext").addEventListener("click", () => {
  diagnosticPage++; loadDiagnostics().catch(error => showToast(errorMessage(error), true));
});
