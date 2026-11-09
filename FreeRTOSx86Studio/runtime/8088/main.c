#include <stdio.h>

#include "FreeRTOS.h"
#include "task.h"
#include "dos_agent.h"
#include "gui.h"
#include "gui_task.h"
#include "user_events.h"

static StaticTask_t xGuiTaskTCB;
static StaticTask_t xAgentTaskTCB;
static StaticTask_t xExitTaskTCB;
static StackType_t xGuiTaskStack[configGUI_TASK_STACK_SIZE];
static StackType_t xAgentTaskStack[configMINIMAL_STACK_SIZE];
static StackType_t xExitTaskStack[configMINIMAL_STACK_SIZE];
static StaticTask_t xIdleTaskTCB;
static StackType_t xIdleTaskStack[configMINIMAL_STACK_SIZE];
static unsigned short usSmokeTest;
static unsigned short usSmokeHgcPagesMatch;

static void prvWriteSmokeResult(const char * status)
{
    FILE * resultFile = fopen("RESULT.TXT", "w");
    if (resultFile == NULL)
    {
        return;
    }

    fprintf(resultFile, "status=%s\n", status);
    fprintf(resultFile, "form_drawn=%u\n", gui_form_was_drawn());
    fprintf(resultFile, "gui_init_stage=%u\n", gui_initialization_stage());
#if defined(FREERTOSX86_SMOKE)
    fprintf(resultFile, "hgc_pages_match=%u\n", usSmokeHgcPagesMatch);
#endif
#if defined(FREERTOSX86_SMOKE)
    {
        unsigned long receivedBytes;
        unsigned long validFrames;
        unsigned long invalidCrcFrames;
        unsigned long taskRuns;
        unsigned long interruptBytes;
        unsigned long polledBytes;

        dos_agent_get_smoke_diagnostics(
            &receivedBytes,
            &validFrames,
            &invalidCrcFrames,
            &taskRuns,
            &interruptBytes,
            &polledBytes);
        fprintf(resultFile, "agent_rx_bytes=%lu\n", receivedBytes);
        fprintf(resultFile, "agent_valid_frames=%lu\n", validFrames);
        fprintf(resultFile, "agent_invalid_crc_frames=%lu\n", invalidCrcFrames);
        fprintf(resultFile, "agent_task_runs=%lu\n", taskRuns);
        fprintf(resultFile, "agent_interrupt_bytes=%lu\n", interruptBytes);
        fprintf(resultFile, "agent_polled_bytes=%lu\n", polledBytes);
    }
#endif
    fclose(resultFile);
}

static void prvExitTask(void * parameters)
{
    unsigned short smokeTicks = 0U;

    (void)parameters;
    for (;;)
    {
        if (((usSmokeTest != 0U) && (smokeTicks >= 36U)) ||
            ((usSmokeTest == 0U) && (gui_should_exit() != 0U)))
        {
            vTaskEndScheduler();
        }
        if (usSmokeTest != 0U)
        {
            smokeTicks++;
            if ((smokeTicks % 18U) == 0U)
            {
                prvWriteSmokeResult("RUNNING");
            }
        }
        vTaskDelay((TickType_t)1U);
    }
}

void vApplicationGetIdleTaskMemory(
    StaticTask_t ** taskTCBBuffer,
    StackType_t ** taskStackBuffer,
    configSTACK_DEPTH_TYPE * taskStackSize)
{
    *taskTCBBuffer = &xIdleTaskTCB;
    *taskStackBuffer = xIdleTaskStack;
    *taskStackSize = configMINIMAL_STACK_SIZE;
}

int main(void)
{
    TaskHandle_t guiTask;
    TaskHandle_t agentTask;
    TaskHandle_t exitTask;

#if defined(FREERTOSX86_SMOKE)
    usSmokeTest = 1U;
    prvWriteSmokeResult("RUNNING");
#endif

    puts("FreeRTOSx86Studio DOS GUI — press ESC to return to DOS.");
    if (dos_agent_initialize() != DOS_AGENT_OK)
    {
        puts("Unable to initialize the serial Agent. Check DOSAGENT.CFG.");
        return 1;
    }
    guiTask = xTaskCreateStatic(
        vFreeRTOSx86GuiTask,
        "GuiTask",
        configGUI_TASK_STACK_SIZE,
        NULL,
        (UBaseType_t)2U,
        xGuiTaskStack,
        &xGuiTaskTCB);
    agentTask = xTaskCreateStatic(
        dos_agent_task,
        "Agent",
        configMINIMAL_STACK_SIZE,
        NULL,
        (UBaseType_t)1U,
        xAgentTaskStack,
        &xAgentTaskTCB);
    exitTask = xTaskCreateStatic(
        prvExitTask,
        "ExitTask",
        configMINIMAL_STACK_SIZE,
        NULL,
        (UBaseType_t)1U,
        xExitTaskStack,
        &xExitTaskTCB);
    if ((guiTask == NULL) || (agentTask == NULL) || (exitTask == NULL))
    {
        puts("Unable to create the GUI tasks.");
        dos_agent_shutdown();
        return 1;
    }

    vTaskStartScheduler();
#if defined(FREERTOSX86_SMOKE)
    if (usSmokeTest != 0U)
    {
        usSmokeHgcPagesMatch = gui_hgc_pages_match();
    }
#endif
#if defined(FREERTOSX86_USER_APP)
    user_app_shutdown();
#endif
    gui_shutdown();
    dos_agent_shutdown();
    if (usSmokeTest != 0U)
    {
        prvWriteSmokeResult(
            (gui_form_was_drawn() != 0U) ? "PASS" : "FAIL");
    }
    puts("FreeRTOS GUI stopped.");
    return 0;
}
