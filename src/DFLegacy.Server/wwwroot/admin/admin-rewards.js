"use strict";

viewCopy.victory = ["胜点发放", "向指定角色发放可消费的决斗胜点"];

const rewardErrors = {
  "mailbox-full": "邮箱已满",
  "character-not-found": "角色已不存在",
  "invalid-request": "请填写有效的角色和正整数数量",
  "victory-points-overflow": "发放后胜点将超过上限",
  "operation-conflict": "该批次内容与先前提交不一致，请新建批次",
  "store-quarantined": "存档写入结果尚未确认，请先检查服务端状态",
  "write-outcome-unknown": "存档写入结果尚未确认，请保留当前批次，核对服务端后重试"
};
function rewardErrorText(error) { return rewardErrors[error] || error || "未知原因"; }
function rewardOperationId() {
  if (globalThis.crypto.randomUUID) return globalThis.crypto.randomUUID();
  const bytes = globalThis.crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
  const hex = [...bytes].map(b => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
let activeMailBatch = null;
let lastMailBatch = null;
let lastVictoryOperation = null;
let rewardsOwner = "";

function pendingKey() { return `dflegacy.admin.rewards.${auth.userName.toUpperCase()}`; }
function savePendingRewards() {
  try {
    sessionStorage.setItem(pendingKey(), JSON.stringify({ version: 1, mail: activeMailBatch, victory: lastVictoryOperation }));
  } catch { throw new Error("无法保存奖励操作 ID，请检查浏览器会话存储后重试；本次不会创建新的奖励操作。"); }
  showPendingRewards();
}
function showPendingRewards() {
  $("#victoryOperation").textContent = lastVictoryOperation
    ? `待核对操作：${lastVictoryOperation.operationId}；请按原角色及数量 ${lastVictoryOperation.amount} 重试。` : "";
  $("#mailBatchPending").classList.toggle("hidden", !activeMailBatch);
  $("#mailPendingId").textContent = activeMailBatch?.operationId || "";
}
function restorePendingRewards() {
  if (!isAdmin() || !auth.userName || rewardsOwner === auth.userName.toUpperCase()) return;
  let saved;
  try { saved = JSON.parse(sessionStorage.getItem(pendingKey()) || "null"); }
  catch { throw new Error("奖励操作记录无法读取，请保留此标签页并核对原操作 ID。"); }
  activeMailBatch = saved?.version === 1 ? saved.mail : null;
  lastVictoryOperation = saved?.version === 1 ? saved.victory : null;
  rewardsOwner = auth.userName.toUpperCase();
  if (lastVictoryOperation) {
    $('[name="characterId"]', $("#victoryForm")).value = lastVictoryOperation.characterId;
    $('[name="amount"]', $("#victoryForm")).value = lastVictoryOperation.amount;
  }
  if (activeMailBatch) {
    const form = $("#mailForm");
    $("#mailScope").value = "all";
    for (const [name, value] of Object.entries(activeMailBatch.mail)) {
      const input = $(`[name="${name}"]`, form);
      if (input) input.value = value ?? "";
    }
    updateAttachmentFields(); updateMailScope();
  }
  showPendingRewards();
}

function refreshVictoryPointBalance() {
  const form = $("#victoryForm");
  if (!form) return;
  restorePendingRewards();
  const select = $('[name="characterId"]', form);
  if (lastVictoryOperation && !select.value && state.characters.some(c => c.id === lastVictoryOperation.characterId))
    select.value = lastVictoryOperation.characterId;
  const selected = state.characters.find(c => c.id === select.value);
  $("#victoryBalance").textContent = `当前胜点：${selected ? Number(selected.victoryPoints || 0).toLocaleString() : "—"}`;
}

async function prepareBroadcastMail(mail, retryOf = null) {
  restorePendingRewards();
  const signature = JSON.stringify({ mail, retryOf });
  if (activeMailBatch?.phase === "sending" && activeMailBatch.signature !== signature)
    throw new Error("原批次发送结果尚未确认，请先恢复原批次结果。");
  if (!activeMailBatch || activeMailBatch.signature !== signature)
    activeMailBatch = { operationId: rewardOperationId(), signature, mail, retryOf, phase: "preview" };
  const batch = activeMailBatch;
  savePendingRewards();
  const result = await api("/api/admin/mail/broadcast/preview", {
    method: "POST", body: JSON.stringify({ operationId: batch.operationId, mail, retryOf })
  });
  if (activeMailBatch !== batch) return;
  if (result.status === "Completed") { renderMailBatchResult(result, batch.mail); return; }
  batch.phase = "prepared"; savePendingRewards();
  const attachment = mail.attachmentKind ? `；附件 ID ${mail.itemId} × ${mail.quantity}` : "；无附件";
  $("#mailBatchPreviewText").textContent = `接收 ${result.targetCount} 个角色；发件人：${mail.sender}；金币：${mail.gold}${attachment}。正文：${mail.text || "（空）"}`;
  $("#mailBatchConfirm").disabled = false;
  $("#mailBatchPreview").classList.remove("hidden");
}

function renderMailBatchResult(result, mail) {
  if (activeMailBatch?.operationId === result.operationId) {
    activeMailBatch.phase = "completed";
    savePendingRewards();
  }
  lastMailBatch = { result, mail };
  $("#mailBatchPreview").classList.add("hidden");
  $("#mailBatchResult").classList.remove("hidden");
  $("#mailBatchId").textContent = result.operationId;
  $("#mailBatchSummary").textContent = `目标 ${result.targetCount} 个角色，发送成功 ${result.sentCount}，失败 ${result.failedCount}，已送达而跳过 ${result.skippedCount}。`;
  const failures = $("#mailBatchFailures"); failures.replaceChildren();
  for (const item of result.deliveries.filter(d => !d.success)) {
    const row = document.createElement("li");
    row.textContent = `${item.name}：${rewardErrorText(item.error)}`;
    failures.appendChild(row);
  }
  $("#mailBatchRetry").classList.toggle("hidden", !result.failedCount);
}

function updateMailScope() {
  const all = $("#mailScope").value === "all";
  const select = $('[name="characterId"]', $("#mailForm"));
  select.disabled = all; select.required = !all;
  $("#mailCharacterField").classList.toggle("hidden", all);
  $("#mailSubmit").textContent = all ? "预览全服邮件" : "发送邮件";
  $("#mailBatchPreview").classList.add("hidden");
}

document.addEventListener("DOMContentLoaded", () => {
  if (!$("#victoryForm")) return;
  $("#mailScope").addEventListener("change", updateMailScope);
  const invalidatePreview = () => {
    if (activeMailBatch?.phase !== "sending") {
      activeMailBatch = null;
      savePendingRewards();
    }
    $("#mailBatchPreview").classList.add("hidden");
  };
  $("#mailForm").addEventListener("input", invalidatePreview);
  $("#mailForm").addEventListener("change", invalidatePreview);
  $("#mailBatchCancel").addEventListener("click", () => $("#mailBatchPreview").classList.add("hidden"));
  $("#mailBatchNew").addEventListener("click", () => {
    if (activeMailBatch?.phase === "sending") { showToast("请先恢复原批次结果。", true); return; }
    activeMailBatch = null; lastMailBatch = null;
    savePendingRewards();
    $("#mailBatchPreview").classList.add("hidden"); $("#mailBatchResult").classList.add("hidden");
  });
  $("#mailBatchConfirm").addEventListener("click", async event => {
    const batch = activeMailBatch; if (!batch) return;
    const button = event.currentTarget; button.disabled = true;
    try {
      batch.phase = "sending";
      savePendingRewards();
      const result = await api(`/api/admin/mail/broadcast/${batch.operationId}/send`, { method: "POST" });
      renderMailBatchResult(result, batch.mail);
      showToast(`全服邮件已处理：成功 ${result.sentCount}，失败 ${result.failedCount}`);
    } catch (error) { showToast(rewardErrorText(errorMessage(error)), true); }
    finally { button.disabled = false; }
  });
  $("#mailBatchRecover").addEventListener("click", async event => {
    const batch = activeMailBatch; if (!batch) return;
    const button = event.currentTarget; button.disabled = true;
    try {
      // Preview replays the durable operation. It never sends; a completed
      // operation returns its receipt and a prepared one still needs confirmation.
      await prepareBroadcastMail(batch.mail, batch.retryOf);
    } catch (error) { showToast(rewardErrorText(errorMessage(error)), true); }
    finally { button.disabled = false; }
  });
  $("#mailBatchRetry").addEventListener("click", async event => {
    if (!lastMailBatch) return;
    if (activeMailBatch?.phase === "sending") { showToast("请先恢复原批次结果。", true); return; }
    const button = event.currentTarget; button.disabled = true;
    try {
      activeMailBatch = null;
      await prepareBroadcastMail(lastMailBatch.mail, lastMailBatch.result.operationId);
    } catch (error) { showToast(rewardErrorText(errorMessage(error)), true); }
    finally { button.disabled = false; }
  });
  $('[name="characterId"]', $("#victoryForm")).addEventListener("change", refreshVictoryPointBalance);
  $("#victoryForm").addEventListener("submit", event => {
    event.preventDefault();
    runForm(event.currentTarget, async data => {
      const characterId = data.get("characterId"), amount = Number(data.get("amount"));
      if (!Number.isSafeInteger(amount) || amount <= 0 || amount > 4294967295)
        throw new Error("胜点数量必须是 1～4294967295 的整数。");
      const signature = JSON.stringify({ characterId, amount });
      if (lastVictoryOperation && lastVictoryOperation.signature !== signature)
        throw new Error("原胜点操作结果尚未确认，请按待核对记录的原角色及数量重试。");
      if (!lastVictoryOperation)
        lastVictoryOperation = { signature, operationId: rewardOperationId(), characterId, amount };
      savePendingRewards();
      let result;
      try {
        result = await api(`/api/admin/characters/${characterId}/victory-points`, {
          method: "POST", body: JSON.stringify({ operationId: lastVictoryOperation.operationId, amount })
        });
      } catch (error) {
        // An explicit durable rejection is known not to have granted anything.
        // Transport/503/conflict errors keep the original identity for recovery.
        if (error.body?.status === "Rejected" && error.body.error !== "operation-conflict") {
          lastVictoryOperation = null;
          savePendingRewards();
        }
        throw new Error(rewardErrorText(errorMessage(error)));
      }
      const completedOperation = lastVictoryOperation;
      lastVictoryOperation = null;
      try { savePendingRewards(); }
      catch (error) { lastVictoryOperation = completedOperation; throw error; }
      const character = state.characters.find(c => c.id === characterId);
      if (character) character.victoryPoints = result.balance;
      refreshVictoryPointBalance();
      if (result.replayed) await refreshData();
      return { message: `已发放 ${amount} 胜点，发放后余额 ${result.balance}` };
    });
  });
  updateMailScope(); refreshVictoryPointBalance();
});
