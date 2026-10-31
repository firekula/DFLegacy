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
  itemResults: [],
  users: [],
  selectedItem: null,
  toastTimer: null
};

const viewCopy = {
  mail: ["邮件", "发送系统邮件和游戏物品"],
  direct: ["单人通知", "向指定在线角色发送弹框或消息"],
  broadcast: ["广播通知", "向全部在线角色发送弹框或消息"],
  dungeon: ["副本权限", "设置角色可进入的 DGN 难度"],
  users: ["用户角色", "提升或降级账号的 8081 后台权限"],
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
    throw new Error(errorText(body) || `${response.status} ${response.statusText}`);
  }
  return body;
}

/* ---------- 通用界面 ---------- */

function showToast(message, error = false) {
  const toast = $("#toast");
  toast.textContent = message;
  toast.classList.toggle("error", error);
  toast.classList.add("visible");
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
    row.append(actionCell);

    body.append(row);
  });
  $("#userEmpty").classList.toggle("hidden", state.users.length > 0);
  $("#userTable").classList.toggle("hidden", state.users.length === 0);
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

function updateAttachmentFields() {
  const kind = Number($("#attachmentKind").value);
  const hasAttachment = kind !== 0;
  $$(".attachment-field").forEach(field => {
    field.classList.toggle("hidden", !hasAttachment);
  });
  $("#avatarAbilityField").classList.toggle("hidden", kind !== 2);
  $("#quantityField").classList.toggle("hidden", !hasAttachment);
  const quantity = $('[name="quantity"]', $("#mailForm"));
  if (kind === 2 || kind === 3) {
    quantity.value = "1";
    quantity.disabled = true;
  } else {
    quantity.disabled = !hasAttachment;
  }
  state.selectedItem = null;
  $("#selectedItemId").value = "";
  $("#itemResults").replaceChildren();
  $("#itemMeta").textContent = hasAttachment ? "尚未选择物品" : "无附件";
  if (hasAttachment && $("#itemSearch").value.trim()) {
    searchItems();
  }
}

function describeItem(item) {
  return [
    `ID ${item.id}`,
    item.name,
    item.scriptKind,
    item.attachType,
    item.typeTag || "无类型标签",
    item.scriptPath
  ].join(" · ");
}

async function searchItems() {
  const query = $("#itemSearch").value.trim();
  const kind = Number($("#attachmentKind").value);
  if (kind === 0) {
    return;
  }
  try {
    const items = await api(
      `/api/admin/items?query=${encodeURIComponent(query)}&kind=${kind}&limit=60`);
    state.itemResults = items;
    const results = $("#itemResults");
    results.replaceChildren();
    items.forEach(item => results.append(option(
      item.id,
      `${item.id} · ${item.name} · ${item.scriptPath}`)));
    if (items.length === 0) {
      results.append(option("", "没有匹配的 PVF 物品", true));
      $("#itemMeta").textContent = "没有匹配的 PVF 物品";
    }
  } catch (error) {
    showToast(errorMessage(error), true);
  }
}

function selectItem() {
  const itemId = Number($("#itemResults").value);
  const item = state.itemResults.find(candidate => candidate.id === itemId) || null;
  state.selectedItem = item;
  $("#selectedItemId").value = item ? String(item.id) : "";
  $("#itemMeta").textContent = item ? describeItem(item) : "尚未选择物品";
  const quantity = $('[name="quantity"]', $("#mailForm"));
  if (item && item.scriptKind !== "Stackable") {
    quantity.value = "1";
    quantity.disabled = true;
  } else if (Number($("#attachmentKind").value) === 1) {
    quantity.disabled = false;
  }
}

async function resolveSelectedItem() {
  const kind = Number($("#attachmentKind").value);
  if (kind === 0) {
    return null;
  }
  if (state.selectedItem) {
    return state.selectedItem;
  }
  await searchItems();
  const exact = state.itemResults.find(item =>
    String(item.id) === $("#itemSearch").value.trim());
  if (!exact) {
    throw new Error("请选择一个有效的 PVF 物品。");
  }
  $("#itemResults").value = String(exact.id);
  selectItem();
  return exact;
}

function bindForms() {
  $("#mailForm").addEventListener("submit", event => {
    event.preventDefault();
    runForm(event.currentTarget, async data => {
      const item = await resolveSelectedItem();
      const character = state.characters.find(candidate => candidate.id === data.get("characterId"));
      const payload = {
        sender: data.get("sender"),
        text: data.get("text"),
        gold: Number(data.get("gold")),
        attachmentKind: Number(data.get("attachmentKind")),
        itemId: item?.id ?? null,
        quantity: item && item.scriptKind !== "Stackable" ? 1 : Number(data.get("quantity") || 1),
        avatarAbilityIndex: Number(data.get("avatarAbilityIndex") || 0)
      };
      await api(`/api/admin/characters/${data.get("characterId")}/mail`, {
        method: "POST",
        body: JSON.stringify(payload)
      });
      return { message: `邮件已发送给 ${character?.name || "目标角色"}` };
    });
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
  $("#attachmentKind").addEventListener("change", updateAttachmentFields);
  $("#itemSearchButton").addEventListener("click", searchItems);
  $("#itemSearch").addEventListener("keydown", event => {
    if (event.key === "Enter") {
      event.preventDefault();
      searchItems();
    }
  });
  $("#itemResults").addEventListener("change", selectItem);
  bindForms();
  updateAttachmentFields();
  applyIdentity();
  if (!auth.token) {
    showLogin();
    return;
  }
  startSession();
}

document.addEventListener("DOMContentLoaded", initialize);
