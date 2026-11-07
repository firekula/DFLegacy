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

function refreshVictoryPointBalance() {
  const form = $("#victoryForm");
  if (!form) return;
  const selected = state.characters.find(c => c.id === $('[name="characterId"]', form).value);
  $("#victoryBalance").textContent = `当前胜点：${selected ? Number(selected.victoryPoints || 0).toLocaleString() : "—"}`;
}

async function prepareBroadcastMail(mail, retryOf = null) {
  const signature = JSON.stringify({ mail, retryOf });
  if (!activeMailBatch || activeMailBatch.signature !== signature)
    activeMailBatch = { operationId: rewardOperationId(), signature, mail, retryOf };
  const batch = activeMailBatch;
  const result = await api("/api/admin/mail/broadcast/preview", {
    method: "POST", body: JSON.stringify({ operationId: batch.operationId, mail, retryOf })
  });
  if (activeMailBatch !== batch) return;
  if (result.status === "Completed") { renderMailBatchResult(result, batch.mail); return; }
  const attachment = mail.attachmentKind ? `；附件 ID ${mail.itemId} × ${mail.quantity}` : "；无附件";
  $("#mailBatchPreviewText").textContent = `接收 ${result.targetCount} 个角色；发件人：${mail.sender}；金币：${mail.gold}${attachment}。正文：${mail.text || "（空）"}`;
  $("#mailBatchConfirm").disabled = false;
  $("#mailBatchPreview").classList.remove("hidden");
}

function renderMailBatchResult(result, mail) {
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
    activeMailBatch = null;
    $("#mailBatchPreview").classList.add("hidden");
  };
  $("#mailForm").addEventListener("input", invalidatePreview);
  $("#mailForm").addEventListener("change", invalidatePreview);
  $("#mailBatchCancel").addEventListener("click", () => $("#mailBatchPreview").classList.add("hidden"));
  $("#mailBatchNew").addEventListener("click", () => {
    activeMailBatch = null; lastMailBatch = null;
    $("#mailBatchPreview").classList.add("hidden"); $("#mailBatchResult").classList.add("hidden");
  });
  $("#mailBatchConfirm").addEventListener("click", async event => {
    const batch = activeMailBatch; if (!batch) return;
    const button = event.currentTarget; button.disabled = true;
    try {
      const result = await api(`/api/admin/mail/broadcast/${batch.operationId}/send`, { method: "POST" });
      renderMailBatchResult(result, batch.mail);
      showToast(`全服邮件已处理：成功 ${result.sentCount}，失败 ${result.failedCount}`);
    } catch (error) { showToast(rewardErrorText(errorMessage(error)), true); }
    finally { button.disabled = false; }
  });
  $("#mailBatchRetry").addEventListener("click", async event => {
    if (!lastMailBatch) return;
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
      if (!lastVictoryOperation || lastVictoryOperation.signature !== signature)
        lastVictoryOperation = { signature, operationId: rewardOperationId() };
      let result;
      try {
        result = await api(`/api/admin/characters/${characterId}/victory-points`, {
          method: "POST", body: JSON.stringify({ operationId: lastVictoryOperation.operationId, amount })
        });
      } catch (error) { throw new Error(rewardErrorText(errorMessage(error))); }
      lastVictoryOperation = null;
      const character = state.characters.find(c => c.id === characterId);
      if (character) character.victoryPoints = result.balance;
      refreshVictoryPointBalance();
      return { message: `已发放 ${amount} 胜点，发放后余额 ${result.balance}` };
    });
  });
  updateMailScope(); refreshVictoryPointBalance();
});
