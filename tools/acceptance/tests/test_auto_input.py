"""auto_input 驱动端：原子写计划 + 等完成标记（tmp_path 假客户端，不碰真客户端）。"""
import json

from acceptance import auto_input


def test_write_plan_is_atomic_and_done_roundtrip(tmp_path):
    steps = [{"t": 100, "op": "click", "x": 320, "y": 240}]
    auto_input.write_plan(tmp_path, steps)
    plan = tmp_path / "auto-input.json"
    assert plan.is_file() and not (tmp_path / "auto-input.json.tmp").exists()
    assert json.loads(plan.read_text(encoding="utf-8"))["steps"] == steps
    assert auto_input.wait_ready(tmp_path, timeout_s=1) is True   # 没有 .done = 已就绪
    (tmp_path / "auto-input.done.json").write_text("{}", encoding="utf-8")
    assert auto_input.wait_ready(tmp_path, timeout_s=1) is False  # .done 在 = 未就绪
    assert auto_input.await_done(tmp_path, timeout_s=1) is True
    assert auto_input.await_done(tmp_path, timeout_s=1) is True   # 幂等


def test_write_plan_removes_stale_plan_and_writes_raw_utf8(tmp_path):
    """旧 auto-input.json/.tmp 先删；非 ASCII text 必须按裸 UTF-8 落盘。

    客户端解析器只认 \\ 和 \\" 两种转义，\\uXXXX 转义会被整单拒绝，所以
    json.dumps 必须 ensure_ascii=False。
    """
    (tmp_path / "auto-input.json").write_text("stale", encoding="utf-8")
    (tmp_path / "auto-input.json.tmp").write_text("stale", encoding="utf-8")
    steps = [{"t": 0, "op": "text", "chars": "测试"}]
    auto_input.write_plan(tmp_path, steps)
    plan = tmp_path / "auto-input.json"
    assert json.loads(plan.read_text(encoding="utf-8"))["steps"] == steps
    assert "测试" in plan.read_text(encoding="utf-8")
    assert not (tmp_path / "auto-input.json.tmp").exists()


def test_await_done_returns_false_on_timeout(tmp_path):
    """到点未完成 → False，调用方判失败，绝不猜。"""
    assert auto_input.await_done(tmp_path, timeout_s=0.2) is False
