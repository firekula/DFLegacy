cmake_minimum_required(VERSION 3.24)

project(DFLegacy.Ijl15 LANGUAGES CXX RC)

if(NOT WIN32)
    message(FATAL_ERROR "DFLegacy.Ijl15 only supports Windows.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "DFLegacy.Ijl15 must be configured with -A Win32.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(DETOURS_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/detours/src")
add_library(detours STATIC
    ${DETOURS_DIR}/detours.cpp ${DETOURS_DIR}/modules.cpp
    ${DETOURS_DIR}/disasm.cpp ${DETOURS_DIR}/image.cpp ${DETOURS_DIR}/creatwth.cpp
    ${DETOURS_DIR}/disolx86.cpp ${DETOURS_DIR}/disolx64.cpp
    ${DETOURS_DIR}/disolia64.cpp ${DETOURS_DIR}/disolarm.cpp ${DETOURS_DIR}/disolarm64.cpp)
target_include_directories(detours PUBLIC ${DETOURS_DIR})
target_compile_options(detours PRIVATE /W4 /WX /Brepro)
set(DETOURS_LICENSE_FILE "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/detours/LICENSE")
set(DETOURS_PROVENANCE_FILE "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/detours/PROVENANCE.md")
configure_file(../DFLegacy.Client/detours_notices.rc.in detours_notices.rc @ONLY)

add_library(ijl15 SHARED
    include/ijl15.h
    src/auto_input.cpp
    src/auto_input.h
    src/debug_module.cpp
    src/debug_module.h
    src/client_debug_state.h
    src/boss_room_death.cpp
    src/boss_room_death.h
    src/loot_gather.cpp
    src/loot_gather.h
    src/loot_gather_hooks.cpp
    src/creature_gift_row.cpp
    src/creature_gift_row.h
    src/countdown_coin.cpp
    src/countdown_coin.h
    src/dungeon_tooltip.cpp
    src/dungeon_tooltip.h
    src/channel_switch.cpp
    src/channel_switch.h
    src/channel_reentry.cpp
    src/channel_reentry.h
    src/relay_send_queue.cpp
    src/relay_send_queue.h
    src/ground_item_removal.cpp
    src/ground_item_removal.h
    src/inventory_tab.cpp
    src/inventory_tab.h
    src/warehouse_upgrade_button.cpp
    src/warehouse_upgrade_button.h
    src/motion_horizon.cpp
    src/motion_horizon.h
    src/native_loop_probe.cpp
    src/native_loop_probe.h
    src/motion_test_probe.cpp
    src/motion_test_probe.h
    src/provider_probe.cpp
    src/provider_probe.h
    src/party_card_display.cpp
    src/party_card_display.h
    src/d3d8_scaler.cpp
    src/d3d8_scaler.h
    src/d3d8_hooks.cpp
    src/d3d8_hooks.h
    src/native_incidents.cpp
    src/native_incidents.h
    src/native_capture.cpp
    src/native_capture.h
    src/diagnostic_delivery.h
    src/pe_import.h
    src/window_input_scaling.cpp
    src/window_input_scaling.h
    src/d3d8_abi.h
    src/d3d8_presenter.cpp
    src/d3d8_presenter.h
    src/presentation_scaling.cpp
    src/presentation_scaling.h
    src/dnf_patches.cpp
    src/ijl15_entry.cpp
    ../DFLegacy.Client/bootstrap_runtime.cpp
    src/dnf_patches.h
    src/external_string_table.cpp
    src/external_string_table.h
    src/ijl15_config.cpp
    src/ijl15_config.h
    src/ijl15.cpp
    src/ijl15.def
    src/message_audio.cpp
    src/message_audio.h
    src/memory_patch.cpp
    src/memory_patch.h
    src/mouse_wheel.cpp
    src/mouse_wheel.h
    src/net_dump.cpp
    src/net_dump.h
    src/output_paths.cpp
    src/output_paths.h
    src/performance_overlay.cpp
    src/performance_overlay.h
    src/party_combat_detail.cpp
    src/party_combat_detail.h
    src/mage_skill_contact.cpp
    src/mage_skill_contact.h
    src/priest_heal_target.cpp
    src/priest_heal_target.h
    src/stage_follow.cpp
    src/stage_follow.h
    src/summon_owner_death.cpp
    src/summon_owner_death.h
    src/window_scaling.cpp
    src/window_scaling.h
    src/payment_window_scaling.cpp
    src/payment_window_scaling.h
    src/payment_window_layout.h)

target_compile_definitions(ijl15 PRIVATE
    IJL15_EXPORTS
    UNICODE
    _UNICODE)
target_compile_options(ijl15 PRIVATE
    /Brepro
    /W4
    /WX
    /permissive-
    /EHsc)
target_link_options(ijl15 PRIVATE /Brepro /INCREMENTAL:NO /PDBALTPATH:ijl15.pdb)
target_include_directories(ijl15 PUBLIC include)
target_link_libraries(ijl15 PRIVATE
    ole32
    psapi
    shlwapi
    windowscodecs
    winhttp
    ws2_32)

set_target_properties(ijl15 PROPERTIES
    OUTPUT_NAME "ijl15"
    DEBUG_POSTFIX "")

target_sources(ijl15 PRIVATE ../DFLegacy.Client/player_runtime.cpp ../DFLegacy.Client/client_paths.cpp
    ../DFLegacy.Debug/src/client_state.cpp
    ../DFLegacy.Debug/src/client_state_json.cpp
    ../DFLegacy.Debug/src/client_navigation.cpp
    ../DFLegacy.Debug/src/client_navigation_runtime.cpp
    ../DFLegacy.Debug/src/map_click_feedback.cpp
    ../DFLegacy.Debug/src/client_map_feedback_runtime.cpp
    ../DFLegacy.Debug/src/town_route_catalog.cpp
    ../DFLegacy.Debug/src/town_route_controller.cpp
    ../DFLegacy.Debug/src/client_town_route_contract.cpp
    ../DFLegacy.Debug/src/client_town_route_runtime.cpp
)
target_link_libraries(ijl15 PRIVATE bcrypt detours)

get_target_property(client_sources ijl15 SOURCES)
list(REMOVE_ITEM client_sources src/ijl15.cpp src/ijl15.def src/ijl15_entry.cpp)
add_library(client_patch SHARED ${client_sources}
    ../DFLegacy.Client/client_entry.cpp ../DFLegacy.Client/client.def ../DFLegacy.Client/early_log_redirect.cpp)
set_target_properties(client_patch PROPERTIES OUTPUT_NAME "DFLegacy.Client")
target_compile_definitions(client_patch PRIVATE UNICODE _UNICODE)
target_compile_options(client_patch PRIVATE /W4 /WX /permissive- /EHsc /Brepro)
target_link_options(client_patch PRIVATE /Brepro /INCREMENTAL:NO /PDBALTPATH:DFLegacy.Client.pdb)
target_include_directories(client_patch PRIVATE include)
target_link_libraries(client_patch PRIVATE detours ole32 psapi shlwapi windowscodecs winhttp ws2_32 bcrypt)

add_custom_command(TARGET ijl15 POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${CMAKE_CURRENT_SOURCE_DIR}/Config.ini"
        "$<TARGET_FILE_DIR:ijl15>/Config.ini")

include(CTest)
if(BUILD_TESTING)
    add_executable(performance_text_lifetime_tests tests/performance_text_lifetime_tests.cpp src/memory_patch.cpp)
    target_compile_options(performance_text_lifetime_tests PRIVATE /W4 /WX /permissive- /EHsc)
    target_link_options(performance_text_lifetime_tests PRIVATE /DYNAMICBASE:NO /BASE:0x10000000)
    target_link_libraries(performance_text_lifetime_tests PRIVATE psapi)
    add_test(NAME performance_text_lifetime COMMAND performance_text_lifetime_tests)
    add_executable(loot_gather_tests tests/loot_gather_tests.cpp src/loot_gather.cpp)
    target_compile_options(loot_gather_tests PRIVATE /W4 /WX /permissive- /EHsc)
    add_test(NAME loot_gather COMMAND loot_gather_tests)
    add_executable(loot_gather_hook_tests tests/loot_gather_hook_tests.cpp src/loot_gather.cpp src/memory_patch.cpp ../DFLegacy.Client/client_paths.cpp)
    target_compile_options(loot_gather_hook_tests PRIVATE /W4 /WX /permissive- /EHsc)
    target_link_options(loot_gather_hook_tests PRIVATE /DYNAMICBASE:NO /BASE:0x10000000)
    add_test(NAME loot_gather_hook_abi COMMAND loot_gather_hook_tests)
endif()
if(BUILD_TESTING)
    add_executable(native_incident_tests tests/incident_tests.cpp src/native_incidents.cpp src/native_capture.cpp src/memory_patch.cpp ../DFLegacy.Client/client_paths.cpp)
    target_link_libraries(native_incident_tests PRIVATE detours bcrypt ws2_32)
    target_include_directories(native_incident_tests PRIVATE src)
    target_compile_options(native_incident_tests PRIVATE /W4 /WX /EHsc)
    target_link_options(native_incident_tests PRIVATE /BASE:0x10000000 /DYNAMICBASE:NO)
    find_package(Python3 COMPONENTS Interpreter REQUIRED)
    add_test(NAME native_incidents COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/tests/test_incidents.py" "$<TARGET_FILE:native_incident_tests>")
    add_subdirectory(../../tools/debug/crash_capture "${CMAKE_CURRENT_BINARY_DIR}/crash_capture")
    add_library(native_fault_module SHARED tests/fault_module.cpp)
    set_target_properties(native_fault_module PROPERTIES OUTPUT_NAME "DFLegacy.Client" RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/fault-module/$<CONFIG>" ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/fault-module/$<CONFIG>" PDB_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/fault-module/$<CONFIG>")
    target_compile_options(native_fault_module PRIVATE /O2 /Zi /W4 /WX)
    target_link_options(native_fault_module PRIVATE /DEBUG:FULL /OPT:REF /OPT:ICF /INCREMENTAL:NO /PDBALTPATH:DFLegacy.Client.pdb)
    add_dependencies(native_incident_tests native_fault_module)
    add_executable(native_capture_helper_fixture tests/capture_helper_fixture.cpp ../../tools/debug/crash_capture/local_capture.cpp)
    target_compile_definitions(native_capture_helper_fixture PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_compile_options(native_capture_helper_fixture PRIVATE /W4 /WX)
    target_link_libraries(native_capture_helper_fixture PRIVATE psapi shell32 ole32 advapi32 bcrypt)
    add_library(original_game_probe SHARED tests/original_game_probe.cpp tests/original_game_probe.def src/native_capture.cpp)
    target_include_directories(original_game_probe PRIVATE src)
    target_compile_options(original_game_probe PRIVATE /W4 /WX)
    target_link_libraries(original_game_probe PRIVATE detours bcrypt)
    add_executable(original_game_host tests/original_game_host.cpp)
    target_compile_options(original_game_host PRIVATE /W4 /WX)
    target_link_libraries(original_game_host PRIVATE detours)
    add_test(NAME native_capture COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/tests/test_native_capture.py" "$<TARGET_FILE:native_incident_tests>" "$<TARGET_FILE:native_capture_helper_fixture>")
    add_executable(payment_window_layout_tests tests/payment_window_layout_tests.cpp)
    target_compile_options(payment_window_layout_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    add_test(NAME payment_window_layout COMMAND payment_window_layout_tests)
    add_library(debug_loading_fixture SHARED ../DFLegacy.Client/tests/debug_loading_fixture.cpp)
    add_executable(debug_loading_tests ../DFLegacy.Client/tests/debug_loading_tests.cpp
        src/ijl15_config.cpp src/presentation_scaling.cpp ../DFLegacy.Client/client_paths.cpp)
    target_compile_options(debug_loading_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_libraries(debug_loading_tests PRIVATE winhttp)
    foreach(case test-missing test-0 normal-1 legacy-0 legacy-1)
        string(REPLACE "-" ";" parts "${case}")
        add_test(NAME debug_loading_${case} COMMAND debug_loading_tests $<TARGET_FILE:debug_loading_fixture> ${parts})
    endforeach()
    add_executable(inventory_tab_tests tests/inventory_tab_tests.cpp src/inventory_tab.cpp src/memory_patch.cpp)
    target_include_directories(inventory_tab_tests PRIVATE src)
    target_compile_options(inventory_tab_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_options(inventory_tab_tests PRIVATE /DYNAMICBASE:NO /BASE:0x10000000)
    add_test(NAME inventory_tab_preservation COMMAND inventory_tab_tests)
    add_test(NAME inventory_tab_original_control COMMAND inventory_tab_tests --original-control)
    add_executable(warehouse_upgrade_button_tests tests/warehouse_upgrade_button_tests.cpp src/warehouse_upgrade_button.cpp src/memory_patch.cpp)
    target_include_directories(warehouse_upgrade_button_tests PRIVATE src)
    target_compile_options(warehouse_upgrade_button_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_options(warehouse_upgrade_button_tests PRIVATE /DYNAMICBASE:NO /BASE:0x10000000)
    add_test(NAME warehouse_upgrade_button COMMAND warehouse_upgrade_button_tests)
    add_test(NAME warehouse_upgrade_original_control COMMAND warehouse_upgrade_button_tests --original-control)
endif()
add_executable(client_bootstrap ../DFLegacy.Client/bootstrap_main.cpp)
set_target_properties(client_bootstrap PROPERTIES OUTPUT_NAME "DFLegacy.Bootstrap32")
target_compile_options(client_bootstrap PRIVATE /W4 /WX /utf-8 /EHsc /Brepro)
target_link_options(client_bootstrap PRIVATE /Brepro /INCREMENTAL:NO /PDBALTPATH:DFLegacy.Bootstrap32.pdb)
target_link_libraries(client_bootstrap PRIVATE detours bcrypt)
target_sources(client_patch PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/detours_notices.rc")
target_sources(client_bootstrap PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/detours_notices.rc")
if(BUILD_TESTING)
    add_executable(bootstrap_diagnostic ../DFLegacy.Client/bootstrap_main.cpp)
    target_compile_definitions(bootstrap_diagnostic PRIVATE FIXTURE_BOOTSTRAP)
    target_compile_options(bootstrap_diagnostic PRIVATE /W4 /WX /utf-8 /EHsc /Brepro)
    target_link_libraries(bootstrap_diagnostic PRIVATE detours bcrypt)
    add_library(bootstrap_original_ijl SHARED ../DFLegacy.Client/tests/bootstrap_fixture.cpp)
    target_compile_definitions(bootstrap_original_ijl PRIVATE FIXTURE_ORIGINAL_IJL)
    set_target_properties(bootstrap_original_ijl PROPERTIES OUTPUT_NAME "ijl15"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/fixture-original"
        ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/fixture-original")
    add_library(bootstrap_rs SHARED ../DFLegacy.Client/tests/rs_log_fixture.cpp)
    set_target_properties(bootstrap_rs PROPERTIES OUTPUT_NAME "RS")
    add_executable(bootstrap_fixture ../DFLegacy.Client/tests/bootstrap_fixture.cpp)
    target_link_libraries(bootstrap_fixture PRIVATE bootstrap_original_ijl bootstrap_rs)
    # Match the supported client's fixed image layout. A randomized synthetic
    # EXE below 0x80000 can hit the pinned Detours trampoline allocation floor;
    # that layout is outside the absolute-address client runtime contract.
    target_link_options(bootstrap_fixture PRIVATE /DYNAMICBASE:NO /BASE:0x400000)
    add_executable(bootstrap_winmain_fixture WIN32 ../DFLegacy.Client/tests/bootstrap_fixture.cpp)
    target_compile_definitions(bootstrap_winmain_fixture PRIVATE FIXTURE_WINMAIN)
    target_compile_options(bootstrap_winmain_fixture PRIVATE /W4 /WX /utf-8 /EHsc /Brepro)
    target_link_libraries(bootstrap_winmain_fixture PRIVATE bootstrap_original_ijl bootstrap_rs)
    target_link_options(bootstrap_winmain_fixture PRIVATE /DYNAMICBASE:NO /BASE:0x400000)
    add_library(bootstrap_fixture_dll SHARED ../DFLegacy.Client/tests/bootstrap_fixture.cpp ../DFLegacy.Client/bootstrap_runtime.cpp)
    target_sources(bootstrap_fixture_dll PRIVATE ../DFLegacy.Client/client_paths.cpp ../DFLegacy.Client/client_entry.cpp ../DFLegacy.Client/client.def ../DFLegacy.Client/early_log_redirect.cpp)
    target_link_libraries(bootstrap_fixture_dll PRIVATE detours)
    target_compile_definitions(bootstrap_fixture_dll PRIVATE FIXTURE_DLL)
    set_target_properties(bootstrap_fixture_dll PROPERTIES OUTPUT_NAME "DFLegacy.Client"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/fixture"
        ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/fixture")
    add_executable(bootstrap_tests ../DFLegacy.Client/tests/bootstrap_tests.cpp)
    foreach(target bootstrap_fixture bootstrap_fixture_dll bootstrap_tests bootstrap_original_ijl)
        target_compile_options(${target} PRIVATE /W4 /WX /utf-8 /EHsc /Brepro)
    endforeach()
    target_link_libraries(bootstrap_tests PRIVATE bcrypt)
    add_test(NAME bootstrap_raw_winmain_tail COMMAND bootstrap_tests raw-tail $<TARGET_FILE:client_bootstrap> $<TARGET_FILE:bootstrap_winmain_fixture> $<TARGET_FILE:bootstrap_fixture_dll> $<TARGET_FILE:client_patch> $<TARGET_FILE:bootstrap_original_ijl> $<TARGET_FILE:bootstrap_rs>)
    set(bootstrap_cases initializes_before_entry_outside_loader_lock failure_never_runs_entry rejects_hash_architecture_and_unknown_image unicode_path_preserves_files parent_eof_owns_cleanup ack_releases_only_owned_child)
    set(bootstrap_args init failure reject unicode eof mismatch)
    foreach(index RANGE 0 5)
        list(GET bootstrap_cases ${index} name)
        list(GET bootstrap_args ${index} arg)
        add_test(NAME bootstrap_${name} COMMAND bootstrap_tests ${arg} $<TARGET_FILE:client_bootstrap> $<TARGET_FILE:bootstrap_fixture> $<TARGET_FILE:bootstrap_fixture_dll> $<TARGET_FILE:client_patch> $<TARGET_FILE:bootstrap_original_ijl> $<TARGET_FILE:bootstrap_rs>)
    endforeach()
    foreach(arg rs-log timeout uncommitted killed unknown location eof-initializing ack-trailing ack-trailing-closed ack-trailing-binary ack-closed)
        add_test(NAME bootstrap_${arg} COMMAND bootstrap_tests ${arg} $<TARGET_FILE:client_bootstrap> $<TARGET_FILE:bootstrap_fixture> $<TARGET_FILE:bootstrap_fixture_dll> $<TARGET_FILE:client_patch> $<TARGET_FILE:bootstrap_original_ijl> $<TARGET_FILE:bootstrap_rs>)
    endforeach()
endif()
if(BUILD_TESTING)
    add_executable(pe_import_tests tests/pe_import_tests.cpp)
    target_compile_options(pe_import_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    add_test(NAME d3d8_named_import COMMAND pe_import_tests)
    add_executable(d3d8_hook_tests tests/d3d8_hook_tests.cpp src/ijl15_config.cpp src/presentation_scaling.cpp src/d3d8_hooks.cpp src/d3d8_presenter.cpp src/window_input_scaling.cpp src/memory_patch.cpp)
    target_compile_options(d3d8_hook_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_libraries(d3d8_hook_tests PRIVATE user32 winhttp psapi)
    add_test(NAME d3d8_hook_lifecycle COMMAND d3d8_hook_tests)
    add_executable(presentation_scaling_tests tests/presentation_scaling_tests.cpp src/presentation_scaling.cpp)
    target_compile_options(presentation_scaling_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    add_test(NAME presentation_scaling_policy COMMAND presentation_scaling_tests)
    add_executable(d3d8_presenter_tests tests/d3d8_presenter_tests.cpp src/d3d8_presenter.cpp src/presentation_scaling.cpp src/d3d8_hooks.cpp)
    target_compile_options(d3d8_presenter_tests PRIVATE /W4 /WX /utf-8 /EHsc /Zi)
    target_link_options(d3d8_presenter_tests PRIVATE /DEBUG)
    target_link_libraries(d3d8_presenter_tests PRIVATE user32 dbghelp)
    option(DFLEGACY_ENABLE_GPU_TESTS "Run D3D8 tests in an interactive graphics session" OFF)
    if(DFLEGACY_ENABLE_GPU_TESTS)
        add_test(NAME d3d8_presentation_gpu COMMAND d3d8_presenter_tests)
    endif()
    # The same test source also builds from tests/DFLegacy.Ijl15.Tests.vcxproj,
    # which is what DFLegacy.Emulator.slnx uses. CMake is not installed on every
    # machine that has to run these (measured 2026-10-04: no cmake.exe in PATH or
    # under the VS install, so build-native.ps1 -RunTests cannot start), while
    # MSBuild and the x86 toolset are present. Keep both lists in step: a source
    # added here has to be added there as well.
    add_executable(ijl15_tests
        src/auto_input.cpp
        src/auto_input.h
        src/external_string_table.cpp
        src/external_string_table.h
        src/ijl15_config.cpp
        src/ijl15_config.h
        src/memory_patch.cpp
        src/memory_patch.h
        src/net_dump.cpp
        src/net_dump.h
        src/output_paths.cpp
        src/output_paths.h
        src/stage_follow.cpp
        src/stage_follow.h
        tests/stage_follow_tests.cpp
        src/summon_owner_death.cpp
        src/summon_owner_death.h
        tests/summon_owner_death_tests.cpp
        src/party_combat_detail.cpp
        src/party_combat_detail.h
        tests/party_combat_detail_tests.cpp
        src/mage_skill_contact.cpp
        src/mage_skill_contact.h
        tests/mage_skill_contact_tests.cpp
        src/boss_room_death.cpp
        src/boss_room_death.h
        src/loot_gather.cpp
        src/loot_gather_hooks.cpp
        tests/boss_room_death_tests.cpp
        src/creature_gift_row.cpp
        src/creature_gift_row.h
        src/channel_switch.cpp
        src/channel_switch.h
        src/channel_reentry.cpp
        src/channel_reentry.h
        tests/channel_reentry_tests.cpp
        src/relay_send_queue.cpp
        src/relay_send_queue.h
        tests/relay_send_queue_tests.cpp
        tests/channel_switch_tests.cpp
        tests/creature_gift_row_tests.cpp
        src/countdown_coin.cpp
        src/countdown_coin.h
        tests/countdown_coin_tests.cpp
        src/dungeon_tooltip.cpp
        src/dungeon_tooltip.h
        tests/dungeon_tooltip_tests.cpp
        src/priest_heal_target.cpp
        src/priest_heal_target.h
        tests/priest_heal_target_tests.cpp
        src/ground_item_removal.cpp
        src/ground_item_removal.h
        tests/ground_item_removal_tests.cpp
        src/motion_horizon.cpp
        src/motion_horizon.h
        tests/motion_horizon_tests.cpp
        tests/native_loop_probe_tests.cpp
        src/party_card_display.cpp
        src/party_card_display.h
        tests/party_card_display_tests.cpp
        tests/auto_input_drag_tests.cpp
        tests/ijl15_tests.cpp)
    target_sources(ijl15_tests PRIVATE tests/client_service_url_tests.cpp src/presentation_scaling.cpp src/d3d8_hooks.cpp src/d3d8_presenter.cpp src/window_input_scaling.cpp)
    target_compile_options(ijl15_tests PRIVATE /W4 /WX /permissive- /EHsc)
    target_include_directories(ijl15_tests PRIVATE include src)
    target_link_libraries(ijl15_tests PRIVATE shlwapi ws2_32 user32 winhttp)
    add_dependencies(ijl15_tests ijl15)
    # The tests load ijl15.dll by bare name, so it has to sit beside them.
    add_custom_command(TARGET ijl15_tests POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:ijl15>"
            "$<TARGET_FILE_DIR:ijl15_tests>/ijl15.dll")
    add_test(NAME ijl15_compatibility COMMAND ijl15_tests)
    set_tests_properties(ijl15_compatibility PROPERTIES WORKING_DIRECTORY "$<TARGET_FILE_DIR:ijl15_tests>")
endif()

# Keep explicit source lists aligned with the native vcxproj targets.
add_library(df_debug SHARED
    ../DFLegacy.Debug/src/debug_overlay.cpp
    ../DFLegacy.Debug/src/frame_profile.cpp
    ../DFLegacy.Debug/src/gameplay_trace.cpp
    ../DFLegacy.Debug/src/native_loop_profile.cpp
    ../DFLegacy.Debug/src/client_test_control.cpp
    ../DFLegacy.Debug/src/client_test_control_runtime.cpp
    ../DFLegacy.Debug/src/client_navigation.cpp
    ../DFLegacy.Debug/src/shared_state_bridge.cpp
    ../DFLegacy.Debug/src/client_state.cpp
    ../DFLegacy.Debug/src/client_party.cpp
    ../DFLegacy.Debug/src/client_party_runtime.cpp
    ../DFLegacy.Debug/src/client_state_json.cpp
    ../DFLegacy.Debug/src/debug_window.cpp
    ../DFLegacy.Debug/src/debug.def)
set_target_properties(df_debug PROPERTIES OUTPUT_NAME "debug")
target_compile_definitions(df_debug PRIVATE UNICODE _UNICODE DFL_DEBUG_STATE_CONSUMER)
target_compile_options(df_debug PRIVATE /W4 /WX /utf-8 /EHsc /Brepro)
target_link_options(df_debug PRIVATE /Brepro /INCREMENTAL:NO /PDBALTPATH:debug.pdb)
target_link_libraries(df_debug PRIVATE user32 bcrypt)

if(BUILD_TESTING)
    find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
    add_executable(df_debug_tests
    ../DFLegacy.Debug/tests/gameplay_trace_tests.cpp
    ../DFLegacy.Debug/tests/native_loop_profile_tests.cpp
    ../DFLegacy.Debug/tests/../src/native_loop_profile.cpp
    ../DFLegacy.Debug/tests/debug_tests.cpp
    ../DFLegacy.Debug/tests/frame_profile_tests.cpp
    ../DFLegacy.Debug/tests/frame_profile_runtime_tests.cpp
    ../DFLegacy.Debug/tests/client_test_control_tests.cpp
    ../DFLegacy.Debug/tests/client_test_control_runtime_tests.cpp
    ../DFLegacy.Debug/tests/../src/client_test_control.cpp
    ../DFLegacy.Debug/tests/client_state_tests.cpp
    ../DFLegacy.Debug/tests/client_party_tests.cpp
    ../DFLegacy.Debug/tests/client_party_runtime_tests.cpp
    ../DFLegacy.Debug/tests/../src/client_party.cpp
    ../DFLegacy.Debug/tests/client_navigation_tests.cpp
    ../DFLegacy.Debug/tests/map_click_feedback_tests.cpp
    ../DFLegacy.Debug/tests/map_click_feedback_runtime_tests.cpp
    ../DFLegacy.Debug/tests/../src/map_click_feedback.cpp
    ../DFLegacy.Debug/tests/client_navigation_runtime_tests.cpp
    ../DFLegacy.Debug/tests/../src/client_navigation.cpp
    ../DFLegacy.Debug/tests/../src/town_route_catalog.cpp
    ../DFLegacy.Debug/tests/../src/town_route_controller.cpp
    ../DFLegacy.Debug/tests/../src/client_town_route_contract.cpp
    ../DFLegacy.Debug/tests/town_route_io_tests.cpp
    ../DFLegacy.Debug/tests/town_route_catalog_tests.cpp
    ../DFLegacy.Debug/tests/town_route_controller_tests.cpp
    ../DFLegacy.Debug/tests/town_route_runtime_tests.cpp
    ../DFLegacy.Debug/tests/../src/client_state.cpp
    ../DFLegacy.Debug/tests/../src/client_state_json.cpp)
    target_compile_definitions(df_debug_tests PRIVATE UNICODE _UNICODE)
    target_compile_options(df_debug_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_libraries(df_debug_tests PRIVATE user32 bcrypt)
    add_dependencies(df_debug_tests df_debug)
    add_custom_command(TARGET df_debug_tests POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:df_debug>" "$<TARGET_FILE_DIR:df_debug_tests>/debug.dll")
    set(DEBUG_OVERLAY_FIXTURE "${CMAKE_CURRENT_BINARY_DIR}/debug-overlay-fixture.bin")
    add_test(NAME debug_overlay_fixture COMMAND "${DOTNET_EXECUTABLE}" run
        --project "${CMAKE_CURRENT_SOURCE_DIR}/../../tests/DFLegacy.SmokeTests"
        -c $<CONFIG> -- --debug-overlay)
    set_tests_properties(debug_overlay_fixture PROPERTIES
        FIXTURES_SETUP DebugOverlayFixture
        ENVIRONMENT "DFLEGACY_DEBUG_TEST_FIXTURE=${DEBUG_OVERLAY_FIXTURE}")
    add_test(NAME debug_overlay COMMAND df_debug_tests "${DEBUG_OVERLAY_FIXTURE}")
    set_tests_properties(debug_overlay PROPERTIES
        FIXTURES_REQUIRED DebugOverlayFixture
        WORKING_DIRECTORY "$<TARGET_FILE_DIR:df_debug_tests>")
    foreach(debug_group IN ITEMS native-loop gameplay-trace frame-profile client-test-control map-feedback client-party client-state client-navigation town-route)
        add_test(NAME debug_${debug_group} COMMAND df_debug_tests --${debug_group})
        set_tests_properties(debug_${debug_group} PROPERTIES WORKING_DIRECTORY "$<TARGET_FILE_DIR:df_debug_tests>")
    endforeach()
    add_test(NAME debug_gameplay_abi COMMAND df_debug_tests --gameplay-abi "$<TARGET_FILE:df_debug>")
    set_tests_properties(debug_gameplay_abi PROPERTIES WORKING_DIRECTORY "$<TARGET_FILE_DIR:df_debug_tests>")
endif()

if(BUILD_TESTING)
    add_executable(client_paths_tests ../DFLegacy.Client/tests/client_paths_tests.cpp ../DFLegacy.Client/client_paths.cpp src/ijl15_config.cpp src/presentation_scaling.cpp)
    target_link_libraries(client_paths_tests PRIVATE winhttp)
    target_compile_options(client_paths_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    add_test(NAME client_paths COMMAND client_paths_tests)
endif()

if(BUILD_TESTING)
    add_executable(player_runtime_tests ../DFLegacy.Client/tests/player_runtime_tests.cpp ../DFLegacy.Client/player_runtime.cpp ../DFLegacy.Client/client_paths.cpp)
    target_compile_options(player_runtime_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_options(player_runtime_tests PRIVATE /DYNAMICBASE:NO /BASE:0x10000000)
    target_link_libraries(player_runtime_tests PRIVATE psapi)
    add_dependencies(player_runtime_tests df_debug)
    add_test(NAME player_runtime COMMAND player_runtime_tests)
endif()

if(BUILD_TESTING)
    foreach(target d3d8_hook_tests d3d8_presenter_tests ijl15_tests)
        target_sources(${target} PRIVATE ../DFLegacy.Client/client_paths.cpp)
    endforeach()
    target_sources(ijl15_tests PRIVATE ../DFLegacy.Client/bootstrap_runtime.cpp)
endif()

# Private production symbols for optimized Release. /DEBUG changes linker
# optimization defaults, so restore REF/ICF explicitly after it.
foreach(symbol_target IN ITEMS ijl15 client_patch df_debug client_bootstrap)
    target_compile_options(${symbol_target} PRIVATE "$<$<CONFIG:Release>:/Zi>")
    target_link_options(${symbol_target} PRIVATE "$<$<CONFIG:Release>:/DEBUG:FULL>" "$<$<CONFIG:Release>:/OPT:REF>" "$<$<CONFIG:Release>:/OPT:ICF>")
endforeach()
