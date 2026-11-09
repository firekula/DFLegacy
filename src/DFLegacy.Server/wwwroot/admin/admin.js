"use strict";

const SESSION_TOKEN_KEY = "dflegacy.admin.token";
const SESSION_USER_KEY = "dflegacy.admin.userName";
const SESSION_ROLE_KEY = "dflegacy.admin.role";

const ROLE_PLAYER = 0;
const ROLE_ADMIN = 1;

// 与 QuestCatalog.JobNames / AccountKind 保持一致，仅用于展示。
const JOB_NAMES = { 0: "鬼剑士", 1: "格斗家", 2: "神枪手", 3: "魔法师", 4: "圣职者" };
const KIND_NAMES = { 0: "未分配", 1: "测试账号", 2: "预留账号", 3: "普通账号" };

const auth = {
  token: "",
  userName: "",
  role: ROLE_PLAYER,
  waiters: []
};

const state = {
  characters: [],
  dungeons: [],
  messageTypes: [],
  users: [],
  toastTimer: null
};

const viewCopy = {
  mail: ["邮件", "发送系统邮件和游戏物品"],
  direct: ["单人通知", "向指定在线角色发送弹框或消息"],
  broadcast: ["广播通知", "向全部在线角色发送弹框或消息"],
  dungeon: ["副本权限", "设置角色可进入的 DGN 难度"],
  users: ["账号管理", "管理角色、密码、封禁和账号权限"],
  characters: ["我的角色", "账号名下的角色列表"]
};

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

const isAdmin = () => auth.role === ROLE_ADMIN;
const errorText = body =>
  body && typeof body === "object" && body.error ? String(body.error) : "";
const errorMessage = error => (error instanceof Error ? error.message : String(error));

/* ---------- 会话（sessionStorage，按标签页隔离） ---------- */

function loadSession() {
  auth.token = sessionStorage.getItem(SESSION_TOKEN_KEY) || "";
  auth.userName = sessionStorage.getItem(SESSION_USER_KEY) || "";
  auth.role = Number(sessionStorage.getItem(SESSION_ROLE_KEY)) === ROLE_ADMIN
    ? ROLE_ADMIN
    : ROLE_PLAYER;
}

function saveSession() {
  sessionStorage.setItem(SESSION_TOKEN_KEY, auth.token);
  sessionStorage.setItem(SESSION_USER_KEY, auth.userName);
  sessionStorage.setItem(SESSION_ROLE_KEY, String(auth.role));
}

function clearSession() {
  auth.token = "";
  auth.userName = "";
  auth.role = ROLE_PLAYER;
  sessionStorage.removeItem(SESSION_TOKEN_KEY);
  sessionStorage.removeItem(SESSION_USER_KEY);
  sessionStorage.removeItem(SESSION_ROLE_KEY);
}

const authHeaders = () => (auth.token ? { Authorization: `Bearer ${auth.token}` } : {});

/* ---------- 登录 ---------- */

function setLoginError(message) {
  const element = $("#loginError");
  element.textContent = message || "";
  element.classList.toggle("hidden", !message);
}

function showLogin() {
  $("#loginOverlay").classList.remove("hidden");
  setLoginError("");
  $("#loginPassword").value = "";
  const target = $("#loginUserName").value.trim() ? $("#loginPassword") : $("#loginUserName");
  target.focus();
}

function hideLogin() {
  $("#loginOverlay").classList.add("hidden");
  setLoginError("");
}

// api() 拿到 401 时挂起，等重新登录成功后重放原请求。
function waitForLogin() {
  return new Promise(resolve => auth.waiters.push(resolve));
}

async function requestLogin(userName, password) {
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userName, password })
  });
  const contentType = response.headers.get("content-type") || "";
  const body = contentType.includes("application/json")
    ? await response.json()
    : await response.text();
  if (!response.ok) {
    if (response.status === 429) {
      throw new Error(errorText(body) || "失败次数过多，请稍后再试。");
    }
    if (response.status === 401) {
      throw new Error("账号或密码错误。");
    }
    throw new Error(errorText(body) || `登录失败（${response.status}）`);
  }
  return body;
}

function completeLogin(payload) {
  auth.token = payload.token || "";
  auth.userName = payload.userName || $("#loginUserName").value.trim();
  auth.role = Number(payload.role) === ROLE_ADMIN ? ROLE_ADMIN : ROLE_PLAYER;
  saveSession();
  applyIdentity();
  hideLogin();
  const waiters = auth.waiters.splice(0);
  if (waiters.length > 0) {
    waiters.forEach(resolve => resolve());
  } else {
    startSession();
  }
}

async function submitLoginForm(event) {
  event.preventDefault();
  const userName = $("#loginUserName").value.trim();
  const password = $("#loginPassword").value;
  if (!userName || !password) {
    setLoginError("请输入账号和密码。");
    return;
  }
  const button = $("#loginSubmit");
  button.disabled = true;
  setLoginError("");
  try {
    completeLogin(await requestLogin(userName, password));
  } catch (error) {
    setLoginError(errorMessage(error));
  } finally {
    button.disabled = false;
  }
}

async function logout() {
  const token = auth.token;
  clearSession();
  applyIdentity();
  setServerState("pending", "未登录");
  showLogin();
  if (!token) {
    return;
  }
  try {
    await fetch("/api/auth/logout", {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` }
    });
  } catch {
    // 登出失败不阻塞界面：token 已被本地清除，服务端过期后自然失效。
  }
}

/* ---------- 请求 ---------- */

async function api(path, options = {}, retry = true) {
  const response = await fetch(path, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...authHeaders(),
      ...(options.headers || {})
    }
  });
  const contentType = response.headers.get("content-type") || "";
  const body = contentType.includes("application/json")
    ? await response.json()
    : await response.text();
  if (response.status === 401) {
    clearSession();
    applyIdentity();
    showLogin();
    if (retry) {
      await waitForLogin();
      return api(path, options, false);
    }
    throw new Error(errorText(body) || "需要登录（token 缺失或已失效）。");
  }
  if (response.status === 403) {
    const message = errorText(body) || "需要管理员权限。";
    showToast(message, true);
    throw new Error(message);
  }
  if (!response.ok) {
    const error = new Error(errorText(body) || `${response.status} ${response.statusText}`);
    error.status = response.status;
    error.body = body;
    throw error;
  }
  return body;
}

/* ---------- 通用界面 ---------- */

function showToast(message, error = false) {
  const toast = $("#toast");
  toast.textContent = message;
  toast.classList.toggle("error", error);
  toast.classList.add("visible");
  if ($("#gmAccountDialog")?.open) {
    const status = $("#gmActionStatus");
    status.textContent = message;
    status.classList.toggle("error", error);
  }
  clearTimeout(state.toastTimer);
  state.toastTimer = setTimeout(() => toast.classList.remove("visible"), 3200);
}

function setSubmitting(form, submitting) {
  $$(`button[type="submit"]`, form).forEach(control => {
    control.disabled = submitting;
  });
}

async function runForm(form, action) {
  setSubmitting(form, true);
  try {
    const result = await action(new FormData(form));
    const message = result.message || "操作成功";
    showToast(message);
    return result;
  } catch (error) {
    showToast(errorMessage(error), true);
    return null;
  } finally {
    setSubmitting(form, false);
  }
}

function option(value, label, disabled = false) {
  const element = document.createElement("option");
  element.value = value;
  element.textContent = label;
  element.disabled = disabled;
  return element;
}

function cell(text) {
  const element = document.createElement("td");
  element.textContent = text;
  return element;
}

const jobLabel = job => JOB_NAMES[job] || `职业 ${job}`;

/* ---------- 身份与角色门禁 ---------- */

function applyIdentity() {
  const admin = isAdmin();
  const guest = !auth.token;
  const accountDialog = $("#gmAccountDialog");
  if ((!admin || guest) && accountDialog?.open) accountDialog.close();
  document.body.classList.toggle("player-mode", !admin && !guest);
  document.body.classList.toggle("guest-mode", guest);
  $("#identityRole").textContent = guest
    ? "当前身份：未登录"
    : (admin ? "当前身份：管理员" : "当前身份：玩家");
  $("#identityName").textContent = auth.userName || "—";
  $("#ownerName").textContent = auth.userName || "—";
  $("#logoutButton").disabled = guest;
  if (guest) {
    return;
  }
  const active = $(".nav-item.active");
  if (admin && active && active.classList.contains("player-only")) {
    switchView("mail");
  } else if (!admin && (!active || !active.classList.contains("player-only"))) {
    switchView("characters");
  }
}

/* ---------- 渲染 ---------- */

function renderCharacters() {
  const onlineCount = state.characters.filter(character => character.online).length;
  $("#onlineCount").textContent = `${onlineCount} 名角色在线`;
  $$(".character-select").forEach(select => {
    const previous = select.value;
    const onlineOnly = select.classList.contains("online-only");
    select.replaceChildren(option("", onlineOnly && onlineCount === 0
      ? "当前没有在线角色"
      : "请选择角色"));
    state.characters
      .filter(character => !onlineOnly || character.online)
      .forEach(character => {
        const presence = character.online ? "在线" : "离线";
        const town = character.townId
          ? ` · 城镇${character.townId}/区域${character.areaId}`
          : "";
        select.append(option(
          character.id,
          `[${presence}] ${character.name} · ${character.userName} · Lv.${character.level}${town}`));
      });
    if ([...select.options].some(item => item.value === previous)) {
      select.value = previous;
    }
  });
  if (typeof refreshVictoryPointBalance === "function") refreshVictoryPointBalance();
}

function renderDungeons() {
  const select = $("#dungeonSelect");
  const previous = select.value;
  select.replaceChildren(option("", "请选择副本"));
  state.dungeons.forEach(dungeon => {
    const flags = [
      `Lv.${dungeon.minimumLevel}-${dungeon.basisLevel}`,
      dungeon.isHellDungeon ? "深渊" : null
    ].filter(Boolean).join(" · ");
    select.append(option(dungeon.dungeonId, `${dungeon.dungeonId} · ${flags}`));
  });
  if ([...select.options].some(item => item.value === previous)) {
    select.value = previous;
  }
}

function renderMessageTypes() {
  $$(".message-type-select").forEach(select => {
    select.replaceChildren();
    state.messageTypes.forEach(messageType => {
      select.append(option(
        messageType.value,
        `${messageType.value} · ${messageType.name}`));
    });
    select.value = "16";
  });
}

function renderOwnCharacters(characters) {
  const body = $("#characterTableBody");
  body.replaceChildren();
  characters.forEach(character => {
    const row = document.createElement("tr");
    row.append(cell(character.name));
    row.append(cell(`Lv.${character.level}`));
    row.append(cell(jobLabel(character.job)));
    row.append(cell(String(character.slot)));
    body.append(row);
  });
  $("#characterTable").classList.toggle("hidden", characters.length === 0);
  $("#characterEmpty").classList.toggle("hidden", characters.length > 0);
}

function renderUsers() {
  const body = $("#userTableBody");
  body.replaceChildren();
  const selfName = auth.userName.toLowerCase();
  state.users.forEach(account => {
    const role = Number(account.role) === ROLE_ADMIN ? ROLE_ADMIN : ROLE_PLAYER;
    const row = document.createElement("tr");

    row.append(cell(account.userName));

    const roleCell = document.createElement("td");
    const tag = document.createElement("span");
    tag.className = `role-tag ${role === ROLE_ADMIN ? "admin" : "player"}`;
    tag.textContent = role === ROLE_ADMIN ? "管理员" : "玩家";
    roleCell.append(tag);
    row.append(roleCell);

    row.append(cell(String((account.characters || []).length)));

    const uidCell = document.createElement("td");
    uidCell.textContent =
      `UID ${account.accountUid} · ${KIND_NAMES[account.kind] ?? `类型 ${account.kind}`}`;
    row.append(uidCell);
    row.append(cell(accountBanLabel(account.ban)));

    const actionCell = document.createElement("td");
    actionCell.className = "align-right";
    const button = document.createElement("button");
    button.type = "button";
    button.className = "secondary-button small-button";
    const isSelf = account.userName.toLowerCase() === selfName;
    const target = role === ROLE_ADMIN ? ROLE_PLAYER : ROLE_ADMIN;
    button.textContent = role === ROLE_ADMIN ? "降级为玩家" : "提升为管理员";
    button.disabled = isSelf;
    if (isSelf) {
      button.title = "不能修改自己的权限。";
    }
    button.addEventListener("click", () => setUserRole(account.userName, target));
    actionCell.append(button);
    const kindButton = document.createElement("button");
    kindButton.type = "button";
    kindButton.className = "secondary-button small-button";
    const nextKind = Number(account.kind) === 1 ? "Normal" : "Test";
    kindButton.textContent = nextKind === "Test" ? "设为测试账号" : "设为普通账号";
    kindButton.addEventListener("click", () => setUserKind(account.userName, nextKind));
    actionCell.append(kindButton);
    const manageButton = document.createElement("button");
    manageButton.type = "button";
    manageButton.className = "secondary-button small-button";
    manageButton.textContent = "管理账号";
    manageButton.addEventListener("click", () => openManagedAccount(account));
    actionCell.append(manageButton);
    row.append(actionCell);

    body.append(row);
  });
  $("#userEmpty").classList.toggle("hidden", state.users.length > 0);
  $("#userTable").classList.toggle("hidden", state.users.length === 0);
}

function accountBanLabel(ban) {
  if (!ban) return "未封禁";
  return ban.expiresAt ? `封禁至 ${new Date(ban.expiresAt).toLocaleString("zh-CN")}` : "永久封禁";
}

function updateManagedBan(account) {
  $("#gmBanStatus").textContent = account.ban
    ? `${accountBanLabel(account.ban)}。原因：${account.ban.reason}`
    : "未封禁";
  $("#gmUnban").disabled = !account.ban;
}

function renderManagedCharacters(userName, protectedAccount = false) {
  const characters = state.characters.filter(character =>
    character.userName.toLowerCase() === userName.toLowerCase());
  const list = $("#managedCharacterList");
  list.replaceChildren();
  $("#managedCharacterHint").textContent = characters.length ? "仅可删除离线角色。" : "该账号没有角色。";
  for (const character of characters) {
    const row = document.createElement("li");
    const label = document.createElement("span");
    label.textContent = `${character.name} · Lv.${character.level} · ${character.online ? "在线" : "离线"}`;
    const button = document.createElement("button");
    button.type = "button";
    button.className = "secondary-button small-button";
    button.textContent = "删除角色";
    button.disabled = protectedAccount || character.online !== false;
    button.addEventListener("click", async () => {
      if (!window.confirm(`永久删除账号 ${userName} 的角色 ${character.name}？此操作无法撤销。`)) return;
      button.disabled = true;
      try {
        await api(`/api/admin/characters/${encodeURIComponent(character.id)}`, { method: "DELETE" }, false);
        state.characters = state.characters.filter(row => row.id !== character.id);
        renderCharacters();
        const dialog = $("#gmAccountDialog");
        if (dialog.open && dialog.dataset.userName === userName) {
          renderManagedCharacters(userName, protectedAccount);
        }
        showToast(`已删除角色 ${character.name}`);
        await refreshUsers();
      } catch (error) {
        showToast(errorMessage(error), true);
      } finally {
        button.disabled = false;
      }
    });
    row.append(label, button);
    list.append(row);
  }
}

async function openManagedAccount(account) {
  const dialog = $("#gmAccountDialog");
  const protectedAccount = Number(account.role) === ROLE_ADMIN;
  dialog.dataset.userName = account.userName;
  $("#gmAccountName").textContent = account.userName;
  $("#gmActionStatus").textContent = "";
  $("#managedCharacterHint").textContent = "正在读取角色状态…";
  $("#managedCharacterList").replaceChildren();
  $("#gmPlayerActions").classList.toggle("hidden", protectedAccount);
  $("#gmProtectedAccount").classList.toggle("hidden", !protectedAccount);
  $("#gmPasswordForm").reset();
  $("#gmBanForm").reset();
  updateBanDuration();
  updateManagedBan(account);
  dialog.showModal();
  try {
    state.characters = await api("/api/admin/characters", {}, false);
    if (dialog.open && dialog.dataset.userName === account.userName) {
      renderManagedCharacters(account.userName, protectedAccount);
    }
  } catch (error) {
    if (dialog.open && dialog.dataset.userName === account.userName) {
      $("#managedCharacterHint").textContent = "无法读取角色状态。请重新打开账号管理。";
      showToast(errorMessage(error), true);
    }
  }
}

function updateBanDuration() {
  const permanent = $("#gmBanMode").value === "permanent";
  $("#gmBanHoursField").classList.toggle("hidden", permanent);
  $("#gmBanHours").disabled = permanent;
}

function bindAccountManagement() {
  const dialog = $("#gmAccountDialog");
  const passwordForm = $("#gmPasswordForm");
  passwordForm.addEventListener("submit", event => {
    event.preventDefault();
    const userName = dialog.dataset.userName;
    runForm(passwordForm, async data => {
      const newPassword = data.get("newPassword");
      if (newPassword !== data.get("confirmPassword")) throw new Error("两次输入的密码不一致。");
      passwordForm.reset();
      await api(`/api/admin/users/${encodeURIComponent(userName)}/password`, {
        method: "PUT", body: JSON.stringify({ newPassword })
      }, false);
      return { message: `已重置 ${userName} 的密码` };
    });
  });
  dialog.addEventListener("close", () => passwordForm.reset());
  $("#gmBanMode").addEventListener("change", updateBanDuration);
  const banForm = $("#gmBanForm");
  banForm.addEventListener("submit", event => {
    event.preventDefault();
    const userName = dialog.dataset.userName;
    runForm(banForm, async data => {
      const durationHours = data.get("mode") === "permanent" ? null : Number(data.get("durationHours"));
      if (durationHours !== null && (!Number.isInteger(durationHours) || durationHours < 1 || durationHours > 8760)) {
        throw new Error("封禁时长必须为 1–8760 个整小时。");
      }
      const reason = String(data.get("reason")).trim();
      if (!reason) throw new Error("请输入封禁原因。");
      const duration = durationHours === null ? "永久封禁" : `封禁 ${durationHours} 小时`;
      if (!window.confirm(`确认对 ${userName} 执行${duration}？`)) return { message: "已取消封禁操作" };
      await api(`/api/admin/users/${encodeURIComponent(userName)}/ban`, {
        method: "PUT", body: JSON.stringify({ durationHours, reason })
      }, false);
      await refreshUsers();
      return { message: `已对 ${userName} 执行${duration}` };
    });
  });
  $("#gmUnban").addEventListener("click", async event => {
    const userName = dialog.dataset.userName;
    if (!window.confirm(`确认解除账号 ${userName} 的封禁？`)) return;
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await api(`/api/admin/users/${encodeURIComponent(userName)}/ban`, { method: "DELETE" }, false);
      await refreshUsers();
      showToast(`已解除 ${userName} 的封禁`);
    } catch (error) {
      button.disabled = false;
      showToast(errorMessage(error), true);
    }
  });
}

function setServerState(mode, text) {
  const element = $("#serverState");
  element.className = `server-state ${mode}`;
  element.lastElementChild.textContent = text;
}

/* ---------- 数据加载 ---------- */

async function refreshUsers() {
  if (!isAdmin()) {
    return;
  }
  try {
    const users = await api("/api/admin/users");
    state.users = Array.isArray(users) ? users : [];
    renderUsers();
    const dialog = $("#gmAccountDialog");
    const account = dialog?.open && state.users.find(user => user.userName === dialog.dataset.userName);
    if (account) updateManagedBan(account);
  } catch (error) {
    showToast(errorMessage(error), true);
  }
}

async function setUserRole(userName, role) {
  const target = role === ROLE_ADMIN ? "Admin" : "Player";
  const label = role === ROLE_ADMIN ? "提升为管理员" : "降级为玩家";
  if (!window.confirm(`确认将 ${userName} ${label}？`)) {
    return;
  }
  try {
    const result = await api(`/api/admin/users/${encodeURIComponent(userName)}/role`, {
      method: "PUT",
      body: JSON.stringify({ role: target })
    });
    const roleName = result.role === "Admin" ? "管理员" : "玩家";
    showToast(`${result.userName} 已设为${roleName}（作废 token ${result.revokedTokens} 个）`);
    await refreshUsers();
  } catch (error) {
    showToast(errorMessage(error), true);
  }
}

async function setUserKind(userName, kind) {
  const label = kind === "Test" ? "测试账号" : "普通账号";
  if (!window.confirm(`确认将 ${userName} 改为${label}？请先退出所有游戏连接；账号权限、余额和物品保持不变。`)) return;
  try {
    await api(`/api/admin/users/${encodeURIComponent(userName)}/kind`, {
      method: "PUT", body: JSON.stringify({ kind })
    });
    showToast(`${userName} 已设为${label}，请重新登录。`);
    await refreshUsers();
  } catch (error) { showToast(errorMessage(error), true); }
}

// 玩家与管理员都能读自己的角色列表，用它同时完成 token 校验。
async function loadOwnCharacters() {
  setServerState("pending", "正在读取账号");
  try {
    const account = await api(`/api/accounts/${encodeURIComponent(auth.userName)}/characters`);
    const characters = Array.isArray(account?.characters) ? account.characters : [];
    renderOwnCharacters(characters);
    setServerState("online", `共 ${characters.length} 个角色`);
    return account;
  } catch (error) {
    setServerState("error", "无法读取角色");
    showToast(errorMessage(error), true);
    return null;
  }
}

async function refreshData() {
  if (!isAdmin()) {
    await loadOwnCharacters();
    return;
  }
  setServerState("pending", "正在读取服务端");
  try {
    const [status, characters, dungeons, messageTypes] = await Promise.all([
      api("/api/status"),
      api("/api/admin/characters"),
      api("/api/admin/dungeons"),
      api("/api/admin/message-types")
    ]);
    state.characters = characters;
    state.dungeons = dungeons;
    state.messageTypes = messageTypes;
    renderCharacters();
    renderDungeons();
    renderMessageTypes();
    setServerState("online", `${status.activeSessions} 个服务会话`);
  } catch (error) {
    setServerState("error", "服务端不可用");
    showToast(errorMessage(error), true);
  }
  await refreshUsers();
}

async function startSession() {
  const account = await loadOwnCharacters();
  if (account && isAdmin()) {
    await refreshData();
  }
}

/* ---------- 视图切换 ---------- */

function switchView(view) {
  $$(".nav-item").forEach(button => {
    button.classList.toggle("active", button.dataset.view === view);
  });
  $$(".view").forEach(panel => {
    panel.classList.toggle("active", panel.dataset.viewPanel === view);
  });
  const copy = viewCopy[view] || viewCopy.mail;
  $("#pageTitle").textContent = copy[0];
  $("#pageSubtitle").textContent = copy[1];
}

/* ---------- 邮件附件 ---------- */

const MAX_MAIL_ATTACHMENTS = 10;
let mailAttachments = [];
let mailAttachmentSequence = 0;
let mailSubmissionPending = false;

function mailControl(tag, properties = {}) {
  return Object.assign(document.createElement(tag), properties);
}

function mailField(label, control, className = "field") {
  const field = mailControl("label", { className });
  field.append(mailControl("span", { textContent: label }), control);
  return field;
}

function updateMailAttachmentCount() {
  mailAttachments.forEach((entry, index) => {
    entry.legend.textContent = `物品格 ${index + 1}`;
    entry.remove.setAttribute("aria-label", `移除物品格 ${index + 1}`);
    entry.results.setAttribute("aria-label", `物品格 ${index + 1} 查询结果`);
  });
  $("#mailAttachmentCount").textContent = `${mailAttachments.length} / ${MAX_MAIL_ATTACHMENTS} 格`;
  $("#mailAddAttachment").disabled = mailAttachments.length >= MAX_MAIL_ATTACHMENTS;
}

function updateMailFormLock() {
  const locked = mailSubmissionPending || !!activeMailBatch;
  $("#mailDraftFields").disabled = locked;
  $("#mailSubmit").disabled = locked;
}

function addMailAttachment(value = {}) {
  if (mailAttachments.length >= MAX_MAIL_ATTACHMENTS) return;
  const id = `mail-item-${++mailAttachmentSequence}`;
  const row = mailControl("fieldset", { className: "mail-attachment-row" });
  const legend = mailControl("legend");
  const remove = mailControl("button", { type: "button", className: "secondary-button small-button remove-attachment", textContent: "移除此格" });
  const grid = mailControl("div", { className: "form-grid two-column" });
  const kind = mailControl("select", { name: "attachmentKind" });
  kind.append(option(1, "普通道具 / 装备"), option(2, "Avatar 装备"), option(3, "宠物蛋"));
  kind.value = String(value.attachmentKind || 1);
  const quantity = mailControl("input", { name: "quantity", type: "number", min: "1", step: "1", value: String(value.quantity ?? 1), required: true });
  const ability = mailControl("input", { name: "avatarAbilityIndex", type: "number", min: "0", max: "65535", step: "1", value: String(value.avatarAbilityIndex ?? 0) });
  const abilityField = mailField("Avatar 能力索引", ability);
  const picker = mailControl("div", { className: "field span-two" });
  const search = mailControl("input", { id, className: "item-search", autocomplete: "off", placeholder: "输入物品 ID、名称或脚本路径", value: value.itemId == null ? "" : String(value.itemId), required: true });
  const searchLabel = mailControl("label", { htmlFor: id, textContent: "物品 ID 或名称" });
  const searchRow = mailControl("div", { className: "search-row" });
  const searchButton = mailControl("button", { type: "button", className: "secondary-button", textContent: "查询" });
  const results = mailControl("select", { className: "item-results", size: 5 });
  const meta = mailControl("div", { id: `${id}-meta`, className: "item-meta", textContent: value.itemId == null ? "尚未选择物品" : `已保存物品 ID ${value.itemId}` });
  search.setAttribute("aria-describedby", meta.id);
  searchRow.append(search, searchButton);
  picker.append(searchLabel, searchRow, results, meta);
  grid.append(mailField("附件类型", kind), mailField("数量", quantity), abilityField, picker);
  row.append(legend, remove, grid);
  const entry = { row, legend, remove, kind, quantity, ability, abilityField, search, results, meta, items: [], selectedItem: null, searchVersion: 0 };
  mailAttachments.push(entry);
  $("#mailAttachmentRows").append(row);
  updateAttachmentFields(entry, false);
  remove.addEventListener("click", () => {
    mailAttachments = mailAttachments.filter(candidate => candidate !== entry);
    row.remove(); updateMailAttachmentCount();
  });
  kind.addEventListener("change", () => updateAttachmentFields(entry));
  search.addEventListener("input", () => {
    entry.searchVersion++; entry.selectedItem = null; entry.items = [];
    results.replaceChildren(); meta.textContent = "尚未选择物品";
    updateAttachmentFields(entry, false);
  });
  searchButton.addEventListener("click", () => searchItems(entry).catch(error => showToast(errorMessage(error), true)));
  search.addEventListener("keydown", event => {
    if (event.key === "Enter") {
      event.preventDefault();
      searchItems(entry).catch(error => showToast(errorMessage(error), true));
    }
  });
  results.addEventListener("change", () => selectItem(entry));
  updateMailAttachmentCount();
  return entry;
}

function bindMailAttachments() {
  $("#mailAddAttachment").addEventListener("click", () => {
    const entry = addMailAttachment();
    entry?.search.focus();
  });
  updateMailAttachmentCount();
}

function mailAttachmentValues(mail) {
  if (Array.isArray(mail.attachments)) return mail.attachments;
  return mail.attachmentKind ? [{ attachmentKind: mail.attachmentKind, itemId: mail.itemId, quantity: mail.quantity, avatarAbilityIndex: mail.avatarAbilityIndex }] : [];
}

function restoreMailDraft(mail) {
  for (const name of ["sender", "text", "gold"])
    $(`[name="${name}"]`, $("#mailForm")).value = String(mail[name] ?? "");
  mailAttachments = [];
  $("#mailAttachmentRows").replaceChildren();
  mailAttachmentValues(mail).forEach(addMailAttachment);
  updateMailAttachmentCount();
}

function updateAttachmentFields(entry, reset = true) {
  const kind = Number(entry.kind.value);
  entry.abilityField.classList.toggle("hidden", kind !== 2);
  entry.ability.disabled = kind !== 2;
  if (reset) {
    entry.searchVersion++; entry.selectedItem = null; entry.items = [];
    entry.results.replaceChildren(); entry.meta.textContent = "尚未选择物品";
  }
  const single = kind !== 1 || (entry.selectedItem && entry.selectedItem.scriptKind !== "Stackable");
  entry.quantity.disabled = !!single;
  if (single) entry.quantity.value = "1";
}

function describeItem(item) {
  return [`ID ${item.id}`, item.name, item.scriptKind, item.attachType,
    item.typeTag || "无类型标签", item.scriptPath].join(" · ");
}

async function searchItems(entry) {
  const query = entry.search.value.trim(), kind = Number(entry.kind.value);
  const version = ++entry.searchVersion;
  const items = await api(`/api/admin/items?query=${encodeURIComponent(query)}&kind=${kind}&limit=60`);
  // A late response must not replace a different row, a newer query, or a removed row.
  if (!mailAttachments.includes(entry) || version !== entry.searchVersion
      || query !== entry.search.value.trim() || kind !== Number(entry.kind.value)) return;
  entry.items = items;
  entry.results.replaceChildren();
  items.forEach(item => entry.results.append(option(item.id, `${item.id} · ${item.name} · ${item.scriptPath}`)));
  if (!items.length) {
    entry.results.append(option("", "没有匹配的 PVF 物品", true));
    entry.meta.textContent = "没有匹配的 PVF 物品";
  }
}

function selectItem(entry) {
  const itemId = Number(entry.results.value);
  entry.selectedItem = entry.items.find(candidate => candidate.id === itemId) || null;
  if (entry.selectedItem) entry.search.value = String(entry.selectedItem.id);
  entry.meta.textContent = entry.selectedItem ? describeItem(entry.selectedItem) : "尚未选择物品";
  updateAttachmentFields(entry, false);
}

async function resolveSelectedItem(entry) {
  if (entry.selectedItem) return entry.selectedItem;
  await searchItems(entry);
  const exact = entry.items.find(item => String(item.id) === entry.search.value.trim());
  if (!exact) throw new Error("请选择每个物品格的有效 PVF 物品，或移除空格。");
  entry.results.value = String(exact.id);
  selectItem(entry);
  return exact;
}

async function readMailAttachments() {
  return Promise.all(mailAttachments.map(async entry => {
    const kind = Number(entry.kind.value), quantity = Number(entry.quantity.value), ability = Number(entry.ability.value);
    if (![1, 2, 3].includes(kind) || !Number.isSafeInteger(quantity) || quantity < 1
        || (kind === 2 && (!Number.isInteger(ability) || ability < 0 || ability > 65535)))
      throw new Error("请填写有效的附件类型、正整数数量及 Avatar 能力索引。");
    const item = await resolveSelectedItem(entry);
    return { attachmentKind: kind, itemId: item.id, quantity: item.scriptKind !== "Stackable" || kind !== 1 ? 1 : quantity,
      avatarAbilityIndex: kind === 2 ? ability : 0 };
  }));
}

function bindForms() {
  $("#mailForm").addEventListener("submit", async event => {
    event.preventDefault();
    if (mailSubmissionPending || activeMailBatch) return;
    mailSubmissionPending = true;
    try {
      await runForm(event.currentTarget, async data => {
        updateMailFormLock();
        const attachments = await readMailAttachments();
        const character = state.characters.find(candidate => candidate.id === data.get("characterId"));
        const payload = { sender: data.get("sender"), text: data.get("text"), gold: Number(data.get("gold")), attachments };
        if (data.get("scope") === "all") {
          await prepareBroadcastMail(payload);
          return { message: "全服邮件预览已就绪，请核对并确认发送。" };
        }
        await api(`/api/admin/characters/${data.get("characterId")}/mail`, {
          method: "POST", body: JSON.stringify(payload)
        });
        return { message: `已向 ${character?.name || "目标角色"} 发送 ${Math.max(1, attachments.length)} 封邮件，金币仅附首封。` };
      });
    } finally {
      mailSubmissionPending = false;
      updateMailFormLock();
    }
  });

  $("#directPopupForm").addEventListener("submit", event => {
    event.preventDefault();
    runForm(event.currentTarget, async data => {
      const character = state.characters.find(candidate => candidate.id === data.get("characterId"));
      await api(`/api/characters/${data.get("characterId")}/popup`, {
        method: "POST",
        body: JSON.stringify({ message: data.get("message") })
      });
      return { message: `弹框已发送给 ${character?.name || "目标角色"}` };
    });
  });

  $("#directMessageForm").addEventListener("submit", event => {
    event.preventDefault();
    runForm(event.currentTarget, async data => {
      const character = state.characters.find(candidate => candidate.id === data.get("characterId"));
      await api(`/api/characters/${data.get("characterId")}/message`, {
        method: "POST",
        body: JSON.stringify({
          message: data.get("message"),
          messageType: Number(data.get("messageType")),
          targetAreaUserId: 0
        })
      });
      return { message: `消息已发送给 ${character?.name || "目标角色"}` };
    });
  });

  $("#broadcastPopupForm").addEventListener("submit", event => {
    event.preventDefault();
    if (!window.confirm("确认向全部在线角色广播弹框？")) {
      return;
    }
    runForm(event.currentTarget, async data => {
      const result = await api("/api/admin/broadcast/popup", {
        method: "POST",
        body: JSON.stringify({ message: data.get("message") })
      });
      return { message: `弹框已广播给 ${result.recipients} 名在线角色` };
    });
  });

  $("#broadcastMessageForm").addEventListener("submit", event => {
    event.preventDefault();
    if (!window.confirm("确认向全部在线角色广播消息？")) {
      return;
    }
    runForm(event.currentTarget, async data => {
      const result = await api("/api/admin/broadcast/message", {
        method: "POST",
        body: JSON.stringify({
          message: data.get("message"),
          messageType: Number(data.get("messageType")),
          targetAreaUserId: 0
        })
      });
      return { message: `消息已广播给 ${result.recipients} 名在线角色` };
    });
  });

  $("#dungeonForm").addEventListener("submit", event => {
    event.preventDefault();
    runForm(event.currentTarget, async data => {
      const character = state.characters.find(candidate => candidate.id === data.get("characterId"));
      const difficulty = Number(data.get("maximumDifficulty"));
      const names = ["普通", "冒险", "勇士", "王者"];
      await api(
        `/api/admin/characters/${data.get("characterId")}/dungeons/${data.get("dungeonId")}`,
        {
          method: "PUT",
          body: JSON.stringify({ maximumDifficulty: difficulty })
        });
      return {
        message: `${character?.name || "目标角色"} 的副本 ${data.get("dungeonId")} 已开放至${names[difficulty]}`
      };
    });
  });
}

function initialize() {
  loadSession();
  $$(".nav-item").forEach(button => {
    button.addEventListener("click", () => {
      switchView(button.dataset.view);
      if (button.dataset.view === "users") {
        refreshUsers();
      } else if (button.dataset.view === "characters") {
        loadOwnCharacters();
      }
    });
  });
  $("#refreshData").addEventListener("click", refreshData);
  $("#logoutButton").addEventListener("click", logout);
  $("#loginForm").addEventListener("submit", submitLoginForm);
  bindMailAttachments();
  bindForms();
  bindAccountManagement();
  applyIdentity();
  if (!auth.token) {
    showLogin();
    return;
  }
  startSession();
}

document.addEventListener("DOMContentLoaded", initialize);
