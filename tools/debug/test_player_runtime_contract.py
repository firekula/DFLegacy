"""Host wiring guards supplement the executable frame/lifecycle fixtures."""
from pathlib import Path
import re
import unittest

ROOT=Path(__file__).resolve().parents[2]
class PlayerRuntimeContractTests(unittest.TestCase):
    def test_frame_gate_patch_does_not_depend_on_optional_overlay_flags(self):
        source=(ROOT/'src/DFLegacy.Ijl15/src/dnf_patches.cpp').read_text(encoding='utf-8')
        policy=source.split('bool IsPatchEnabled(',1)[1].split('enum class PatchState',1)[0]
        self.assertNotIn('ijl15_config',policy,'ordinary frame gate must not depend on FPS/debug/input settings')
        self.assertRegex(source,r'true,\s*//[^}]*?0x0083F992,\s*\{0x75, 0x07, 0x8B, 0xCF, 0xE8, 0xF5\},\s*\{0x90, 0x90, 0x8B, 0xCF, 0xE8, 0xF5\}')
        self.assertIn('if (!dfl::performance_overlay::InstallCallHook())',source)

    def test_debug_library_does_not_link_navigation_runtime_owners(self):
        source=(ROOT/'src/DFLegacy.Ijl15/CMakeLists.txt').read_text(encoding='utf-8')
        debug=source.split('add_library(df_debug SHARED',1)[1].split('set_target_properties(df_debug',1)[0]
        for owner in ['client_navigation_runtime.cpp','client_town_route_runtime.cpp','client_map_feedback_runtime.cpp']:
            self.assertNotIn(owner,debug)
        self.assertIn('DFL_DEBUG_STATE_CONSUMER',source)
