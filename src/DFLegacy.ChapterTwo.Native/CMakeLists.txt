cmake_minimum_required(VERSION 3.24)
project(DFLegacyChapterTwoNative LANGUAGES CXX)

if(NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "Chapter Two native components require the x86 MSVC toolchain.")
endif()

set(IJL "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Ijl15/src")
set(CLIENT "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Client")
set(DEBUG_NATIVE "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Debug/src")
add_library(chapter_two_presentation STATIC
    ChapterTwoPresentation.cpp
    ChapterTwoPerformance.cpp
    ChapterTwoPaymentProtocol.cpp
    ChapterTwoPaymentNavigation.cpp
    ChapterTwoPaymentViewport.cpp
    ChapterTwoNpcDialogue.cpp
    ChapterTwoWarehouseUpgrade.cpp
    ChapterTwoInventoryTab.cpp
    ChapterTwoHudPortraitCache.cpp
    ChapterTwoFreeCardDisplay.cpp
    ChapterTwoParty.cpp
    ChapterTwoTownMap.cpp
    "${DEBUG_NATIVE}/town_route_catalog.cpp"
    "${DEBUG_NATIVE}/town_route_controller.cpp"
    "${IJL}/d3d8_hooks.cpp"
    "${IJL}/d3d8_presenter.cpp"
    "${IJL}/presentation_scaling.cpp"
    "${IJL}/window_input_scaling.cpp"
    "${IJL}/memory_patch.cpp"
    "${CLIENT}/client_paths.cpp")
target_include_directories(chapter_two_presentation PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_include_directories(chapter_two_presentation PRIVATE "${DEBUG_NATIVE}")
target_compile_features(chapter_two_presentation PUBLIC cxx_std_17)
target_compile_options(chapter_two_presentation PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_presentation PUBLIC user32 psapi urlmon ole32 uuid bcrypt)

add_executable(chapter_two_presentation_tests presentation_adapter_tests.cpp)
target_compile_options(chapter_two_presentation_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_presentation_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_presentation_policy_tests
    ../DFLegacy.Ijl15/tests/presentation_scaling_tests.cpp
    "${IJL}/presentation_scaling.cpp")
target_compile_features(chapter_two_presentation_policy_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_presentation_policy_tests PRIVATE /W4 /WX /utf-8 /EHsc)

add_executable(chapter_two_party_relay_tests party_relay_host_tests.cpp)
target_compile_options(chapter_two_party_relay_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_libraries(chapter_two_party_relay_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_town_map_tests town_map_tests.cpp)
target_compile_options(chapter_two_town_map_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_town_map_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_town_map_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_inventory_tab_tests inventory_tab_tests.cpp)
target_compile_options(chapter_two_inventory_tab_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_inventory_tab_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_inventory_tab_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_hud_portrait_baseline_tests hud_portrait_cache_tests.cpp)
target_compile_definitions(chapter_two_hud_portrait_baseline_tests PRIVATE CHAPTER_TWO_HUD_BASELINE)
target_compile_options(chapter_two_hud_portrait_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_hud_portrait_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_hud_portrait_baseline_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_hud_portrait_cache_tests hud_portrait_cache_tests.cpp)
target_compile_options(chapter_two_hud_portrait_cache_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_hud_portrait_cache_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_hud_portrait_cache_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_free_card_baseline_tests free_card_display_tests.cpp)
target_compile_definitions(chapter_two_free_card_baseline_tests PRIVATE CHAPTER_TWO_CARDS_BASELINE)
target_compile_options(chapter_two_free_card_baseline_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_free_card_baseline_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)

add_executable(chapter_two_free_card_display_tests free_card_display_tests.cpp)
target_compile_options(chapter_two_free_card_display_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_free_card_display_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_free_card_display_tests PRIVATE chapter_two_presentation)

add_executable(chapter_two_performance_text_lifetime_tests performance_text_lifetime_tests.cpp)
target_compile_features(chapter_two_performance_text_lifetime_tests PRIVATE cxx_std_17)
target_compile_options(chapter_two_performance_text_lifetime_tests PRIVATE /W4 /WX /utf-8 /EHsc)
target_link_options(chapter_two_performance_text_lifetime_tests PRIVATE /BASE:0x10000 /DYNAMICBASE:NO)
target_link_libraries(chapter_two_performance_text_lifetime_tests PRIVATE psapi)

if(DEFINED CHAPTER_TWO_HOST_SOURCE)
    add_executable(chapter_two_presentation_host "${CHAPTER_TWO_HOST_SOURCE}")
    target_compile_features(chapter_two_presentation_host PRIVATE cxx_std_17)
    target_link_options(chapter_two_presentation_host PRIVATE /BASE:0x10000 /SAFESEH:NO /MAP)
    target_link_libraries(chapter_two_presentation_host PRIVATE chapter_two_presentation ws2_32)
endif()
