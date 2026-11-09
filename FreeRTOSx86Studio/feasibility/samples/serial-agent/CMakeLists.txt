cmake_minimum_required(VERSION 3.13)
project(MacStudioAgent C)

add_application(MacStudioAgent
    main.c
    agent.r
)

add_application(AgentIpcProbe
    debug-client.c
    debug-client.r
)

add_application(ZAgentLaunchProbe
    launch-client.c
    launch-client.r
)

add_application(AgentLaunchTarget
    launch-target.c
    launch-target.r
)

set_target_properties(MacStudioAgent PROPERTIES
    COMPILE_OPTIONS -ffunction-sections
    LINK_FLAGS "-Wl,-gc-sections -Wl,--mac-single"
)

set_target_properties(AgentIpcProbe PROPERTIES
    COMPILE_OPTIONS -ffunction-sections
    LINK_FLAGS "-Wl,-gc-sections -Wl,--mac-single"
)

set_target_properties(ZAgentLaunchProbe AgentLaunchTarget PROPERTIES
    COMPILE_OPTIONS -ffunction-sections
    LINK_FLAGS "-Wl,-gc-sections -Wl,--mac-single"
)
