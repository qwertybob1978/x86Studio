cmake_minimum_required(VERSION 3.13)
project(HelloMac C)

add_application(HelloMac
    hello_mac.c
    hello_mac.r
)

set_target_properties(HelloMac PROPERTIES
    COMPILE_OPTIONS -ffunction-sections
    LINK_FLAGS "-Wl,-gc-sections -Wl,--mac-single"
)
