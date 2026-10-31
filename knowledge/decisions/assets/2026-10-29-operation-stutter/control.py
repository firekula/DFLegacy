from pathlib import Path
import sys
sys.path.insert(0,r'D:\codex_worktree\party-chat-gold\DFLegacy')
from tools.acceptance.native_control import main
raise SystemExit(main([str(Path(__file__).resolve().parent),*sys.argv[1:]]))
