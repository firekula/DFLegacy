# NTSC and CRT shader sources

The GLSL files are from [libretro/glsl-shaders at a35069864b4bf30f8c8e740d57206543fb7e745e](https://github.com/libretro/glsl-shaders/tree/a35069864b4bf30f8c8e740d57206543fb7e745e).

- `ntsc-pass1.glsl` and `ntsc-pass2.glsl` are the NTSC-Adaptive shaders based on Hans-Kristian Arntzen's NTSC shader. [Themaister's original publication](https://forums.libretro.com/t/ntsc-shader/305) specifies GPLv3. The preset comes from `ntsc/ntsc-adaptive.glslp`. The full GPLv3 text is in [the bundled GPL license](../../../third_party/xbrz/1.9/License.txt).
- `crt-lottes.glsl` is Timothy Lottes' public-domain CRT shader from `crt/shaders/crt-lottes.glsl`.

`../ChapterTwoNtscShaders.h` adapts the two-phase NTSC path, the flat Lottes beam, bloom and aperture grille to HLSL. The game input is 640×480. NTSC uses a signed floating-point intermediate at four times the original width and decodes to two times the original width, with the original row count throughout. The CRT result is rasterized at 640×480 and then passed to the game's window scaler. The presets use fixed field zero, as in the default static browser preview. RGB uses the CRT stage directly.
