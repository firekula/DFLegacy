"use strict";

const $ = id => document.getElementById(id);
const api = new URL("../api/client-guild/", location.href);
const roleNames = { 0: "新成员", 1: "会长", 2: "副会长", 3: "优秀成员", 4: "普通成员" };
const jobNames = ["鬼剑士", "格斗家", "神枪手", "魔法师", "圣职者"];
const iconCanvas = $("guild-icon");
const iconContext = iconCanvas.getContext("2d");
let currentGuild;
let busy = false;

function setStatus(text = "", kind = "") {
  $("status").textContent = text;
  $("status").dataset.kind = kind;
}

async function request(path, body) {
  const init = body === undefined ? {} : {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-DFLegacy-Guild-Request": "1" },
    body: JSON.stringify(body)
  };
  const response = await fetch(new URL(path, api), { credentials: "same-origin", ...init });
  if (response.status === 204) return null;
  const result = await response.json().catch(() => null);
  if (!response.ok) {
    const error = new Error(result?.error || "公会服务暂时无法处理请求。");
    error.status = response.status;
    throw error;
  }
  return result;
}

function showLogin() {
  currentGuild = null;
  for (const id of ["character", "members", "guild-details", "guild-name", "announcement-view", "no-guild"])
    $(id).replaceChildren();
  $("search").value = "";
  $("announcement-input").value = "";
  resetManagementForms();
  $("login").hidden = false;
  for (const id of ["character-panel", "guild-nav", "guild", "no-guild", "refresh"]) $(id).hidden = true;
}

function lockControls(disabled) {
  for (const control of document.querySelectorAll("button, #character, #members select")) control.disabled = disabled;
}

async function run(work, revealStatus = false) {
  if (busy) return;
  busy = true;
  lockControls(true);
  setStatus();
  try {
    await work();
  } catch (error) {
    if (error.status === 401) showLogin();
    setStatus(error.message, "error");
  } finally {
    busy = false;
    lockControls(false);
    if (revealStatus && $("status").textContent) $("status").scrollIntoView({ block: "nearest" });
  }
}

async function loadCharacters() {
  const characters = await request("characters");
  const select = $("character");
  const previous = select.value;
  select.replaceChildren(...characters.map(character => new Option(character.name, character.id)));
  if (characters.some(character => character.id === previous)) select.value = previous;
  $("login").hidden = true;
  $("character-panel").hidden = false;
  $("refresh").hidden = false;
  if (characters.length) await loadGuild();
  else renderEmpty("该账号还没有可用角色。");
}

function renderEmpty(message) {
  currentGuild = null;
  resetManagementForms();
  $("guild").hidden = true;
  $("guild-nav").hidden = true;
  $("no-guild").hidden = false;
  $("no-guild").textContent = message;
}

async function loadGuild() {
  const selected = $("character").value;
  const view = await request("characters/" + encodeURIComponent(selected));
  if (!view.guild) {
    renderEmpty(view.name + " 尚未加入公会。");
    return;
  }
  if (currentGuild?.guild.id !== view.guild.id) resetManagementForms();
  currentGuild = view;
  $("guild").hidden = false;
  $("guild-nav").hidden = false;
  $("no-guild").hidden = true;
  $("guild-name").textContent = view.guild.name;
  renderIcon(view.guild.iconPixels);
  $("guild-details").replaceChildren(...[
    ["公会等级", "Lv." + view.guild.level],
    ["公会经验", view.guild.experience],
    ["技能点", view.guild.skillPoints],
    ["我的权限", roleNames[view.rank] || "成员"]
  ].map(([term, value]) => {
    const group = document.createElement("div");
    const label = document.createElement("dt");
    const detail = document.createElement("dd");
    label.textContent = term;
    detail.textContent = value;
    group.append(label, detail);
    return group;
  }));
  $("announcement-view").textContent = view.guild.announcement || "暂无公告。";
  $("announcement-edit").hidden = !view.canEditAnnouncement;
  $("announcement-input").value = view.guild.announcement || "";
  $("invite-member").hidden = !view.canInvite;
  $("settings").hidden = $("settings-link").hidden = !view.canDisband;
  $("set-icon").hidden = !view.canSetIcon;
  $("icon-locked").hidden = !view.canDisband || view.canSetIcon;
  renderRoster();
}

function resetManagementForms() {
  for (const id of ["invite-member", "set-icon", "disband-guild"]) $(id).reset();
}

function renderIcon(pixels) {
  iconCanvas.hidden = true;
  iconContext.clearRect(0, 0, iconCanvas.width, iconCanvas.height);
  if (!pixels) return;
  const rgba = Uint8ClampedArray.from(atob(pixels), byte => byte.charCodeAt(0));
  if (rgba.length !== iconCanvas.width * iconCanvas.height * 4) return;
  iconContext.putImageData(new ImageData(rgba, iconCanvas.width, iconCanvas.height), 0, 0);
  iconCanvas.hidden = false;
}

async function readIcon(file) {
  const types = ["image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp"];
  if (!file || !types.includes(file.type) || file.size > 2 * 1024 * 1024)
    throw new Error("请选择不超过 2 MB 的 PNG、JPEG、GIF、WebP 或 BMP 图片。");
  let bitmap;
  try { bitmap = await createImageBitmap(file); }
  catch { throw new Error("无法读取这张图片，请换一张图。"); }

  try {
    const canvas = document.createElement("canvas");
    canvas.width = iconCanvas.width;
    canvas.height = iconCanvas.height;
    const context = canvas.getContext("2d");
    const scale = Math.min(canvas.width / bitmap.width, canvas.height / bitmap.height);
    const width = Math.max(1, Math.round(bitmap.width * scale));
    const height = Math.max(1, Math.round(bitmap.height * scale));
    context.imageSmoothingEnabled = scale < 1;
    context.drawImage(bitmap, Math.floor((canvas.width - width) / 2), Math.floor((canvas.height - height) / 2), width, height);
    return btoa(String.fromCharCode(...context.getImageData(0, 0, canvas.width, canvas.height).data));
  } finally {
    bitmap.close();
  }
}

function actionButton(text, action, dangerous = false) {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = text;
  if (dangerous) button.className = "danger";
  button.addEventListener("click", action);
  return button;
}

function rowActions(member) {
  const actions = document.createElement("div");
  actions.className = "member-actions";
  function manage(operation, body, message, confirmText = "") {
    if (confirmText && !window.confirm(confirmText)) return;
    run(async () => {
      const character = $("character").value;
      await request(`characters/${encodeURIComponent(character)}/members/${encodeURIComponent(member.id)}/${operation}`, body);
      await loadGuild();
      setStatus(message);
    });
  }

  if (member.canSetRank) {
    const rank = document.createElement("select");
    rank.setAttribute("aria-label", member.name + " 的权限");
    const allowed = currentGuild.rank === 1 ? [0, 4, 3, 2] : [0, 4, 3];
    rank.replaceChildren(...allowed.map(value => new Option(roleNames[value], value)));
    rank.value = member.rank;
    actions.append(rank, actionButton("保存", () => manage("rank", { rank: Number(rank.value) }, "成员权限已保存。")));
  }
  if (member.canTransfer)
    actions.append(actionButton("移交", () => manage("transfer", {}, "会长已移交。", `将会长移交给 ${member.name}？`)));
  if (member.canRemove)
    actions.append(actionButton("移除", () => manage("remove", {}, "成员已移除。", `将 ${member.name} 移出公会？`), true));
  if (!actions.childElementCount) actions.textContent = "—";
  return actions;
}

function renderRoster() {
  if (!currentGuild) return;
  const search = $("search").value.trim().toLowerCase();
  const roster = currentGuild.members.filter(member => member.name.toLowerCase().includes(search));
  $("member-count").textContent = search ? `${roster.length} / ${currentGuild.members.length} 位成员` : `${roster.length} 位成员`;
  const body = $("members");
  body.replaceChildren();
  if (!roster.length) {
    const cell = body.insertRow().insertCell();
    cell.colSpan = 6;
    cell.textContent = "没有找到匹配的角色。";
  }
  for (const member of roster) {
    const row = body.insertRow();
    for (const value of [member.name, member.level, jobNames[member.job] || "—", roleNames[member.rank] || "成员"])
      row.insertCell().textContent = value;
    const status = row.insertCell();
    status.textContent = member.online ? "在线" : "离线";
    status.className = member.online ? "online" : "offline";
    row.insertCell().append(rowActions(member));
  }
}

$("login").addEventListener("submit", event => {
  event.preventDefault();
  const form = event.currentTarget;
  run(async () => {
    const credentials = Object.fromEntries(new FormData(form));
    try { await request("session", credentials); }
    finally { form.elements.password.value = ""; }
    await loadCharacters();
  });
});
$("logout").addEventListener("click", () => run(async () => {
  await request("logout", {});
  showLogin();
  setStatus("已退出公会主页。");
}));
$("refresh").addEventListener("click", () => run(loadCharacters));
$("character").addEventListener("change", () => run(async () => {
  $("search").value = "";
  resetManagementForms();
  await loadGuild();
}));
$("search").addEventListener("input", renderRoster);
$("announcement-edit").addEventListener("submit", event => {
  event.preventDefault();
  run(async () => {
    const announcement = $("announcement-input").value.replace(/[\r\n]/g, " ");
    await request(`characters/${encodeURIComponent($("character").value)}/announcement`, { announcement });
    await loadGuild();
    setStatus("公会公告已更新。");
  });
});

for (const [id, action, getBody, message] of [
  ["invite-member", "invite", () => ({ targetName: $("invite-name").value.trim() }), "邀请已发送，请对方在游戏中确认。"],
  ["set-icon", "icon", async () => ({ pixels: await readIcon($("icon-file").files[0]) }), "公会图标已保存。"],
  ["disband-guild", "disband", () => ({ confirmName: $("disband-name").value }), "公会已解散。"]
]) {
  $(id).addEventListener("submit", event => {
    event.preventDefault();
    const form = event.currentTarget;
    if (!currentGuild) return;
    const guildId = currentGuild.guild.id;
    const characterId = encodeURIComponent($("character").value);
    run(async () => {
      const body = await getBody();
      await request(`characters/${characterId}/${action}`, { guildId, ...body });
      form.reset();
      if (action !== "invite") await loadGuild();
      setStatus(message);
    }, true);
  });
}

run(async () => {
  try { await loadCharacters(); }
  catch (error) {
    showLogin();
    if (error.status !== 401) throw error;
  }
});
