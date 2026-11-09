#include <conio.h>
#include <dos.h>
#include <i86.h>

#if defined(FREERTOSX86_USER_APP)
#include "FreeRTOS.h"
#include "task.h"
#endif

#include "dos_serial.h"

#define DOS_TICKS_PER_DAY 0x001800B0UL
#define DOS_TICKS_PER_SECOND 18UL
#define UART_LINE_STATUS 5
#define UART_DATA_READY 0x01
#define UART_TRANSMIT_EMPTY 0x20

#if defined(FREERTOSX86_USER_APP)
#define UART_RX_BUFFER_SIZE 256U

typedef void (__interrupt __far * DosUartInterrupt)(void);

static unsigned short usInterruptUartBase;
static unsigned char ucInterruptUartIrq;
static unsigned char ucOldUartInterruptMask;
static unsigned char ucOldUartInterruptEnable;
static unsigned char ucOldUartLineControl;
static unsigned char ucOldUartModemControl;
static unsigned char ucOldUartDivisorLow;
static unsigned char ucOldUartDivisorHigh;
static unsigned char ucInterruptUartOpen;
static DosUartInterrupt pxOldUartInterrupt;
static volatile unsigned char ucReceiveBuffer[UART_RX_BUFFER_SIZE];
static volatile unsigned char ucReceiveHead;
static volatile unsigned char ucReceiveTail;

static unsigned char prvIrqVector(void)
{
    return (unsigned char)(0x08U + ucInterruptUartIrq);
}

static void __interrupt __far prvUartInterrupt(void)
{
    while ((inp(usInterruptUartBase + UART_LINE_STATUS) & UART_DATA_READY) != 0)
    {
        unsigned char value;
        unsigned char nextHead;

        value = (unsigned char)inp(usInterruptUartBase);
        nextHead = (unsigned char)(ucReceiveHead + 1U);
        if (nextHead != ucReceiveTail)
        {
            ucReceiveBuffer[ucReceiveHead] = value;
            ucReceiveHead = nextHead;
        }
    }

    outp(0x20U, 0x20U);
}

static int prvInitializeInterruptUart(DosUart *uart, unsigned int divisor)
{
    unsigned char interruptMask;
    unsigned char masterMask;
    unsigned char lineControl;

    if (ucInterruptUartOpen != 0)
        return 0;

    usInterruptUartBase = (unsigned short)uart->base;
    ucInterruptUartIrq = (uart->port_number == 1 || uart->port_number == 3)
        ? 4U : 3U;
    pxOldUartInterrupt = _dos_getvect(prvIrqVector());
    ucOldUartInterruptMask = (unsigned char)inp(0x21U);
    interruptMask = (unsigned char)(1U << ucInterruptUartIrq);

    portENTER_CRITICAL();
    ucOldUartInterruptEnable = (unsigned char)inp(usInterruptUartBase + 1U);
    outp(usInterruptUartBase + 1U, 0U);
    lineControl = (unsigned char)inp(usInterruptUartBase + 3U);
    ucOldUartLineControl = lineControl;
    outp(usInterruptUartBase + 3U, (unsigned char)(lineControl | 0x80U));
    ucOldUartDivisorLow = (unsigned char)inp(usInterruptUartBase);
    ucOldUartDivisorHigh = (unsigned char)inp(usInterruptUartBase + 1U);
    ucOldUartModemControl = (unsigned char)inp(usInterruptUartBase + 4U);

    outp(usInterruptUartBase + 3U, 0x80U);
    outp(usInterruptUartBase, divisor & 0xFFU);
    outp(usInterruptUartBase + 1U, divisor >> 8);
    outp(usInterruptUartBase + 3U, 0x03U);
    outp(usInterruptUartBase + 2U, 0x07U);
    outp(usInterruptUartBase + 4U, 0x0BU);
    ucReceiveHead = 0U;
    ucReceiveTail = 0U;
    _dos_setvect(prvIrqVector(), prvUartInterrupt);
    masterMask = (unsigned char)(ucOldUartInterruptMask & (unsigned char)~interruptMask);
    outp(0x21U, masterMask);
    ucInterruptUartOpen = 1U;
    outp(usInterruptUartBase + 1U, 0x01U);
    portEXIT_CRITICAL();

    return 1;
}

static void prvRestoreInterruptUart(void)
{
    unsigned char currentMask;
    unsigned char interruptMask;

    if (ucInterruptUartOpen == 0)
        return;

    portENTER_CRITICAL();
    outp(usInterruptUartBase + 1U, 0U);
    _dos_setvect(prvIrqVector(), pxOldUartInterrupt);
    interruptMask = (unsigned char)(1U << ucInterruptUartIrq);
    currentMask = (unsigned char)inp(0x21U);
    currentMask = (unsigned char)(
        (currentMask & (unsigned char)~interruptMask) |
        (ucOldUartInterruptMask & interruptMask));
    outp(0x21U, currentMask);

    outp(usInterruptUartBase + 3U, 0x80U);
    outp(usInterruptUartBase, ucOldUartDivisorLow);
    outp(usInterruptUartBase + 1U, ucOldUartDivisorHigh);
    outp(usInterruptUartBase + 3U, ucOldUartLineControl);
    outp(usInterruptUartBase + 4U, ucOldUartModemControl);
    outp(usInterruptUartBase + 1U, ucOldUartInterruptEnable);
    ucInterruptUartOpen = 0U;
    portEXIT_CRITICAL();
}
#endif

rl_u32 dos_clock_ticks(void)
{
    unsigned long far *ticks;
    ticks = (unsigned long far *)MK_FP(0x40, 0x6C);
    return *ticks;
}

void dos_idle(void)
{
#if defined(FREERTOSX86_USER_APP)
    vTaskDelay((TickType_t)1U);
#else
    /* Poll the UART continuously so its receive FIFO cannot overrun. */
#endif
}

int dos_uart_port_available(unsigned char port_number)
{
    unsigned short far *bios_ports;

    if (port_number < 1 || port_number > 4)
        return 0;

    bios_ports = (unsigned short far *)MK_FP(0x40, 0);
    return bios_ports[port_number - 1] != 0;
}

int dos_uart_open(DosUart *uart, unsigned char port_number, rl_u32 baud_rate)
{
    unsigned short far *bios_ports;
    unsigned int divisor;

    if (uart == 0 || port_number < 1 || port_number > 4 ||
        baud_rate < 300 || baud_rate > 115200)
    {
        return 0;
    }

    bios_ports = (unsigned short far *)MK_FP(0x40, 0);
    uart->base = bios_ports[port_number - 1];
    if (uart->base == 0)
        return 0;
    divisor = (unsigned int)(115200UL / baud_rate);
    if (divisor == 0)
        return 0;

    uart->port_number = port_number;
    uart->baud_rate = baud_rate;
#if defined(FREERTOSX86_USER_APP)
    if (!prvInitializeInterruptUart(uart, divisor))
    {
        uart->base = 0;
        return 0;
    }
    return 1;
#endif
    outp(uart->base + 1, 0);
    outp(uart->base + 3, 0x80);
    outp(uart->base, divisor & 0xFF);
    outp(uart->base + 1, divisor >> 8);
    outp(uart->base + 3, 0x03);
    outp(uart->base + 2, 0x07);
    outp(uart->base + 4, 0x03);
    return 1;
}

void dos_uart_close(DosUart *uart)
{
    if (uart == 0)
        return;
#if defined(FREERTOSX86_USER_APP)
    if (ucInterruptUartOpen != 0 &&
        uart->base == usInterruptUartBase)
    {
        prvRestoreInterruptUart();
    }
#endif
    uart->base = 0;
}

int dos_uart_try_read(DosUart *uart, rl_u8 *value)
{
    if (uart == 0 || value == 0 || uart->base == 0)
        return 0;
#if defined(FREERTOSX86_USER_APP)
    if (ucInterruptUartOpen != 0 &&
        uart->base == usInterruptUartBase)
    {
        unsigned char tail = ucReceiveTail;
        if (tail == ucReceiveHead)
            return 0;
        *value = ucReceiveBuffer[tail];
        ucReceiveTail = (unsigned char)(tail + 1U);
        return 1;
    }
#endif
    if ((inp(uart->base + UART_LINE_STATUS) & UART_DATA_READY) == 0)
        return 0;
    *value = (rl_u8)inp(uart->base);
    return 1;
}

int dos_uart_write(DosUart *uart, rl_u8 value, rl_u16 timeout_seconds)
{
#if defined(FREERTOSX86_USER_APP)
    TickType_t start_tick;
    TickType_t timeout_ticks;
    TickType_t last_yield_tick;
    TickType_t now_tick;
#else
    rl_u32 start;
    rl_u32 timeout_ticks;
    rl_u32 now;
#endif

    if (uart == 0 || uart->base == 0)
        return 0;
#if defined(FREERTOSX86_USER_APP)
    start_tick = xTaskGetTickCount();
    timeout_ticks = (TickType_t)timeout_seconds *
        (TickType_t)configTICK_RATE_HZ;
    last_yield_tick = start_tick;
    do
    {
        if ((inp(uart->base + UART_LINE_STATUS) & UART_TRANSMIT_EMPTY) != 0)
        {
            outp(uart->base, value);
            return 1;
        }
        now_tick = xTaskGetTickCount();
        if ((TickType_t)(now_tick - start_tick) >= timeout_ticks)
            return 0;
        if (now_tick != last_yield_tick)
        {
            last_yield_tick = now_tick;
            dos_idle();
        }
    } while (1);
#else
    start = dos_clock_ticks();
    timeout_ticks = (rl_u32)timeout_seconds * DOS_TICKS_PER_SECOND;
    do
    {
        if ((inp(uart->base + UART_LINE_STATUS) & UART_TRANSMIT_EMPTY) != 0)
        {
            outp(uart->base, value);
            return 1;
        }
        now = dos_clock_ticks();
        if (now < start)
        {
            if (DOS_TICKS_PER_DAY - start + now >= timeout_ticks)
                return 0;
        }
        else if (now - start >= timeout_ticks)
        {
            return 0;
        }
        dos_idle();
    } while (1);
#endif
}
