#include <conio.h>
#include <ctype.h>
#include <dos.h>
#include <i86.h>
#include <stddef.h>
#if defined(FREERTOSX86_SMOKE)
#include <stdio.h>
#endif

#include "lvgl.h"
#include "gui.h"
#include "hgc_asm.h"

static unsigned short usMouseAvailable;
static unsigned short usFormDrawn;
static unsigned short usFirstFlushComplete;
static unsigned short usInitializationStage;
static unsigned short usVisiblePage;
static unsigned short usDrawPage;
static unsigned short usDamageValid;
static unsigned short usDamageX1;
static unsigned short usDamageY1;
static unsigned short usDamageX2;
static unsigned short usDamageY2;
static lv_disp_draw_buf_t xDisplayDrawBuffer;
static lv_color_t xDisplayPixels[720U * 8U];
static lv_disp_drv_t xDisplayDriver;
static lv_indev_drv_t xMouseDriver;

#if defined(FREERTOSX86_SMOKE)
static void prvWriteInitializationStage(unsigned short stage)
{
    FILE * stageFile = fopen("GUISTAGE.TXT", "w");
    if (stageFile != NULL)
    {
        fprintf(stageFile, "%u\n", stage);
        fclose(stageFile);
    }
}
#endif

static const unsigned char ucFont5x7[36][7] =
{
    { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 }, /* A */
    { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E }, /* B */
    { 0x0F, 0x10, 0x10, 0x10, 0x10, 0x10, 0x0F }, /* C */
    { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E }, /* D */
    { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F }, /* E */
    { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 }, /* F */
    { 0x0F, 0x10, 0x10, 0x17, 0x11, 0x11, 0x0F }, /* G */
    { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 }, /* H */
    { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E }, /* I */
    { 0x07, 0x02, 0x02, 0x02, 0x12, 0x12, 0x0C }, /* J */
    { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 }, /* K */
    { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F }, /* L */
    { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 }, /* M */
    { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 }, /* N */
    { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E }, /* O */
    { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 }, /* P */
    { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D }, /* Q */
    { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 }, /* R */
    { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E }, /* S */
    { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 }, /* T */
    { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E }, /* U */
    { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 }, /* V */
    { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A }, /* W */
    { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 }, /* X */
    { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 }, /* Y */
    { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F }, /* Z */
    { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, /* 0 */
    { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E }, /* 1 */
    { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, /* 2 */
    { 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E }, /* 3 */
    { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, /* 4 */
    { 0x1F, 0x10, 0x10, 0x1E, 0x01, 0x01, 0x1E }, /* 5 */
    { 0x0E, 0x10, 0x10, 0x1E, 0x11, 0x11, 0x0E }, /* 6 */
    { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 }, /* 7 */
    { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, /* 8 */
    { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x01, 0x0E }  /* 9 */
};

static void prvRecordDamage(
    unsigned short x1,
    unsigned short y1,
    unsigned short x2,
    unsigned short y2)
{
    if (usDamageValid == 0U)
    {
        usDamageX1 = x1;
        usDamageY1 = y1;
        usDamageX2 = x2;
        usDamageY2 = y2;
        usDamageValid = 1U;
        return;
    }

    if (x1 < usDamageX1)
    {
        usDamageX1 = x1;
    }
    if (y1 < usDamageY1)
    {
        usDamageY1 = y1;
    }
    if (x2 > usDamageX2)
    {
        usDamageX2 = x2;
    }
    if (y2 > usDamageY2)
    {
        usDamageY2 = y2;
    }
}

static void prvCompleteDisplayRefresh(void)
{
    unsigned short previousVisiblePage;

    if (usDamageValid == 0U)
    {
        return;
    }

    previousVisiblePage = usVisiblePage;
    usVisiblePage = usDrawPage;
    usDrawPage = previousVisiblePage;
    hgc_asm_wait_vsync();
    hgc_asm_set_page(usVisiblePage);
    hgc_asm_copy_rect(
        usVisiblePage,
        usDrawPage,
        usDamageX1,
        usDamageY1,
        usDamageX2,
        usDamageY2);
    usDamageValid = 0U;
    usFirstFlushComplete = 1U;
}

static void prvFlushDisplay(
    lv_disp_drv_t * driver,
    const lv_area_t * area,
    lv_color_t * colors)
{
    int clippedX1 = (area->x1 < 0) ? 0 : area->x1;
    int clippedY1 = (area->y1 < 0) ? 0 : area->y1;
    int clippedX2 = (area->x2 > 719) ? 719 : area->x2;
    int clippedY2 = (area->y2 > 347) ? 347 : area->y2;
    long areaWidthValue = (long)area->x2 - (long)area->x1 + 1L;
    unsigned short areaWidth;
    int y;

    if ((areaWidthValue <= 0L) || (areaWidthValue > 720L) ||
        (clippedX1 > clippedX2) || (clippedY1 > clippedY2))
    {
        if (lv_disp_flush_is_last(driver))
        {
            prvCompleteDisplayRefresh();
        }
        lv_disp_flush_ready(driver);
        return;
    }

    areaWidth = (unsigned short)areaWidthValue;
    prvRecordDamage(
        (unsigned short)clippedX1,
        (unsigned short)clippedY1,
        (unsigned short)clippedX2,
        (unsigned short)clippedY2);
    for (y = clippedY1; y <= clippedY2; y++)
    {
        hgc_asm_flush_row(
            FP_SEG(colors),
            FP_OFF(colors),
            areaWidth,
            (unsigned short)(y - area->y1),
            (unsigned short)(clippedX1 - area->x1),
            (unsigned short)clippedX1,
            (unsigned short)clippedX2,
            (unsigned short)y,
            usDrawPage);
    }

    if (lv_disp_flush_is_last(driver))
    {
        prvCompleteDisplayRefresh();
    }
    lv_disp_flush_ready(driver);
}

static void prvReadMouse(lv_indev_drv_t * driver, lv_indev_data_t * data)
{
    GuiMouseState state;

    (void)driver;
    data->continue_reading = false;
    data->state = LV_INDEV_STATE_RELEASED;
    if (gui_mouse_poll(&state) == GUI_OK)
    {
        data->point.x = (lv_coord_t)state.x;
        data->point.y = (lv_coord_t)state.y;
        if ((state.buttons & GUI_MOUSE_LEFT) != 0U)
        {
            data->state = LV_INDEV_STATE_PRESSED;
        }
    }
}

static unsigned short prvInitializeMouse(void)
{
    union REGS registers;
    struct SREGS segments;
    unsigned char __far * interruptHandler;

    registers.h.ah = 0x35;
    registers.h.al = 0x33;
    int86x(0x21, &registers, &registers, &segments);
    if ((segments.es == 0U) && (registers.x.bx == 0U))
    {
        return 0U;
    }

    interruptHandler = (unsigned char __far *) MK_FP(segments.es, registers.x.bx);
    if (*interruptHandler == 0xCFU)
    {
        return 0U;
    }

    registers.x.ax = 0U;
    int86(0x33, &registers, &registers);
    if (registers.x.ax != 0xFFFFU)
    {
        return 0U;
    }

    registers.x.ax = 7U;
    registers.x.cx = 0U;
    registers.x.dx = 719U;
    int86(0x33, &registers, &registers);
    registers.x.ax = 8U;
    registers.x.cx = 0U;
    registers.x.dx = 347U;
    int86(0x33, &registers, &registers);
    return 1U;
}

unsigned short gui_initialize(void)
{
    lv_disp_t * display;

    usInitializationStage = 1U;
#if defined(FREERTOSX86_SMOKE)
    prvWriteInitializationStage(usInitializationStage);
#endif
    hgc_asm_set_mode();
    usVisiblePage = 0U;
    usDrawPage = 1U;
    usDamageValid = 0U;
    hgc_asm_set_page(usVisiblePage);
    hgc_asm_clear(0U);
    hgc_asm_clear(1U);
    usInitializationStage = 2U;
#if defined(FREERTOSX86_SMOKE)
    prvWriteInitializationStage(usInitializationStage);
#endif
    usMouseAvailable = prvInitializeMouse();
    usFormDrawn = 0U;
    usFirstFlushComplete = 0U;

    lv_init();
    usInitializationStage = 3U;
#if defined(FREERTOSX86_SMOKE)
    prvWriteInitializationStage(usInitializationStage);
#endif
    lv_disp_draw_buf_init(
        &xDisplayDrawBuffer,
        xDisplayPixels,
        NULL,
        (uint32_t)(720U * 8U));
    lv_disp_drv_init(&xDisplayDriver);
    xDisplayDriver.hor_res = 720;
    xDisplayDriver.ver_res = 348;
    xDisplayDriver.draw_buf = &xDisplayDrawBuffer;
    xDisplayDriver.flush_cb = prvFlushDisplay;
    display = lv_disp_drv_register(&xDisplayDriver);
    if (display == NULL)
    {
        usInitializationStage = 2U;
        return GUI_ERROR;
    }

    usInitializationStage = 3U;
    lv_indev_drv_init(&xMouseDriver);
    xMouseDriver.type = LV_INDEV_TYPE_POINTER;
    xMouseDriver.read_cb = prvReadMouse;
    if (lv_indev_drv_register(&xMouseDriver) == NULL)
    {
        usInitializationStage = 4U;
        return GUI_ERROR;
    }
    usInitializationStage = 5U;
#if defined(FREERTOSX86_SMOKE)
    prvWriteInitializationStage(usInitializationStage);
#endif
    return GUI_OK;
}

unsigned short gui_initialization_stage(void)
{
    return usInitializationStage;
}

void gui_mark_form_drawn(void)
{
    usFormDrawn = 1U;
}

unsigned short gui_form_was_drawn(void)
{
    return (unsigned short)(usFormDrawn != 0U && usFirstFlushComplete != 0U);
}

void gui_shutdown(void)
{
    union REGS registers;

    hgc_asm_shutdown();
    registers.h.ah = 0U;
    registers.h.al = 7U;
    int86(0x10, &registers, &registers);
}

void gui_clear(void)
{
    usVisiblePage = 0U;
    usDrawPage = 1U;
    usDamageValid = 0U;
    hgc_asm_set_page(usVisiblePage);
    hgc_asm_clear(0U);
    hgc_asm_clear(1U);
    lv_obj_clean(lv_scr_act());
}

#if defined(FREERTOSX86_SMOKE)
unsigned short gui_hgc_pages_match(void)
{
    const unsigned char __far * pageZero =
        (const unsigned char __far *)MK_FP(0xB000U, 0U);
    const unsigned char __far * pageOne =
        (const unsigned char __far *)MK_FP(0xB800U, 0U);
    unsigned short offset;

    for (offset = 0U; offset < 0x8000U; offset++)
    {
        if (pageZero[offset] != pageOne[offset])
        {
            return 0U;
        }
    }
    return 1U;
}
#endif

void gui_process(void)
{
    lv_tick_inc((uint32_t)(1000U / 18U));
    lv_timer_handler();
}

void gui_pixel(unsigned short x, unsigned short y, unsigned char color)
{
    hgc_asm_pixel(x, y, (unsigned short)(color != 0U), 0U);
}

void gui_line(unsigned short x1, unsigned short y1,
              unsigned short x2, unsigned short y2)
{
    hgc_asm_line(x1, y1, x2, y2);
}

void gui_rectangle(unsigned short x, unsigned short y,
                   unsigned short width, unsigned short height,
                   unsigned char filled)
{
    hgc_asm_rectangle(x, y, width, height, (unsigned short)(filled != 0U));
}

void gui_circle(unsigned short centerX, unsigned short centerY,
                unsigned short radius, unsigned char filled)
{
    hgc_asm_circle(
        centerX,
        centerY,
        radius,
        (unsigned short)(filled != 0U));
}

static unsigned short prvGlyphIndex(unsigned char character)
{
    character = (unsigned char)toupper(character);
    if ((character >= 'A') && (character <= 'Z'))
    {
        return (unsigned short)(character - 'A');
    }
    if ((character >= '0') && (character <= '9'))
    {
        return (unsigned short)(26U + character - '0');
    }
    return 36U;
}

static unsigned char prvGlyphRow(unsigned char character, unsigned short row)
{
    if (character == ' ')
    {
        return 0U;
    }
    if (character == '.')
    {
        return (row == 6U) ? 0x04U : 0U;
    }
    if (character == ':')
    {
        return ((row == 2U) || (row == 5U)) ? 0x04U : 0U;
    }
    if (character == '-')
    {
        return (row == 3U) ? 0x0EU : 0U;
    }
    if (character == '_')
    {
        return (row == 6U) ? 0x1FU : 0U;
    }
    if (character == '?')
    {
        static const unsigned char question[7] =
            { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x00, 0x04 };
        return question[row];
    }
    {
        unsigned short index = prvGlyphIndex(character);
        return (index < 36U) ? ucFont5x7[index][row] : 0U;
    }
}

void gui_text(unsigned short x, unsigned short y, const char * text)
{
    unsigned short characterIndex = 0U;

    if (text == NULL)
    {
        return;
    }

    while ((text[characterIndex] != '\0') && (characterIndex < 80U))
    {
        unsigned short row;
        unsigned char character = (unsigned char)text[characterIndex];
        for (row = 0U; row < 7U; row++)
        {
            unsigned char rowBits = prvGlyphRow(character, row);
            unsigned short column;
            for (column = 0U; column < 5U; column++)
            {
                unsigned short pixelX =
                    (unsigned short)(x + characterIndex * 6U + column);
                unsigned short pixelY = (unsigned short)(y + row);
                if (((rowBits & (unsigned char)(0x10U >> column)) != 0U) &&
                    (pixelX < 720U) &&
                    (pixelY < 348U))
                {
                    gui_pixel(pixelX, pixelY, 1U);
                }
            }
        }
        characterIndex++;
    }
}

void gui_button(unsigned short x, unsigned short y,
                unsigned short width, unsigned short height,
                const char * text)
{
    hgc_asm_rectangle(x, y, width, height, 0U);
    gui_text(
        (unsigned short)(x + 4U),
        (unsigned short)(y + (height > 7U ? (height - 7U) / 2U : 0U)),
        text);
}

void gui_checkbox(unsigned short x, unsigned short y,
                  const char * text, unsigned char checked)
{
    hgc_asm_rectangle(x, (unsigned short)(y + 1U), 10U, 10U, 0U);
    if (checked != 0U)
    {
        hgc_asm_line(
            (unsigned short)(x + 2U),
            (unsigned short)(y + 6U),
            (unsigned short)(x + 4U),
            (unsigned short)(y + 8U));
        hgc_asm_line(
            (unsigned short)(x + 4U),
            (unsigned short)(y + 8U),
            (unsigned short)(x + 8U),
            (unsigned short)(y + 3U));
    }
    gui_text((unsigned short)(x + 15U), y + 2U, text);
}

unsigned short gui_mouse_poll(GuiMouseState * state)
{
    union REGS registers;

    if ((usMouseAvailable == 0U) || (state == NULL))
    {
        return GUI_MOUSE_UNAVAILABLE;
    }

    registers.x.ax = 3U;
    int86(0x33, &registers, &registers);
    state->x = (registers.x.cx > 719U) ? 719U : registers.x.cx;
    state->y = (registers.x.dx > 347U) ? 347U : registers.x.dx;
    state->buttons = (unsigned char)(registers.x.bx & 0x03U);
    return GUI_OK;
}

unsigned short gui_should_exit(void)
{
    if (kbhit() != 0)
    {
        int key = getch();
        return (key == 27) ? 1U : 0U;
    }
    return 0U;
}
