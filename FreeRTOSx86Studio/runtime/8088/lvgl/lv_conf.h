#ifndef LV_CONF_H
#define LV_CONF_H

#include "lvgl_widget_config.h"
#ifndef LV_COLOR_DEPTH
#define LV_COLOR_DEPTH 1
#endif
#define LV_MEM_CUSTOM 1
#define LV_USE_LOG 0
#define LV_USE_ASSERT_NULL 0
#define LV_USE_ASSERT_MALLOC 0
#define LV_USE_ASSERT_STYLE 0
#define LV_USE_ASSERT_MEM_INTEGRITY 0
#define LV_USE_ASSERT_OBJ 0

#define LV_FONT_MONTSERRAT_14 1
#define LV_FONT_DEFAULT &lv_font_montserrat_14

#define LV_USE_THEME_DEFAULT 0
#define LV_USE_THEME_BASIC 0
#define LV_USE_THEME_MONO 0
#ifndef LV_USE_FLEX
#define LV_USE_FLEX 0
#endif
#define LV_USE_GRID 0

#endif
