"use strict";

const $ = selector => document.querySelector(selector);
const stateNames = { not_configured: "未配置", stopped: "已停止", starting: "启动中", running: "运行中", stopping: "停止中", failed: "启动失败", exited: "异常退出" };
const titles = { overview: "运行概览", configuration: "服务配置", clients: "客户端分发", logs: "运行日志" };
let csrf = "", setupMode = false, setupToken = "", savedConfig = null, loggedIn = false, currentView = "overview", busy = false;
let logAfter = 0, logLines = [], toastTimer;
let pvfResource = null, serverRunning = false;

function toast(message, error = false) {
  const element = $("#toast"); element.textContent = message; element.classList.toggle("error", error); element.hidden = false;
  clearTimeout(toastTimer); toastTimer = setTimeout(() => { element.hidden = true; }, error ? 8000 : 4500);
}

async function request(path, options = {}, binary = false) {
  const response = await fetch(path, { credentials: "same-origin", ...options,
    headers: { ...(options.body ? { "Content-Type": "application/json" } : {}), ...(csrf ? { "X-CSRF-Token": csrf } : {}), ...options.headers } });
  if (response.status === 401 && path !== "/api/login") { loggedIn = false; showAuth(); }
  if (!response.ok) {
    let message = "请求失败（" + response.status + "）";
    try { const data = await response.json(); message = data.error || message; } catch (_) { }
    throw new Error(message);
  }
  if (binary) return response;
  const body = await response.text(); return body ? JSON.parse(body) : {};
}
const post = (path, payload = {}) => request(path, { method: "POST", body: JSON.stringify(payload) });

function showAuth() {
  $("#application").hidden = true; $("#authPanel").hidden = false;
  $("#authTitle").textContent = setupMode ? "创建部署管理账号" : "连接你的服务器";
  $("#authDescription").textContent = setupMode ? "首次设置需要初始化凭据。管理密码至少 12 个字符。" : "登录后配置、运行并分发你的 DFLegacy 服务端。";
  $("#setupTokenField").hidden = !setupMode || Boolean(setupToken);
  $("#password").minLength = setupMode ? 12 : 1;
  $("#authSubmit").textContent = setupMode ? "创建账号并继续" : "登录管理器"; $("#authSubmit").disabled = false;
}

async function enterApp() {
  const session = await request("/api/session"); csrf = session.csrf; loggedIn = true;
  $("#identityName").textContent = session.userName;
  $("#password").value = ""; $("#setupToken").value = ""; setupToken = "";
  $("#authPanel").hidden = true; $("#application").hidden = false;
  await loadConfig(); await refreshStatus();
}

function switchView(view) {
  currentView = view;
  for (const section of document.querySelectorAll(".view")) section.hidden = section.id !== view;
  for (const button of document.querySelectorAll(".nav-item")) button.classList.toggle("active", button.dataset.view === view);
  $("#viewTitle").textContent = titles[view];
  if (view === "logs") refreshLogs().catch(error => toast(error.message, true));
}

async function loadConfig() {
  const snapshot = await request("/api/config"); savedConfig = snapshot.saved;
  const fields = $("#configForm").elements;
  for (const note of document.querySelectorAll("[data-environment-note]")) note.remove();
  for (const [name, value] of Object.entries(savedConfig)) {
    const input = fields.namedItem(name); if (!input) continue;
    if (input.type === "checkbox") input.checked = value; else input.value = name === "dropRatePercent" ? value / 100 : value;
    input.disabled = false;
  }
  const mapping = { fallbackRelayEnabled: "FallbackRelay__Enabled", fallbackRelayAdvertisedHost: "FallbackRelay__AdvertisedHost", fallbackRelayTcpPort: "FallbackRelay__TcpPort", fallbackRelayUdpPort: "FallbackRelay__UdpPort", pvpEnabled: "Pvp__Enabled", pvpPort: "Pvp__Port", scriptPvfPath: "ScriptPvfPath", bindHost: "Entrance__Host", adminHost: "Admin__Host", entrancePort: "Entrance__Port", channelPort: "Channel__Port", adminPort: "Admin__Port", characterPort: "CharacterDatagram__Port", gameplayPrimaryPort: "GameplayDatagram__PrimaryPort", gameplaySecondaryPort: "GameplayDatagram__SecondaryPort", channelName: "Channel__Name", maximumUsers: "Channel__MaximumUsers", monsterExperienceMultiplier: "Experience__MonsterMultiplier", clearExperienceMultiplier: "Experience__ClearMultiplier", dropRatePercent: "Drop__RatePercent", dropEconomicRate: "Drop__EconomicRate", additionalEntrancePort: "Entrance__AdditionalPorts__1" };
  for (const [name, key] of Object.entries(mapping)) {
    if ((snapshot.environmentOverrides || []).some(variable => variable.toLowerCase() === ("DFLegacy__" + key).toLowerCase())) {
      const input = fields.namedItem(name); if (!input) continue; input.disabled = true;
      if (input.type === "checkbox") input.checked = snapshot.effective[name]; else input.value = name === "dropRatePercent" ? snapshot.effective[name] / 100 : snapshot.effective[name];
      const note = document.createElement("small"); note.dataset.environmentNote = "true";
      note.textContent = "显示环境变量生效值；文件中保存的值为 " + (name === "dropRatePercent" ? savedConfig[name] / 100 + " 倍" : savedConfig[name]) + "。"; input.closest("label").append(note);
    }
  }
  $("#environmentNotice").hidden = !snapshot.environmentOverrides.length;
  $("#environmentNotice").textContent = "部分设置由环境变量控制，已锁定对应输入；有效值以环境变量为准：" + snapshot.environmentOverrides.join("、");
  $("#networkPreset").value = "custom";
  await refreshPvf();
}

async function refreshPvf() {
  pvfResource = await request("/api/resources/pvf");
  $("#pvfStatus").textContent = pvfResource.available
    ? "当前资源已就绪 · " + (pvfResource.sizeBytes / 1024 / 1024).toFixed(1) + " MiB" : "尚未上传 Script.pvf";
  $("#pvfNote").textContent = pvfResource.externallyControlled
    ? "当前资源由环境变量或容器挂载控制，请在部署环境中更换。"
    : serverRunning ? "游戏服务运行中，请先停服再替换资源。" : "选择与客户端匹配的 Script.pvf，最大 1 GiB；上传成功后自动选用。";
  $("#pvfFile").disabled = busy || serverRunning || pvfResource.externallyControlled;
  $("#uploadPvf").disabled = busy || serverRunning || pvfResource.externallyControlled || !$("#pvfFile").files.length;
}

async function refreshStatus() {
  if (!loggedIn) return;
  const status = await request("/api/status");
  const server = status.server, running = server.state === "running", pending = ["starting", "stopping"].includes(server.state);
  serverRunning = running || pending;
  $("#stateBadge").textContent = stateNames[server.state] || server.state; $("#stateBadge").className = "badge " + server.state;
  $("#metricState").textContent = stateNames[server.state] || server.state;
  $("#metricPid").textContent = server.pid ? "进程 " + server.pid : server.exitCode != null ? "退出码 " + server.exitCode : "等待启动";
  $("#metricVersion").textContent = status.version;
  $("#serverHeadline").textContent = running ? "服务器正在运行" : pending ? "正在处理服务进程" : "你的服务器工作台";
  $("#serverDescription").textContent = server.error || (server.restartRequired ? "配置已更新，重启游戏服务后应用。" : running ? "游戏服务已就绪，可打开游戏后台或分发客户端。" : status.configured ? "服务已配置，可以启动或查看上一次运行日志。" : "先完成服务配置，再创建游戏管理员并启动服务。");
  $("#start").disabled = busy || running || pending || !status.configured;
  $("#stop").disabled = busy || !running || pending; $("#restart").disabled = busy || !running || pending;
  $("#summaryHost").textContent = status.entranceHost + ":" + status.entrancePort;
  $("#summaryApi").textContent = status.accountServiceUrl; $("#summaryGameplay").textContent = status.gameplayAdvertisedHost;
  $("#summaryData").textContent = status.dataDirectory;
  $("#assetNotice").hidden = status.assetsAvailable;
  $("#exportClient").disabled = busy || !status.assetsAvailable || !status.configured;
  await refreshPvf();
  const base = status.accountServiceUrl.replace(/\/+$/, "");
  $("#gmLink").href = base + "/admin/index.html"; $("#playerLink").href = base + "/admin/player.html";
  $("#gameLinksNote").textContent = running ? "使用游戏管理员账号登录 GM 后台。" : "游戏服务当前已停止，这些页面将在启动后可用。";
}

async function refreshLogs() {
  if (!loggedIn || currentView !== "logs") return;
  const lines = await request("/api/logs?after=" + logAfter);
  if (!lines.length) return;
  logLines = logLines.concat(lines).slice(-500); logAfter = lines[lines.length - 1].id;
  const output = $("#logOutput");
  output.textContent = logLines.map(line => new Date(line.time).toLocaleTimeString() + " [" + line.source + "] " + line.text).join("\n");
  if ($("#autoScroll").checked) output.scrollTop = output.scrollHeight;
}

async function withBusy(action) {
  if (busy) return; busy = true;
  for (const id of ["start", "stop", "restart", "exportClient", "uploadPvf", "pvfFile"]) $("#" + id).disabled = true;
  try { await action(); } catch (error) { toast(error.message, true); }
  finally { busy = false; if (loggedIn) await refreshStatus().catch(error => toast(error.message, true)); }
}

$("#authForm").addEventListener("submit", async event => {
  event.preventDefault(); $("#authError").textContent = ""; $("#authSubmit").disabled = true;
  try {
    const payload = { userName: $("#userName").value.trim(), password: $("#password").value };
    if (setupMode) payload.setupToken = setupToken || $("#setupToken").value.trim();
    await post(setupMode ? "/api/setup" : "/api/login", payload); setupMode = false; await enterApp();
  } catch (error) { $("#authError").textContent = error.message; }
  finally { $("#authSubmit").disabled = false; }
});
$("#logout").addEventListener("click", async () => { try { await post("/api/logout"); } finally { loggedIn = false; csrf = ""; showAuth(); } });
for (const button of document.querySelectorAll("[data-view]")) button.addEventListener("click", () => switchView(button.dataset.view));
for (const button of document.querySelectorAll(".jump-config")) button.addEventListener("click", () => switchView("configuration"));
for (const name of ["start", "stop", "restart"]) $("#" + name).addEventListener("click", () => withBusy(async () => { await post("/api/server/" + name); toast("服务操作已完成。"); }));
$("#configForm").addEventListener("submit", event => {
  event.preventDefault(); withBusy(async () => {
    const result = { ...savedConfig };
    for (const input of $("#configForm").elements) {
      if (!input.name || input.disabled || !(input.name in result)) continue;
      result[input.name] = input.type === "checkbox" ? input.checked : input.type === "number" ? Number(input.value) : input.value.trim();
      if (input.name === "dropRatePercent") result[input.name] *= 100;
    }
    await request("/api/config", { method: "PUT", body: JSON.stringify(result) }); await loadConfig(); toast("配置已保存，重启游戏服务后应用。");
  });
});
$("#deriveApi").addEventListener("click", () => {
  const form = $("#configForm").elements, host = form.namedItem("advertisedHost").value;
  form.namedItem("accountServiceUrl").value = "http://" + (host.includes(":") ? "[" + host + "]" : host) + ":" + form.namedItem("adminPort").value;
});
$("#networkPreset").addEventListener("change", event => {
  const form = $("#configForm").elements, mode = event.target.value; if (mode === "custom") return;
  const changes = mode === "local" ? { bindHost: "127.0.0.1", adminHost: "127.0.0.1", advertisedHost: "127.0.0.1", relayEnabled: false }
    : mode === "relay" ? { bindHost: "::", adminHost: "::", relayEnabled: true } : { bindHost: "0.0.0.0", adminHost: "0.0.0.0", relayEnabled: false };
  for (const [name, value] of Object.entries(changes)) { const input = form.namedItem(name); if (input.disabled) continue; if (input.type === "checkbox") input.checked = value; else input.value = value; }
  if (mode !== "local") toast("请填写玩家可以访问的服务器 IP 或域名，并检查账号服务 URL。");
});
$("#gameAdminForm").addEventListener("submit", event => {
  event.preventDefault(); const form = event.currentTarget;
  withBusy(async () => {
    try { await post("/api/game-admin", Object.fromEntries(new FormData(form))); toast("游戏管理员已创建，可以启动服务。"); }
    finally { form.elements.namedItem("password").value = ""; form.elements.namedItem("oicqCode").value = ""; }
  });
});
$("#pvfFile").addEventListener("change", () => refreshPvf().catch(error => toast(error.message, true)));
$("#uploadPvf").addEventListener("click", () => withBusy(async () => {
  const file = $("#pvfFile").files[0];
  if (!file) throw new Error("请先选择 Script.pvf。");
  if (file.size > pvfResource.maximumBytes) throw new Error("文件超过 1 GiB 上限。");
  const progress = $("#pvfProgress"); progress.hidden = false; progress.value = 0;
  try {
    const result = await new Promise((resolve, reject) => {
      const upload = new XMLHttpRequest(); upload.open("POST", "/api/resources/pvf");
      upload.setRequestHeader("Content-Type", "application/octet-stream"); upload.setRequestHeader("X-CSRF-Token", csrf);
      upload.upload.onprogress = event => { if (event.lengthComputable) { progress.value = event.loaded / event.total * 100; progress.textContent = Math.round(progress.value) + "%"; } };
      upload.onerror = () => reject(new Error("上传连接中断，请重试；原资源已保留。"));
      upload.onabort = () => reject(new Error("上传已取消；原资源已保留。"));
      upload.onload = () => {
        let body; try { body = JSON.parse(upload.responseText); } catch (_) { body = {}; }
        if (upload.status >= 200 && upload.status < 300) resolve(body);
        else reject(new Error(body.error || "上传失败（HTTP " + upload.status + "），请重试。"));
      };
      upload.send(file);
    });
    savedConfig.scriptPvfPath = result.path; $("#pvfFile").value = "";
    await refreshPvf(); toast("资源上传成功，已自动选用；请保存其他配置后启动。");
  } finally { progress.hidden = true; }
}));
$("#exportClient").addEventListener("click", () => withBusy(async () => {
  const kind = "client";
  toast("正在生成客户端包，请稍候…");
  const response = await request("/api/export/" + kind, { method: "POST", body: "{}" }, true);
  const blob = await response.blob(), url = URL.createObjectURL(blob), link = document.createElement("a");
  const match = /filename="?([^";]+)"?/.exec(response.headers.get("Content-Disposition") || "");
  link.href = url; link.download = match ? match[1].replace(/[^a-zA-Z0-9_.-]/g, "_") : "dflegacy-" + kind + ".zip";
  document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 60_000); toast("客户端包已生成。");
}));
$("#clearLogs").addEventListener("click", () => { logLines = []; $("#logOutput").textContent = "等待新日志…"; });

async function initialize() {
  const fragment = new URLSearchParams(location.hash.slice(1)); setupToken = fragment.get("setup") || "";
  if (location.hash) history.replaceState(null, "", location.pathname + location.search);
  const status = await request("/api/setup/status"); setupMode = status.required;
  if (setupMode) showAuth(); else { try { await enterApp(); } catch (_) { showAuth(); } }
}
initialize().catch(error => { $("#authDescription").textContent = error.message; });
setInterval(() => { if (loggedIn) refreshStatus().catch(error => toast(error.message, true)); }, 3500);
setInterval(() => { if (loggedIn && currentView === "logs") refreshLogs().catch(error => toast(error.message, true)); }, 2000);
