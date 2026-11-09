#ifndef FREERTOSX86_GUI_H
#define FREERTOSX86_GUI_H

#define GUI_OK                 0U
#define GUI_MOUSE_UNAVAILABLE  1U
#define GUI_ERROR              2U
#define GUI_MOUSE_LEFT         1U
#define GUI_MOUSE_RIGHT        2U

typedef struct GuiMouseState
{
    unsigned short x;
    unsigned short y;
    unsigned char buttons;
} GuiMouseState;

unsigned short gui_initialize(void);
unsigned short gui_initialization_stage(void);
void gui_process(void);
void gui_mark_form_drawn(void);
unsigned short gui_form_was_drawn(void);
#if defined(FREERTOSX86_SMOKE)
unsigned short gui_hgc_pages_match(void);
#endif
void gui_shutdown(void);
void gui_clear(void);
void gui_pixel(unsigned short x, unsigned short y, unsigned char color);
void gui_line(unsigned short x1, unsigned short y1,
              unsigned short x2, unsigned short y2);
void gui_rectangle(unsigned short x, unsigned short y,
                   unsigned short width, unsigned short height,
                   unsigned char filled);
void gui_circle(unsigned short centerX, unsigned short centerY,
                unsigned short radius, unsigned char filled);
void gui_text(unsigned short x, unsigned short y, const char * text);
void gui_button(unsigned short x, unsigned short y,
                unsigned short width, unsigned short height,
                const char * text);
void gui_checkbox(unsigned short x, unsigned short y,
                  const char * text, unsigned char checked);
unsigned short gui_mouse_poll(GuiMouseState * state);
unsigned short gui_should_exit(void);

#endif
