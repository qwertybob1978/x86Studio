#include "FreeRTOS.h"
#include "task.h"
#include "dos_agent.h"
#include "gui.h"
#include "core/lv_disp.h"
#include "core/lv_obj.h"
#include "extra/widgets/colorwheel/lv_colorwheel.h"
#include "extra/widgets/keyboard/lv_keyboard.h"
#include "extra/widgets/spinbox/lv_spinbox.h"
#include "widgets/lv_arc.h"
#include "widgets/lv_btnmatrix.h"
#include "widgets/lv_checkbox.h"
#include "widgets/lv_label.h"
#include "widgets/lv_textarea.h"
#include "user_events.h"

static const char * xButtonMap2[] =
{
    "One",
    "Two",
    "Three",
    ""
};
static void prvDrawForm(void)
{
    lv_obj_t * obj;
    lv_obj_t * label;
    lv_obj_t * child;

    lv_obj_set_style_bg_color(lv_scr_act(), lv_color_black(), LV_PART_MAIN);
    lv_obj_set_style_bg_opa(lv_scr_act(), LV_OPA_COVER, LV_PART_MAIN);
    obj = lv_checkbox_create(lv_scr_act());
    lv_checkbox_set_text(obj, "Check Box");
    lv_obj_set_pos(obj, 16, 16);
    lv_obj_set_size(obj, 110, 18);
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_border_width(obj, 1, LV_PART_INDICATOR);
    lv_obj_set_style_border_color(obj, lv_color_hex(0xFFFFFF), LV_PART_INDICATOR);
    lv_obj_set_style_bg_opa(obj, LV_OPA_TRANSP, LV_PART_INDICATOR);
    lv_obj_set_style_bg_color(obj, lv_color_hex(0xFFFFFF), LV_PART_INDICATOR | LV_STATE_CHECKED);
    lv_obj_set_style_bg_opa(obj, LV_OPA_COVER, LV_PART_INDICATOR | LV_STATE_CHECKED);
    lv_obj_set_style_line_color(obj, lv_color_white(), LV_PART_INDICATOR);
    obj = lv_arc_create(lv_scr_act());
    lv_arc_set_angles(obj, 0, 180);
    lv_obj_set_pos(obj, 312, 112);
    lv_obj_set_size(obj, 64, 48);
    lv_obj_set_style_arc_color(obj, lv_color_hex(0xFFFFFF), LV_PART_INDICATOR);
    lv_obj_set_style_arc_width(obj, 2, LV_PART_INDICATOR);
    obj = lv_btnmatrix_create(lv_scr_act());
    lv_btnmatrix_set_map(obj, xButtonMap2);
    lv_obj_set_pos(obj, 40, 112);
    lv_obj_set_size(obj, 160, 60);
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_bg_color(obj, lv_color_hex(0x000000), LV_PART_MAIN);
    obj = lv_colorwheel_create(lv_scr_act(), true);
    lv_obj_set_pos(obj, 480, 104);
    lv_obj_set_size(obj, 120, 120);
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_bg_color(obj, lv_color_hex(0x000000), LV_PART_MAIN);
    obj = lv_keyboard_create(lv_scr_act());
    lv_obj_set_pos(obj, 16, 184);
    lv_obj_set_size(obj, 400, 100);
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_bg_color(obj, lv_color_hex(0x000000), LV_PART_MAIN);
    obj = lv_spinbox_create(lv_scr_act());
    lv_spinbox_set_value(obj, 0);
    lv_obj_set_pos(obj, 216, 24);
    lv_obj_set_size(obj, 272, 40);
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_bg_color(obj, lv_color_hex(0x000000), LV_PART_MAIN);
}

void vFreeRTOSx86GuiTask(void * pvParameters)
{
    (void)pvParameters;

    if (gui_initialize() != GUI_OK)
    {
        for (;;)
        {
            vTaskDelay((TickType_t)1U);
        }
    }

    gui_clear();
    prvDrawForm();
    gui_mark_form_drawn();
    dos_agent_log(1U, "LVGL GUI initialized");
    for (;;)
    {
        gui_process();
        vTaskDelay((TickType_t)1U);
    }
}
