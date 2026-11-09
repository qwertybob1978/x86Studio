#include "FreeRTOS.h"
#include "task.h"
#include "dos_agent.h"
#include "gui.h"
#include "core/lv_disp.h"
#include "core/lv_obj.h"
#include "extra/widgets/imgbtn/lv_imgbtn.h"
#include "widgets/lv_btn.h"
#include "widgets/lv_dropdown.h"
#include "widgets/lv_label.h"
#include "user_events.h"

static void prvAddToolbarButton(const char * caption, lv_coord_t x)
{
    lv_obj_t * obj;
    lv_obj_t * label;

    obj = lv_imgbtn_create(lv_scr_act());
    lv_obj_set_pos(obj, x + 4, 2);
    lv_obj_set_size(obj, 24, 24);
    lv_obj_set_style_border_width(obj, 1, LV_PART_MAIN);
    lv_obj_set_style_border_color(obj, lv_color_white(), LV_PART_MAIN);
    lv_obj_set_style_bg_color(obj, lv_color_black(), LV_PART_MAIN);
    lv_obj_set_style_bg_opa(obj, LV_OPA_COVER, LV_PART_MAIN);
    label = lv_label_create(lv_scr_act());
    lv_label_set_text(label, caption);
    lv_obj_set_pos(label, x, 26);
    lv_obj_set_size(label, 32, 16);
    lv_obj_set_style_text_color(label, lv_color_white(), LV_PART_MAIN);
}

static void prvDrawForm(void)
{
    lv_obj_t * obj;
    lv_obj_t * label;

    lv_obj_set_style_bg_color(lv_scr_act(), lv_color_black(), LV_PART_MAIN);
    lv_obj_set_style_bg_opa(lv_scr_act(), LV_OPA_COVER, LV_PART_MAIN);
    obj = lv_dropdown_create(lv_scr_act());
    lv_dropdown_set_options(obj, "COM2\nCOM4");
    lv_obj_set_pos(obj, 10, 4);
    lv_obj_set_size(obj, 120, 24);
    obj = lv_dropdown_create(lv_scr_act());
    lv_dropdown_set_options(obj, "300\n600\n1200\n1800\n2400\n3600\n4800\n7200\n9600\n14400\n19200\n28800\n38400\n57600\n115200");
    lv_obj_set_pos(obj, 136, 4);
    lv_obj_set_size(obj, 120, 24);
    prvAddToolbarButton("LINK", 264);
    prvAddToolbarButton("CTL", 296);
    prvAddToolbarButton("SEND", 328);
    prvAddToolbarButton("GET", 360);
    prvAddToolbarButton("OPEN", 392);
    prvAddToolbarButton("UP", 424);
    prvAddToolbarButton("MARK", 456);
    prvAddToolbarButton("REF", 488);
    obj = lv_label_create(lv_scr_act());
    lv_label_set_text(obj, "COM2 9600 WAITING");
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_pos(obj, 548, 8);
    lv_obj_set_size(obj, 158, 20);
    obj = lv_label_create(lv_scr_act());
    lv_label_set_text(obj, "LOCAL C:\\DATA");
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_pos(obj, 14, 44);
    lv_obj_set_size(obj, 338, 16);
    obj = lv_label_create(lv_scr_act());
    lv_label_set_text(obj, "REMOTE (waiting for Windows)");
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_pos(obj, 368, 44);
    lv_obj_set_size(obj, 338, 16);
    obj = lv_obj_create(lv_scr_act());
    lv_obj_set_pos(obj, 14, 64);
    lv_obj_set_size(obj, 338, 248);
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_bg_color(obj, lv_color_hex(0x000000), LV_PART_MAIN);
    lv_obj_set_style_border_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_border_width(obj, 1, LV_PART_MAIN);
    obj = lv_obj_create(lv_scr_act());
    lv_obj_set_pos(obj, 368, 64);
    lv_obj_set_size(obj, 338, 248);
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_bg_color(obj, lv_color_hex(0x000000), LV_PART_MAIN);
    lv_obj_set_style_border_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_style_border_width(obj, 1, LV_PART_MAIN);
    obj = lv_label_create(lv_scr_act());
    lv_label_set_text(obj, "Waiting for REMOTE connection.");
    lv_obj_set_style_text_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN);
    lv_obj_set_pos(obj, 16, 321);
    lv_obj_set_size(obj, 688, 20);
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
    user_app_init();
    gui_mark_form_drawn();
    dos_agent_log(1U, "LVGL GUI initialized");
    for (;;)
    {
        gui_process();
        user_app_poll();
        vTaskDelay((TickType_t)1U);
    }
}
