"""Toggle town-test server.json topology knobs for the S2 Tier 2/3 runs.

Modes:
  split    -- Processes.Channel.Enabled=true, Game.EntranceBinding=false,
              PrivateAreas=[] (spawn room 1/1 becomes SHARED so two clients
              land in one instance -- the same-screen fixture; the original
              game keeps 1/1 private, revert after the runs)
  mono     -- Processes.Channel.Enabled=false, Game.EntranceBinding=true,
              PrivateAreas=["1:1"] (original privacy restored)

Usage: python server_mode.py split|mono
"""
import json
import sys

PATH = r"G:\Game\DNF_ACT1\DF\town-test\server.json"

mode = sys.argv[1]
data = json.load(open(PATH, encoding="utf-8"))
processes = data["DFLegacy"].setdefault("Processes", {})
if mode == "split":
    processes.setdefault("Channel", {})["Enabled"] = True
    processes.setdefault("Channel", {}).setdefault("LinkPort", 7004)
    processes.setdefault("Game", {})["EntranceBinding"] = False
    # Make the spawn room 1/1 SHARED so both clients land in one instance
    # (the same-screen fixture). Works because the code default is now an
    # empty list (ServerOptions.PrivateAreas, fixed in Task 11: the binder
    # APPENDS config arrays to non-empty defaults, so a non-empty default
    # makes an explicit [] impossible).
    data["DFLegacy"]["TownSync"]["PrivateAreas"] = []
elif mode == "mono":
    processes.setdefault("Channel", {})["Enabled"] = False
    processes.setdefault("Game", {})["EntranceBinding"] = True
    data["DFLegacy"]["TownSync"]["PrivateAreas"] = ["1:1"]
else:
    sys.exit(f"unknown mode {mode}")
open(PATH, "w", encoding="utf-8").write(json.dumps(data, ensure_ascii=False, indent=2))
print(f"mode={mode}: Channel.Enabled={processes['Channel']['Enabled']}, "
      f"EntranceBinding={processes['Game']['EntranceBinding']}, "
      f"PrivateAreas={data['DFLegacy']['TownSync']['PrivateAreas']}")
