#ifndef FREERTOSX86_HGC_ASM_H
#define FREERTOSX86_HGC_ASM_H

#if defined(__WATCOMC__)
#define HGC_ASM_CALL __cdecl
#else
#define HGC_ASM_CALL
#endif

void HGC_ASM_CALL hgc_asm_set_mode(void);
void HGC_ASM_CALL hgc_asm_set_page(unsigned short page);
void HGC_ASM_CALL hgc_asm_wait_vsync(void);
void HGC_ASM_CALL hgc_asm_clear(unsigned short page);
void HGC_ASM_CALL hgc_asm_flush_row(
    unsigned short sourceSegment,
    unsigned short sourceOffset,
    unsigned short sourceWidth,
    unsigned short sourceRow,
    unsigned short sourceColumn,
    unsigned short x1,
    unsigned short x2,
    unsigned short y,
    unsigned short page);
void HGC_ASM_CALL hgc_asm_copy_rect(
    unsigned short sourcePage,
    unsigned short destinationPage,
    unsigned short x1,
    unsigned short y1,
    unsigned short x2,
    unsigned short y2);
void HGC_ASM_CALL hgc_asm_pixel(unsigned short x, unsigned short y,
                                unsigned short color, unsigned short page);
void HGC_ASM_CALL hgc_asm_line(unsigned short x1, unsigned short y1,
                               unsigned short x2, unsigned short y2);
void HGC_ASM_CALL hgc_asm_rectangle(unsigned short x, unsigned short y,
                                    unsigned short width, unsigned short height,
                                    unsigned short filled);
void HGC_ASM_CALL hgc_asm_circle(unsigned short centerX, unsigned short centerY,
                                 unsigned short radius, unsigned short filled);
void HGC_ASM_CALL hgc_asm_shutdown(void);

#endif
