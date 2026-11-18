"use strict";

// Runtime quest task-item drop rate (GM only). Kept in its own file so the
// diagnostics page does not grow another responsibility; the GM listener
// static allowlist must include this script (WebPanelHosting).

const questDropForm = document.getElementById("questDropForm");
const questDropRate = document.getElementById("questDropRate");
const questDropCurrent = document.getElementById("questDropCurrent");

async function loadQuestDropSettings() {
  questDropCurrent.textContent = "正在读取当前倍率…";
  try {
    const result = await api("/api/admin/drop/quest-rate");
    questDropRate.value = String(result.ratePercent);
    questDropCurrent.textContent = `当前倍率：${result.ratePercent}%（范围 ${result.minimum}–${result.maximum}）`
      + (result.persistent ? "" : "；本次运行不可持久化");
  } catch (error) {
    questDropCurrent.textContent = errorMessage(error);
  }
}

if (questDropForm) {
  questDropForm.addEventListener("submit", async event => {
    event.preventDefault();
    const rate = Number(questDropRate.value);
    if (!Number.isInteger(rate) || rate < 0 || rate > 1000) {
      showToast("倍率必须是 0–1000 的整数。", true);
      return;
    }
    try {
      const result = await api("/api/admin/drop/quest-rate", {
        method: "PUT",
        body: JSON.stringify({ ratePercent: rate })
      });
      questDropRate.value = String(result.ratePercent);
      questDropCurrent.textContent = `当前倍率：${result.ratePercent}%（范围 ${result.minimum}–${result.maximum}）`;
      showToast(`任务道具爆率已设为 ${result.ratePercent}%`);
    } catch (error) {
      showToast(errorMessage(error), true);
    }
  });
}
