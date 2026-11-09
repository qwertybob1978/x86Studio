# DOS display preview and dithering

The DOS designer keeps control geometry in the target's native screen pixels,
but displays the screen in a 960 × 720 preview area. The WPF layout transform
maps target coordinates to this 4:3 physical display rectangle. This corrects
the wide appearance of Hercules 720 × 348 and CGA 640 × 200 without changing
saved coordinates, generated geometry, drag, resize, or snapping units.

The preview draws target-palette colors as solid colors. For Hercules and CGA,
the color selector also offers 8 × 8 ordered-dither mixtures. The preview
selects the closest pair of colors from the target palette and fills an
8 × 8 Bayer threshold tile at the requested coverage. This provides 65
coverage steps for monochrome gray and lets CGA palette colors combine into
additional apparent colors. Exact palette colors remain solid. VGA continues
to use its 16-color palette directly.

These dithered brushes and the LVGL-style WPF widget previews are design-time
simulations. They do not change the generated DOS application; the Hercules
runtime still uses LVGL's 1-bit color depth, and CGA/VGA runtime rendering is
not yet implemented.

## Suggested runtime implementation

Use ordered dithering rather than per-pixel error diffusion on the 8088:

1. Keep the 8 × 8 threshold table and a 256-entry RGB332-to-luminance table in
   conventional memory. At the Hercules flush callback, compare each pixel's
   luminance with the threshold selected by screen `x`/`y` modulo 8, then pack
   the result into the existing 1-bpp HGC row blit. Set LVGL to 8-bit color
   depth for this path so the flush callback retains RGB332 input. LVGL v8.3
   uses an 8-bit `lv_color_t` storage element for both its 1-bit and 8-bit
   depths, so the current 720 × 8 partial buffer does not grow merely from
   changing color depth; the renderer and all call sites still need validation.
2. For CGA 320 × 200, precompute one small entry per RGB332 color containing
   the selected low/high palette indices and a 0–64 coverage. During the flush,
   compare coverage against the ordered threshold and pack each four 2-bit
   pixels into a video byte. Keep palette 0 and palette 1 as distinct target
   profiles. CGA 640 × 200 can use the monochrome path.
3. Keep the lookup and threshold phase anchored to absolute screen coordinates,
   not dirty-rectangle origins, so partial refreshes do not make the pattern
   shimmer. Bypass dithering when the source color exactly matches a hardware
   palette entry.
4. Benchmark the assembly row loops and memory use in DOSBox-X and on a real
   8088 before enabling the runtime option. Do not promise color fidelity until
   each display profile has a tested backend.

This design follows the period-appropriate table-driven approach illustrated
by the Windows 3 EGA/CGA [`DITHER.ASM`](https://github.com/astrobleem/windows-3-ega-8088/blob/main/src/DITHER.ASM).
The 8088 MPH composite-color technique is a separate hardware-specific path:
it relies on CGA composite artifact colors and precise signal behavior, so it
should not be conflated with the standard RGB palette profiles.

## References

- [Windows 3 EGA/8088 DITHER.ASM](https://github.com/astrobleem/windows-3-ega-8088/blob/main/src/DITHER.ASM)
- [Reenigne: 1K colours on CGA](https://www.reenigne.org/blog/1k-colours-on-cga-how-its-done/)
- [Ordered dithering](https://en.wikipedia.org/wiki/Ordered_dithering)
