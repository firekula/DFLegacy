"use strict";

let diagnosticPage = 1;
const diagnosticForm = document.getElementById("diagnosticFilters");
const diagnosticSummary = document.getElementById("diagnosticSummary");
const diagnosticGroups = document.getElementById("diagnosticGroups");
const diagnosticDetail = document.getElementById("diagnosticDetail");
const diagnosticActions = document.getElementById("diagnosticActions");

const diagnosticStatusLabels = { new: "新问题", investigating: "处理中", resolved: "已处理" };

function diagnosticButton(label, action, className = "secondary-button") {
  const button = document.createElement("button");
  button.type = "button";
  button.className = className;
  button.textContent = label;
  button.addEventListener("click", async () => {
    button.disabled = true;
    try { await action(); } catch (error) { showToast(errorMessage(error), true); }
    finally { button.disabled = false; }
  });
  return button;
}

function diagnosticCell(row, text, title) {
  const cell = document.createElement("td");
  cell.textContent = text;
  if (title) cell.title = title;
  row.append(cell);
  return cell;
}

function diagnosticQuery() {
  const query = new URLSearchParams();
  for (const [key, value] of new FormData(diagnosticForm)) {
    if (value) query.set(key, key === "from" || key === "to" ? new Date(value).toISOString() : value);
  }
  return query;
}

async function saveDiagnosticStatus(groupId, status, select) {
  await api("/api/admin/diagnostics/groups/" + encodeURIComponent(groupId), {
    method: "PATCH", body: JSON.stringify({ status })
  });
  showToast("已标记为" + diagnosticStatusLabels[status]);
}

function diagnosticStatusSelect(group) {
  const select = document.createElement("select");
  select.className = "diagnostic-status";
  select.setAttribute("aria-label", "处理状态");
  for (const [value, label] of Object.entries(diagnosticStatusLabels)) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = label;
    option.selected = value === group.status;
    select.append(option);
  }
  select.addEventListener("change", () => {
    select.disabled = true;
    saveDiagnosticStatus(group.id, select.value, select)
      .catch(error => { select.value = group.status; showToast(errorMessage(error), true); })
      .finally(() => { select.disabled = false; });
  });
  return select;
}

async function loadDiagnostics() {
  const query = diagnosticQuery();
  query.set("page", String(diagnosticPage));
  query.set("pageSize", "25");
  const result = await api("/api/admin/diagnostics/groups?" + query);
  diagnosticGroups.replaceChildren();
  diagnosticSummary.textContent = `${result.total} 个故障组，第 ${result.page} 页；共 ${result.items.reduce((sum, group) => sum + group.reportCount, 0)} 份报告`;
  document.getElementById("diagnosticPrevious").disabled = diagnosticPage <= 1;
  document.getElementById("diagnosticNext").disabled = diagnosticPage * result.pageSize >= result.total;
  for (const group of result.items) {
    const row = document.createElement("tr");
    diagnosticCell(row, group.kind);
    diagnosticCell(row, group.module);
    diagnosticCell(row, group.clientVersion.split("+")[0], group.clientVersion);
    diagnosticCell(row, String(group.occurrences));
    diagnosticCell(row, String(group.reportCount));
    const statusCell = document.createElement("td");
    statusCell.append(diagnosticStatusSelect(group));
    row.append(statusCell);
    diagnosticCell(row, new Date(group.lastSeen).toLocaleString());
    const actions = document.createElement("td");
    actions.className = "diagnostic-actions";
    let expanded = null;
    actions.append(diagnosticButton("展开报告", () => {
      if (expanded) { expanded.remove(); expanded = null; return; }
      expanded = document.createElement("tr");
      const cell = document.createElement("td");
      cell.colSpan = 8;
      cell.className = "diagnostic-reports";
      cell.textContent = group.reportIds.length === 0 ? "没有报告。" : "";
      for (const id of group.reportIds) {
        cell.append(diagnosticButton(id.slice(0, 8), () => showDiagnostic(id)));
      }
      expanded.append(cell);
      row.after(expanded);
    }));
    row.append(actions);
    diagnosticGroups.append(row);
  }
}

function diagnosticField(name, value) {
  const row = document.createElement("div");
  const label = document.createElement("strong");
  label.textContent = name + "：";
  const text = document.createElement("span");
  text.textContent = value ?? "—";
  row.append(label, text);
  return row;
}

async function showDiagnostic(id) {
  const path = "/api/admin/diagnostics/reports/" + encodeURIComponent(id);
  const detail = await api(path);
  const report = detail.report;
  const system = report.system || {};
  diagnosticDetail.replaceChildren(
    diagnosticField("回执", detail.receiptId),
    diagnosticField("状态", diagnosticStatusLabels[detail.status] || detail.status),
    diagnosticField("类型", report.kind),
    diagnosticField("证据", report.evidence),
    diagnosticField("阶段", report.stage),
    diagnosticField("客户端版本", report.clientVersion),
    diagnosticField("模块", `${report.module} ${report.moduleOffset}`),
    diagnosticField("异常码", report.exceptionCode),
    diagnosticField("发生时间", new Date(report.occurredAt).toLocaleString()),
    diagnosticField("系统", `${system.osVersion || "unknown"} / ${system.gpuModel || "unknown"}`),
    diagnosticField("说明", report.context)
  );
  const raw = document.createElement("details");
  const summary = document.createElement("summary");
  summary.textContent = "原始 JSON";
  const pre = document.createElement("pre");
  pre.textContent = JSON.stringify(detail, null, 2);
  raw.append(summary, pre);
  diagnosticDetail.append(raw);
  diagnosticActions.replaceChildren(
    diagnosticButton("下载脱敏 JSON", async () => {
      // api() carries the same login/refresh handling as the rest of this page.
      const reportBody = await api(path + "/download");
      const url = URL.createObjectURL(new Blob([JSON.stringify(reportBody, null, 2)], { type: "application/json" }));
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

async function exportDiagnostics() {
  const query = diagnosticQuery();
  const response = await fetch("/api/admin/diagnostics/export?" + query, { headers: authHeaders() });
  if (!response.ok) {
    if (response.status === 413) throw new Error("报告太多，请收窄筛选后再导出。");
    if (response.status === 401) throw new Error("登录已过期，请重新登录。");
    throw new Error("导出失败：HTTP " + response.status);
  }
  const disposition = response.headers.get("content-disposition") || "";
  const match = /filename="?([^";]+)"?/i.exec(disposition);
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = match ? match[1] : "diagnostics.zip";
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  showToast("已导出当前筛选的故障报告");
}

document.getElementById("diagnosticExport").addEventListener("click", event => {
  const button = event.currentTarget;
  button.disabled = true;
  exportDiagnostics()
    .catch(error => showToast(errorMessage(error), true))
    .finally(() => { button.disabled = false; });
});
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
