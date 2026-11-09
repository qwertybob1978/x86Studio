/*
 * FreeRTOS Kernel V11.3.1
 * Copyright (C) 2021 Amazon.com, Inc. or its affiliates. All Rights Reserved.
 *
 * SPDX-License-Identifier: MIT
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of
 * this software and associated documentation files (the "Software"), to deal in
 * the Software without restriction, including without limitation the rights to
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
 * FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
 * COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
 * IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 * 8086-compatible adaptation of FreeRTOS portable/oWatcom/16BitDOS/common/portasm.h.
 * The original POPA opcode is replaced with equivalent 8086 POP/ADD instructions.
 */

typedef void TCB_t;
typedef void ( __interrupt __far * pxISR )( void );

extern volatile TCB_t * volatile pxCurrentTCB;
extern void vTaskSwitchContext( void );

void portSWITCH_CONTEXT( void );
void portFIRST_CONTEXT( void );

#ifdef DEBUG_BUILD

    #pragma aux portSWITCH_CONTEXT =    "mov    ax, seg pxCurrentTCB"                                                       \
                                        "mov    ds, ax"                                                                     \
                                        "les    bx, pxCurrentTCB"                                                           \
                                        "mov    es:0x2[ bx ], ss"                                                           \
                                        "mov    es:[ bx ], sp"                                                              \
                                        "call   vTaskSwitchContext"                                                         \
                                        "mov    ax, seg pxCurrentTCB"                                                       \
                                        "mov    ds, ax"                                                                     \
                                        "les    bx, dword ptr pxCurrentTCB"                                                 \
                                        "mov    ss, es:[ bx + 2 ]"                                                          \
                                        "mov    sp, es:[ bx ]"                                                              \
                                        "mov    bp, sp"                                                                     \
                                        "add    bp, 0x0002"

    #pragma aux portFIRST_CONTEXT =     "mov    ax, seg pxCurrentTCB"                                                       \
                                        "mov    ds, ax"                                                                     \
                                        "les    bx, dword ptr pxCurrentTCB"                                                 \
                                        "mov    ss, es:[ bx + 2 ]"                                                          \
                                        "mov    sp, es:[ bx ]"                                                              \
                                        "add    sp, 0x0002"                                                                 \
                                        "pop    ax"                                                                         \
                                        "pop    ax"                                                                         \
                                        "pop    es"                                                                         \
                                        "pop    ds"                                                                         \
                                        "pop    di"                                                                         \
                                        "pop    si"                                                                         \
                                        "pop    bp"                                                                         \
                                        "add    sp, 0x0002"                                                                 \
                                        "pop    bx"                                                                         \
                                        "pop    dx"                                                                         \
                                        "pop    cx"                                                                         \
                                        "pop    ax"                                                                         \
                                        "iret"
#else

    #pragma aux portSWITCH_CONTEXT =    "mov    ax, seg pxCurrentTCB"                                                       \
                                        "mov    ds, ax"                                                                     \
                                        "les    bx, pxCurrentTCB"                                                           \
                                        "mov    es:0x2[ bx ], ss"                                                           \
                                        "mov    es:[ bx ], sp"                                                              \
                                        "call   vTaskSwitchContext"                                                         \
                                        "mov    ax, seg pxCurrentTCB"                                                       \
                                        "mov    ds, ax"                                                                     \
                                        "les    bx, dword ptr pxCurrentTCB"                                                 \
                                        "mov    ss, es:[ bx + 2 ]"                                                          \
                                        "mov    sp, es:[ bx ]"

    #pragma aux portFIRST_CONTEXT =     "mov    ax, seg pxCurrentTCB"                                                       \
                                        "mov    ds, ax"                                                                     \
                                        "les    bx, dword ptr pxCurrentTCB"                                                 \
                                        "mov    ss, es:[ bx + 2 ]"                                                          \
                                        "mov    sp, es:[ bx ]"                                                              \
                                        "pop    ax"                                                                         \
                                        "pop    ax"                                                                         \
                                        "pop    es"                                                                         \
                                        "pop    ds"                                                                         \
                                        "pop    di"                                                                         \
                                        "pop    si"                                                                         \
                                        "pop    bp"                                                                         \
                                        "add    sp, 0x0002"                                                                 \
                                        "pop    bx"                                                                         \
                                        "pop    dx"                                                                         \
                                        "pop    cx"                                                                         \
                                        "pop    ax"                                                                         \
                                        "iret"
#endif
