"""Seed both accounts' primary characters into town 1 area 0 (the shared
square) so the dual-client acceptance run lands them on the same screen.

Usage: python seed_same_area.py
"""
import json

STATE = r"G:\Game\DNF_ACT1\DF\town-test\data\state.json"

data = json.load(open(STATE, encoding="utf-8"))
for account in data["Accounts"]:
    for character in (account.get("Characters") or []):
        before = (character.get("TownId"), character.get("AreaId"))
        character["TownId"] = 1
        character["AreaId"] = 0
        print(f"{character.get('Name')}: {before} -> (1, 0)")
open(STATE, "w", encoding="utf-8").write(json.dumps(data, ensure_ascii=False))
print("seeded: all characters at town 1 area 0")
