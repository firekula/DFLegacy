cmake_minimum_required(VERSION 3.24)

project(DFLegacy.Ijl15 LANGUAGES CXX)

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

add_library(ijl15 SHARED
    include/ijl15.h
    src/auto_input.cpp
    src/auto_input.h
    src/debug_module.cpp
    src/debug_module.h
    src/client_debug_state.h
    src/boss_room_death.cpp
    src/boss_room_death.h
    src/creature_gift_row.cpp
    src/creature_gift_row.h
    src/countdown_coin.cpp
    src/countdown_coin.h
    src/channel_switch.cpp
    src/channel_switch.h
    src/channel_reentry.cpp
    src/channel_reentry.h
    src/relay_send_queue.cpp
    src/relay_send_queue.h
    src/ground_item_removal.cpp
    src/ground_item_removal.h
    src/party_card_display.cpp
    src/party_card_display.h
    src/d3d8_scaler.cpp
    src/d3d8_scaler.h
    src/dnf_patches.cpp
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
    src/window_scaling.h)

target_compile_definitions(ijl15 PRIVATE
    IJL15_EXPORTS
    UNICODE
    _UNICODE)
target_compile_options(ijl15 PRIVATE
    /W4
    /WX
    /permissive-
    /EHsc)
target_include_directories(ijl15 PUBLIC include)
target_link_libraries(ijl15 PRIVATE
    ole32
    psapi
    shlwapi
    windowscodecs
    ws2_32)

set_target_properties(ijl15 PROPERTIES
    OUTPUT_NAME "ijl15"
    DEBUG_POSTFIX "")

add_custom_command(TARGET ijl15 POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${CMAKE_CURRENT_SOURCE_DIR}/Config.ini"
        "$<TARGET_FILE_DIR:ijl15>/Config.ini")

include(CTest)
if(BUILD_TESTING)
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
        src/priest_heal_target.cpp
        src/priest_heal_target.h
        tests/priest_heal_target_tests.cpp
        src/ground_item_removal.cpp
        src/ground_item_removal.h
        tests/ground_item_removal_tests.cpp
        src/party_card_display.cpp
        src/party_card_display.h
        tests/party_card_display_tests.cpp
        tests/ijl15_tests.cpp)
    target_compile_options(ijl15_tests PRIVATE /W4 /WX /permissive- /EHsc)
    target_include_directories(ijl15_tests PRIVATE include src)
    target_link_libraries(ijl15_tests PRIVATE shlwapi ws2_32 user32)
    add_dependencies(ijl15_tests ijl15)
    # The tests load ijl15.dll by bare name, so it has to sit beside them.
    add_custom_command(TARGET ijl15_tests POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:ijl15>"
            "$<TARGET_FILE_DIR:ijl15_tests>/ijl15.dll")
    add_test(NAME ijl15_compatibility COMMAND ijl15_tests)
endif()

# Optional game diagnostic module, loaded by ijl15 only when enabled.
add_library(df_debug SHARED ../DFLegacy.Debug/src/debug_overlay.cpp ../DFLegacy.Debug/src/debug.def)
set_target_properties(df_debug PROPERTIES OUTPUT_NAME "debug")
target_compile_options(df_debug PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(df_debug PRIVATE user32)

if(BUILD_TESTING)
    add_executable(df_debug_tests ../DFLegacy.Debug/tests/debug_tests.cpp)
    target_compile_options(df_debug_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_libraries(df_debug_tests PRIVATE user32)
    add_dependencies(df_debug_tests df_debug)
    add_custom_command(TARGET df_debug_tests POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:df_debug>" "$<TARGET_FILE_DIR:df_debug_tests>/debug.dll")
endif()
