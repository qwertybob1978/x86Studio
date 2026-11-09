#ifndef FREERTOS_CONFIG_H
#define FREERTOS_CONFIG_H

#include <conio.h>
#include <i86.h>

#define configCPU_CLOCK_HZ                         ( ( unsigned long ) 4772727UL )
#define configTICK_RATE_HZ                         18U
#define configUSE_PREEMPTION                       1
#define configUSE_TIME_SLICING                     1
#define configUSE_TICKLESS_IDLE                    0
#define configUSE_PORT_OPTIMISED_TASK_SELECTION    0
#define configMAX_PRIORITIES                       3U
#define configMINIMAL_STACK_SIZE                   512U
#define configGUI_TASK_STACK_SIZE                  2048U
#define configMAX_TASK_NAME_LEN                    8U
#define configTICK_TYPE_WIDTH_IN_BITS              TICK_TYPE_WIDTH_16_BITS
#define configIDLE_SHOULD_YIELD                    1

#define configSUPPORT_STATIC_ALLOCATION             1
#define configSUPPORT_DYNAMIC_ALLOCATION            0
#define configKERNEL_PROVIDED_STATIC_MEMORY         0

#define configUSE_TIMERS                            0
#define configUSE_MUTEXES                           0
#define configUSE_RECURSIVE_MUTEXES                 0
#define configUSE_COUNTING_SEMAPHORES               0
#define configUSE_TASK_NOTIFICATIONS                0
#define configUSE_CO_ROUTINES                       0

#define configUSE_IDLE_HOOK                         0
#define configUSE_TICK_HOOK                         0
#define configUSE_MALLOC_FAILED_HOOK                0
#define configCHECK_FOR_STACK_OVERFLOW              0
#define configUSE_TRACE_FACILITY                    0
#define configUSE_STATS_FORMATTING_FUNCTIONS        0
#define configQUEUE_REGISTRY_SIZE                   0U

#define INCLUDE_vTaskDelay                          1
#define INCLUDE_vTaskEndScheduler                   1

#endif
