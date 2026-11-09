# LVGL DOS port source

This directory contains the LVGL `release/v8.3` source from
https://github.com/lvgl/lvgl at commit
`fbb73d4b3ffe086a7218bd1a8e51f9e7a260c4b9`. The source is distributed under
the MIT license in `LICENCE.txt`.

The DOS build uses LVGL's one-bit color mode. The Open Watcom compatibility
changes avoid unsupported runtime aggregate initializers and use ordinary
field assignments in the generated style helpers, transformed-area helpers,
gradient renderer, canvas, image widget, and monochrome color functions.
These changes do not alter the public LVGL API.
