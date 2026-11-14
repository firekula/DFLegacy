cmake_minimum_required(VERSION 3.24)
project(DFLegacyChapterTwoNative LANGUAGES CXX RC)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(xbrz_vendor STATIC ../../third_party/xbrz/1.9/xbrz.cpp)
set_target_properties(xbrz_vendor PROPERTIES CXX_STANDARD 23 CXX_STANDARD_REQUIRED ON)
target_include_directories(xbrz_vendor PUBLIC ../../third_party/xbrz/1.9)
target_compile_options(xbrz_vendor PRIVATE /utf-8 /EHsc)
add_library(xbrz_frame STATIC ../DFLegacy.Ijl15/src/xbrz_frame.cpp)
target_compile_features(xbrz_frame PUBLIC cxx_std_17)
target_compile_options(xbrz_frame PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(xbrz_frame PUBLIC xbrz_vendor)
add_executable(chapter_two_xbrz_frame_tests ../DFLegacy.Ijl15/tests/xbrz_frame_tests.cpp)
target_compile_options(chapter_two_xbrz_frame_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_xbrz_frame_tests PRIVATE xbrz_frame)

add_executable(chapter_two_relay_endpoint_tests relay_endpoint_tests.cpp)
target_compile_features(chapter_two_relay_endpoint_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_relay_endpoint_tests PRIVATE /W4 /utf-8 /EHsc)
target_link_libraries(chapter_two_relay_endpoint_tests PRIVATE ws2_32)

add_executable(chapter_two_store_title_baseline_tests store_title_tests.cpp)
target_compile_definitions(chapter_two_store_title_baseline_tests PRIVATE CHAPTER_TWO_STORE_TITLE_BASELINE)
target_compile_features(chapter_two_store_title_baseline_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_store_title_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_store_title_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)

if(NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "Chapter Two native components require the x86 MSVC toolchain.")
endif()

set(IJL "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Ijl15/src")
set(CLIENT "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Client")
set(DEBUG_NATIVE "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Debug/src")
set(DFL_CLASSIC_SKIN "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Launcher/Assets/Skin")
set(performance_panel_skin "${CMAKE_CURRENT_BINARY_DIR}/PerformancePanelSkin.rc")
configure_file(PerformancePanelSkin.rc.in "${performance_panel_skin}" @ONLY)
set_source_files_properties("${performance_panel_skin}" PROPERTIES OBJECT_DEPENDS
    "${DFL_CLASSIC_SKIN}/classic-settings-panel.png;${DFL_CLASSIC_SKIN}/classic-crest.png;${DFL_CLASSIC_SKIN}/buton_close00.png;${DFL_CLASSIC_SKIN}/buton_close02.png")
add_library(chapter_two_presentation STATIC
    ChapterTwoPresentation.cpp
    ChapterTwoPerformance.cpp
    ChapterTwoPerformancePanel.cpp
    ChapterTwoSlowFrameTiming.cpp
    ChapterTwoPaymentProtocol.cpp
    ChapterTwoPaymentNavigation.cpp
    ChapterTwoPaymentViewport.cpp
    ChapterTwoNpcDialogue.cpp
    ChapterTwoWarehouseUpgrade.cpp
    ChapterTwoInventoryTab.cpp
    ChapterTwoCreatureTabs.cpp
    ChapterTwoExternalStringTable.cpp
    ChapterTwoMailboxRefresh.cpp
    ChapterTwoJarCapacity.cpp
    ChapterTwoJarFeedback.cpp
    ChapterTwoStoreTitle.cpp
    ChapterTwoQuickStore.cpp
    ChapterTwoCashItemUse.cpp
    ChapterTwoMallCreature.cpp
    ChapterTwoCursorAuto.cpp
    ChapterTwoHudPortraitCache.cpp
    ChapterTwoFreeCardDisplay.cpp
    ChapterTwoParty.cpp
    ChapterTwoRelaySockets.cpp
    ChapterTwoTownMap.cpp
    ChapterTwoLootGather.cpp
    ChapterTwoLootGatherHooks.cpp
    ChapterTwoBackgroundPolicy.cpp
    ChapterTwoMouseWheel.cpp
    ChapterTwoNativeLog.cpp
    ChapterTwoLocalPaths.cpp
    ChapterTwoStartupMutex.cpp
    ChapterTwoType16Audio.cpp
    ChapterTwoPopupAnnouncement.cpp
    ChapterTwoShopLayout.cpp
    ChapterTwoDungeonTooltip.cpp
    ChapterTwoAdultBlood.cpp
    "${DEBUG_NATIVE}/map_click_feedback.cpp"
    "${DEBUG_NATIVE}/town_route_catalog.cpp"
    "${DEBUG_NATIVE}/town_route_controller.cpp"
    "${IJL}/d3d8_hooks.cpp"
    "${IJL}/d3d8_presenter.cpp"
    "${IJL}/presentation_scaling.cpp"
    "${IJL}/window_input_scaling.cpp"
    "${IJL}/external_string_table.cpp"
    "${IJL}/memory_patch.cpp"
    "${CLIENT}/client_paths.cpp")
target_include_directories(chapter_two_presentation PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_include_directories(chapter_two_presentation PRIVATE "${DEBUG_NATIVE}" "${IJL}")
target_compile_features(chapter_two_presentation PUBLIC cxx_std_17)
target_compile_definitions(chapter_two_presentation PRIVATE DFL_CHAPTER_TWO_XBRZ)
target_compile_options(chapter_two_presentation PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_presentation PUBLIC user32 gdi32 psapi urlmon ole32 uuid bcrypt imm32 advapi32 gdiplus ws2_32 xbrz_frame)

add_executable(chapter_two_presentation_tests presentation_adapter_tests.cpp)
target_compile_options(chapter_two_presentation_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_presentation_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_crt_presenter_tests ../DFLegacy.Ijl15/tests/crt_presenter_tests.cpp)
target_compile_options(chapter_two_crt_presenter_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_crt_presenter_tests PRIVATE chapter_two_presentation)
add_executable(chapter_two_xbrz_presenter_tests ../DFLegacy.Ijl15/tests/xbrz_presenter_tests.cpp)
target_compile_options(chapter_two_xbrz_presenter_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_xbrz_presenter_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_performance_window_tests performance_panel_window_tests.cpp "${performance_panel_skin}")
target_compile_options(chapter_two_performance_window_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_performance_window_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_presentation_policy_tests
    ../DFLegacy.Ijl15/tests/presentation_scaling_tests.cpp
    "${IJL}/presentation_scaling.cpp")
target_compile_features(chapter_two_presentation_policy_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_presentation_policy_tests PRIVATE /W4 /WX /utf-8 /EHsc)

add_executable(chapter_two_party_relay_tests party_relay_host_tests.cpp)
target_compile_options(chapter_two_party_relay_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_party_relay_tests PRIVATE chapter_two_presentation ws2_32)

add_executable(chapter_two_town_map_tests town_map_tests.cpp)
target_compile_options(chapter_two_town_map_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_town_map_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_town_map_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_loot_gather_tests loot_gather_tests.cpp)
target_compile_options(chapter_two_loot_gather_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_loot_gather_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_loot_gather_image_tests loot_gather_image_tests.cpp)
target_compile_options(chapter_two_loot_gather_image_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_loot_gather_image_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_loot_gather_hook_tests
    loot_gather_hook_tests.cpp ChapterTwoLootGather.cpp
    "${IJL}/memory_patch.cpp" "${CLIENT}/client_paths.cpp")
target_compile_features(chapter_two_loot_gather_hook_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_loot_gather_hook_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_loot_gather_hook_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_loot_gather_hook_tests PRIVATE user32 psapi)

add_executable(chapter_two_background_policy_tests background_policy_tests.cpp)
target_compile_options(chapter_two_background_policy_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_background_policy_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_mouse_wheel_tests mouse_wheel_tests.cpp)
target_compile_options(chapter_two_mouse_wheel_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_mouse_wheel_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_mouse_wheel_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_native_log_tests native_log_tests.cpp)
target_compile_options(chapter_two_native_log_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_native_log_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_native_log_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_local_path_tests local_path_tests.cpp)
target_compile_options(chapter_two_local_path_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_local_path_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_local_path_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_inventory_tab_tests inventory_tab_tests.cpp)
target_compile_options(chapter_two_inventory_tab_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_inventory_tab_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_inventory_tab_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_creature_tab_tests creature_tab_tests.cpp)
target_compile_options(chapter_two_creature_tab_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_creature_tab_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_creature_tab_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_external_string_table_tests external_string_table_tests.cpp)
target_compile_options(chapter_two_external_string_table_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_external_string_table_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_external_string_table_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_mailbox_refresh_tests mailbox_refresh_tests.cpp)
target_compile_options(chapter_two_mailbox_refresh_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_mailbox_refresh_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_mailbox_refresh_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_mall_creature_tests mall_creature_tests.cpp)
target_compile_options(chapter_two_mall_creature_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_mall_creature_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_mall_creature_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_cash_item_use_tests cash_item_use_tests.cpp)
target_compile_options(chapter_two_cash_item_use_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_cash_item_use_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_cash_item_use_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_cursor_auto_tests cursor_auto_tests.cpp)
target_compile_options(chapter_two_cursor_auto_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_cursor_auto_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_cursor_auto_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_payment_protocol_tests payment_protocol_tests.cpp)
target_compile_features(chapter_two_payment_protocol_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_payment_protocol_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_payment_protocol_tests PRIVATE urlmon ole32 uuid)

foreach(parity_test IN ITEMS type16_audio popup_announcement shop_layout dungeon_tooltip adult_blood)
    add_executable(chapter_two_${parity_test}_tests ${parity_test}_tests.cpp)
    target_compile_options(chapter_two_${parity_test}_tests PRIVATE /W4 /WX /utf-8 /EHsc)
    target_link_options(chapter_two_${parity_test}_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
    target_link_libraries(chapter_two_${parity_test}_tests PRIVATE chapter_two_presentation)
endforeach()

add_executable(chapter_two_hud_portrait_baseline_tests hud_portrait_cache_tests.cpp)
target_compile_definitions(chapter_two_hud_portrait_baseline_tests PRIVATE CHAPTER_TWO_HUD_BASELINE)
target_compile_options(chapter_two_hud_portrait_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_hud_portrait_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_hud_portrait_baseline_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_hud_portrait_cache_tests hud_portrait_cache_tests.cpp)
target_compile_options(chapter_two_hud_portrait_cache_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_hud_portrait_cache_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_hud_portrait_cache_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_hud_portrait_fallback_tests
    hud_portrait_fallback_tests.cpp
    "${IJL}/memory_patch.cpp")
target_compile_features(chapter_two_hud_portrait_fallback_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_hud_portrait_fallback_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_hud_portrait_fallback_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_hud_portrait_fallback_tests PRIVATE psapi user32)

add_executable(chapter_two_free_card_baseline_tests free_card_display_tests.cpp)
target_compile_definitions(chapter_two_free_card_baseline_tests PRIVATE CHAPTER_TWO_CARDS_BASELINE)
target_compile_options(chapter_two_free_card_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_free_card_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)

add_executable(chapter_two_free_card_display_tests free_card_display_tests.cpp)
target_compile_options(chapter_two_free_card_display_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_free_card_display_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_free_card_display_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_performance_text_lifetime_tests
    performance_text_lifetime_tests.cpp ChapterTwoPerformancePanel.cpp ChapterTwoSlowFrameTiming.cpp)
target_compile_features(chapter_two_performance_text_lifetime_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_performance_text_lifetime_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_performance_text_lifetime_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_performance_text_lifetime_tests PRIVATE psapi user32 gdi32 imm32)
target_link_libraries(chapter_two_performance_text_lifetime_tests PRIVATE chapter_two_presentation)

if(DEFINED CHAPTER_TWO_HOST_SOURCE)
    add_executable(chapter_two_presentation_host "${CHAPTER_TWO_HOST_SOURCE}" "${performance_panel_skin}")
    target_compile_features(chapter_two_presentation_host PRIVATE cxx_std_17)
    target_link_options(chapter_two_presentation_host PRIVATE /BASE:0x10000 /SAFESEH:NO /MAP)
    target_link_libraries(chapter_two_presentation_host PRIVATE chapter_two_presentation ws2_32)

    add_executable(chapter_two_keyboard_tests keyboard_state_tests.cpp)
    target_compile_definitions(chapter_two_keyboard_tests PRIVATE CHAPTER_TWO_HOST_SOURCE="${CHAPTER_TWO_HOST_SOURCE}")
    target_compile_options(chapter_two_keyboard_tests PRIVATE /W4 /utf-8 /EHsc)
    target_link_options(chapter_two_keyboard_tests PRIVATE /BASE:0x10000 /SAFESEH:NO)
    target_link_libraries(chapter_two_keyboard_tests PRIVATE chapter_two_presentation ws2_32)
endif()

add_executable(chapter_two_jar_capacity_baseline_tests jar_capacity_tests.cpp)
add_executable(chapter_two_jar_feedback_baseline_tests jar_feedback_tests.cpp)
target_compile_definitions(chapter_two_jar_feedback_baseline_tests PRIVATE CHAPTER_TWO_JAR_FEEDBACK_BASELINE)
target_compile_features(chapter_two_jar_feedback_baseline_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_jar_feedback_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_jar_feedback_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
add_executable(chapter_two_jar_feedback_tests jar_feedback_tests.cpp)
target_compile_options(chapter_two_jar_feedback_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_jar_feedback_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_jar_feedback_tests PRIVATE chapter_two_presentation)
add_executable(chapter_two_store_title_tests store_title_tests.cpp)
target_compile_options(chapter_two_store_title_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_store_title_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_store_title_tests PRIVATE chapter_two_presentation)
add_executable(chapter_two_quick_store_baseline_tests quick_store_tests.cpp)
target_compile_definitions(chapter_two_quick_store_baseline_tests PRIVATE CHAPTER_TWO_QUICK_STORE_BASELINE)
target_compile_features(chapter_two_quick_store_baseline_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_quick_store_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_quick_store_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
add_executable(chapter_two_quick_store_tests quick_store_tests.cpp)
target_compile_options(chapter_two_quick_store_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_quick_store_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_quick_store_tests PRIVATE chapter_two_presentation)
target_compile_definitions(chapter_two_jar_capacity_baseline_tests PRIVATE CHAPTER_TWO_JAR_BASELINE)
target_compile_features(chapter_two_jar_capacity_baseline_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_jar_capacity_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_jar_capacity_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)

add_executable(chapter_two_jar_capacity_tests jar_capacity_tests.cpp)
target_compile_options(chapter_two_jar_capacity_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_jar_capacity_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_jar_capacity_tests PRIVATE chapter_two_presentation)

# Offline startup admission fixture: RED is expected to exit 1, GREEN exits 0.
add_executable(chapter_two_startup_mutex_baseline_tests startup_mutex_tests.cpp)
target_compile_definitions(chapter_two_startup_mutex_baseline_tests PRIVATE STARTUP_MUTEX_BASELINE)
target_compile_features(chapter_two_startup_mutex_baseline_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_startup_mutex_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_startup_mutex_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
add_executable(chapter_two_startup_mutex_tests startup_mutex_tests.cpp)
target_compile_options(chapter_two_startup_mutex_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_startup_mutex_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_startup_mutex_tests PRIVATE chapter_two_presentation)
