.8086
.MODEL LARGE

PUBLIC _hgc_asm_set_mode
PUBLIC _hgc_asm_set_page
PUBLIC _hgc_asm_wait_vsync
PUBLIC _hgc_asm_clear
PUBLIC _hgc_asm_flush_row
PUBLIC _hgc_asm_copy_rect
PUBLIC _hgc_asm_pixel
PUBLIC _hgc_asm_line
PUBLIC _hgc_asm_rectangle
PUBLIC _hgc_asm_circle
PUBLIC _hgc_asm_shutdown

.CODE

_hgc_asm_set_mode PROC FAR
    push ax
    push dx

    mov dx, 03BFh
    mov al, 03h
    out dx, al
    mov dx, 03B8h
    xor al, al
    out dx, al

    mov dx, 03B4h
    mov al, 00h
    out dx, al
    mov dx, 03B5h
    mov al, 35h
    out dx, al
    mov dx, 03B4h
    mov al, 01h
    out dx, al
    mov dx, 03B5h
    mov al, 2Dh
    out dx, al
    mov dx, 03B4h
    mov al, 02h
    out dx, al
    mov dx, 03B5h
    mov al, 2Eh
    out dx, al
    mov dx, 03B4h
    mov al, 03h
    out dx, al
    mov dx, 03B5h
    mov al, 07h
    out dx, al
    mov dx, 03B4h
    mov al, 04h
    out dx, al
    mov dx, 03B5h
    mov al, 5Bh
    out dx, al
    mov dx, 03B4h
    mov al, 05h
    out dx, al
    mov dx, 03B5h
    mov al, 02h
    out dx, al
    mov dx, 03B4h
    mov al, 06h
    out dx, al
    mov dx, 03B5h
    mov al, 57h
    out dx, al
    mov dx, 03B4h
    mov al, 07h
    out dx, al
    mov dx, 03B5h
    mov al, 57h
    out dx, al
    mov dx, 03B4h
    mov al, 08h
    out dx, al
    mov dx, 03B5h
    mov al, 02h
    out dx, al
    mov dx, 03B4h
    mov al, 09h
    out dx, al
    mov dx, 03B5h
    mov al, 03h
    out dx, al
    mov dx, 03B4h
    mov al, 0Ah
    out dx, al
    mov dx, 03B5h
    xor al, al
    out dx, al
    mov dx, 03B4h
    mov al, 0Bh
    out dx, al
    mov dx, 03B5h
    xor al, al
    out dx, al
    mov dx, 03B4h
    mov al, 0Ch
    out dx, al
    mov dx, 03B5h
    xor al, al
    out dx, al
    mov dx, 03B4h
    mov al, 0Dh
    out dx, al
    mov dx, 03B5h
    xor al, al
    out dx, al

    mov dx, 03B8h
    mov al, 0Ah
    out dx, al
    pop dx
    pop ax
    retf
_hgc_asm_set_mode ENDP

_hgc_asm_set_page PROC FAR
    push bp
    mov bp, sp
    push ax
    push dx
    mov dx, 03B8h
    mov al, 0Ah
    cmp word ptr [bp+6], 0
    je page_ready
    or al, 80h
page_ready:
    out dx, al
    pop dx
    pop ax
    pop bp
    retf
_hgc_asm_set_page ENDP

_hgc_asm_wait_vsync PROC FAR
    push ax
    push cx
    push dx
    mov dx, 03BAh
    mov cx, 2000h
wait_vsync:
    in al, dx
    test al, 08h
    jnz wait_vsync_done
    loop wait_vsync
wait_vsync_done:
    pop dx
    pop cx
    pop ax
    retf
_hgc_asm_wait_vsync ENDP

_hgc_asm_clear PROC FAR
    push bp
    mov bp, sp
    push ax
    push cx
    push di
    push es

    mov ax, 0B000h
    cmp word ptr [bp+6], 0
    je clear_page_ready
    mov ax, 0B800h
clear_page_ready:
    mov es, ax
    xor di, di
    xor ax, ax
    mov cx, 4000h
    cld
    rep stosw

    pop es
    pop di
    pop cx
    pop ax
    pop bp
    retf
_hgc_asm_clear ENDP

_hgc_asm_flush_row PROC FAR
    push bp
    mov bp, sp
    sub sp, 4
    push ax
    push bx
    push cx
    push dx
    push si
    push di
    push ds
    push es

    mov ax, word ptr [bp+20]
    mov word ptr [bp-2], ax

    mov ax, word ptr [bp+16]
    test ax, 7
    jnz flush_general
    mov ax, word ptr [bp+18]
    and ax, 7
    cmp ax, 7
    jne flush_general

    mov ax, word ptr [bp+6]
    mov ds, ax
    mov ax, word ptr [bp+10]
    mul word ptr [bp+12]
    add ax, word ptr [bp+8]
    add ax, word ptr [bp+14]
    mov si, ax

    mov ax, 0B000h
    cmp word ptr [bp+22], 0
    je flush_fast_segment_ready
    mov ax, 0B800h
flush_fast_segment_ready:
    mov es, ax

    mov ax, word ptr [bp-2]
    and ax, 3
    mov cx, 2000h
    mul cx
    mov di, ax
    mov ax, word ptr [bp-2]
    mov cl, 2
    shr ax, cl
    mov cx, 90
    mul cx
    add di, ax
    mov ax, word ptr [bp+16]
    mov cl, 3
    shr ax, cl
    add di, ax

    mov ax, word ptr [bp+18]
    sub ax, word ptr [bp+16]
    inc ax
    mov cl, 3
    shr ax, cl
    mov cx, ax
    cld
flush_fast_byte_loop:
    xor bx, bx
    mov dx, 8
flush_fast_pixel_loop:
    shl bl, 1
    lodsb
    test al, al
    jz flush_fast_pixel_off
    or bl, 1
flush_fast_pixel_off:
    dec dx
    jnz flush_fast_pixel_loop
    mov es:[di], bl
    inc di
    loop flush_fast_byte_loop
    jmp flush_row_done

flush_general:
    mov ax, word ptr [bp+16]
    and ax, 0FFF8h
    mov word ptr [bp-4], ax

    mov ax, word ptr [bp+6]
    mov ds, ax
    mov ax, word ptr [bp+10]
    mul word ptr [bp+12]
    add ax, word ptr [bp+8]
    add ax, word ptr [bp+14]
    mov si, ax

    mov ax, word ptr [bp-2]
    and ax, 3
    mov cx, 2000h
    mul cx
    mov di, ax
    mov ax, word ptr [bp-2]
    mov cl, 2
    shr ax, cl
    mov cx, 90
    mul cx
    add di, ax
    mov ax, 0B000h
    cmp word ptr [bp+22], 0
    je flush_general_segment_ready
    mov ax, 0B800h
flush_general_segment_ready:
    mov es, ax

    mov ax, word ptr [bp-4]
    mov cl, 3
    shr ax, cl
    add di, ax

flush_byte_loop:
    xor bx, bx
    mov al, 80h
    mov cx, 8
flush_pixel_loop:
    mov dx, word ptr [bp-4]
    cmp dx, word ptr [bp+16]
    jb flush_pixel_skip
    cmp dx, word ptr [bp+18]
    ja flush_pixel_skip
    or bh, al
    cmp byte ptr ds:[si], 0
    je flush_pixel_off
    or bl, al
flush_pixel_off:
    inc si
flush_pixel_skip:
    inc word ptr [bp-4]
    shr al, 1
    loop flush_pixel_loop

    mov al, es:[di]
    not bh
    and al, bh
    or al, bl
    mov es:[di], al
    inc di
    mov ax, word ptr [bp-4]
    cmp ax, word ptr [bp+18]
    jbe flush_byte_loop

flush_row_done:
    pop es
    pop ds
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    mov sp, bp
    pop bp
    retf
_hgc_asm_flush_row ENDP

_hgc_asm_copy_rect PROC FAR
    push bp
    mov bp, sp
    sub sp, 10
    push ax
    push bx
    push cx
    push dx
    push si
    push di
    push ds
    push es

    mov ax, word ptr [bp+10]
    cmp ax, 720
    jae copy_rect_done
    mov ax, word ptr [bp+14]
    cmp ax, 720
    jae copy_rect_done
    mov ax, word ptr [bp+12]
    cmp ax, 348
    jae copy_rect_done
    mov ax, word ptr [bp+16]
    cmp ax, 348
    jae copy_rect_done
    mov ax, word ptr [bp+10]
    cmp ax, word ptr [bp+14]
    ja copy_rect_done
    mov ax, word ptr [bp+12]
    cmp ax, word ptr [bp+16]
    ja copy_rect_done

    mov ax, 0B000h
    cmp word ptr [bp+6], 0
    je copy_rect_source_ready
    mov ax, 0B800h
copy_rect_source_ready:
    mov ds, ax
    mov ax, 0B000h
    cmp word ptr [bp+8], 0
    je copy_rect_destination_ready
    mov ax, 0B800h
copy_rect_destination_ready:
    mov es, ax

    mov ax, word ptr [bp+10]
    mov cl, 3
    shr ax, cl
    mov word ptr [bp-4], ax
    mov ax, word ptr [bp+14]
    mov cl, 3
    shr ax, cl
    mov word ptr [bp-6], ax
    mov ax, word ptr [bp+12]
    mov word ptr [bp-2], ax
    cld

copy_rect_row:
    mov ax, word ptr [bp-2]
    and ax, 3
    mov cx, 2000h
    mul cx
    mov si, ax
    mov ax, word ptr [bp-2]
    mov cl, 2
    shr ax, cl
    mov cx, 90
    mul cx
    add si, ax
    add si, word ptr [bp-4]
    mov di, si
    mov ax, word ptr [bp-4]
    mov word ptr [bp-8], ax

copy_rect_byte:
    mov bl, 0FFh
    mov ax, word ptr [bp-8]
    cmp ax, word ptr [bp-4]
    jne copy_rect_check_end
    mov cx, word ptr [bp+10]
    and cx, 7
    shr bl, cl
copy_rect_check_end:
    mov ax, word ptr [bp-8]
    cmp ax, word ptr [bp-6]
    jne copy_rect_copy_byte
    mov cx, word ptr [bp+14]
    and cx, 7
    mov al, 7
    sub al, cl
    mov cl, al
    shl bl, cl
copy_rect_copy_byte:
    cmp bl, 0FFh
    jne copy_rect_masked_byte
    movsb
    jmp copy_rect_advance
copy_rect_masked_byte:
    mov al, ds:[si]
    and al, bl
    mov ah, es:[di]
    not bl
    and ah, bl
    or al, ah
    mov es:[di], al
    inc si
    inc di
copy_rect_advance:
    inc word ptr [bp-8]
    mov ax, word ptr [bp-8]
    cmp ax, word ptr [bp-6]
    jbe copy_rect_byte

    inc word ptr [bp-2]
    mov ax, word ptr [bp-2]
    cmp ax, word ptr [bp+16]
    jbe copy_rect_row

copy_rect_done:
    pop es
    pop ds
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    mov sp, bp
    pop bp
    retf
_hgc_asm_copy_rect ENDP

_hgc_asm_pixel PROC FAR
    push bp
    mov bp, sp
    push ax
    push bx
    push cx
    push dx
    push di
    push es

    mov ax, word ptr [bp+6]
    cmp ax, 720
    jae pixel_done
    mov ax, word ptr [bp+8]
    cmp ax, 348
    jae pixel_done

    mov dx, ax
    and ax, 3
    mov di, 0
    cmp ax, 0
    je pixel_bank_ready
    cmp ax, 1
    jne pixel_check_bank_two
    mov di, 2000h
    jmp pixel_bank_ready
pixel_check_bank_two:
    cmp ax, 2
    jne pixel_bank_three
    mov di, 4000h
    jmp pixel_bank_ready
pixel_bank_three:
    mov di, 6000h
pixel_bank_ready:
    mov ax, dx
    mov cl, 2
    shr ax, cl
    mov cx, 90
    mul cx
    add di, ax

    mov dx, word ptr [bp+6]
    mov ax, dx
    and ax, 7
    xor ax, 7
    mov cl, al
    shr dx, 1
    shr dx, 1
    shr dx, 1
    add di, dx
    mov bl, 1
    shl bl, cl

    mov ax, 0B000h
    cmp word ptr [bp+12], 0
    je pixel_segment_ready
    mov ax, 0B800h
pixel_segment_ready:
    mov es, ax
    mov al, es:[di]
    cmp word ptr [bp+10], 0
    je pixel_clear_bit
    or al, bl
    jmp pixel_store
pixel_clear_bit:
    not bl
    and al, bl
pixel_store:
    mov es:[di], al

pixel_done:
    pop es
    pop di
    pop dx
    pop cx
    pop bx
    pop ax
    pop bp
    retf
_hgc_asm_pixel ENDP

_hgc_asm_line PROC FAR
    push bp
    mov bp, sp
    sub sp, 20
    push ax
    push bx
    push cx
    push dx
    push si
    push di

    mov ax, word ptr [bp+6]
    mov word ptr [bp-2], ax
    mov ax, word ptr [bp+8]
    mov word ptr [bp-4], ax
    mov ax, word ptr [bp+10]
    mov word ptr [bp-6], ax
    mov ax, word ptr [bp+12]
    mov word ptr [bp-8], ax

    mov ax, word ptr [bp-6]
    sub ax, word ptr [bp-2]
    jns line_dx_positive
    neg ax
    mov word ptr [bp-14], -1
    jmp line_dx_ready
line_dx_positive:
    mov word ptr [bp-14], 1
line_dx_ready:
    mov word ptr [bp-10], ax

    mov ax, word ptr [bp-8]
    sub ax, word ptr [bp-4]
    jns line_dy_positive
    neg ax
    mov word ptr [bp-16], -1
    jmp line_dy_ready
line_dy_positive:
    mov word ptr [bp-16], 1
line_dy_ready:
    neg ax
    mov word ptr [bp-12], ax
    mov ax, word ptr [bp-10]
    add ax, word ptr [bp-12]
    mov word ptr [bp-18], ax

line_loop:
    xor ax, ax
    push ax
    inc ax
    push ax
    push word ptr [bp-4]
    push word ptr [bp-2]
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp-2]
    cmp ax, word ptr [bp-6]
    jne line_continue
    mov ax, word ptr [bp-4]
    cmp ax, word ptr [bp-8]
    je line_done

line_continue:
    mov ax, word ptr [bp-18]
    shl ax, 1
    mov word ptr [bp-20], ax
    cmp ax, word ptr [bp-12]
    jl line_skip_x
    mov ax, word ptr [bp-18]
    add ax, word ptr [bp-12]
    mov word ptr [bp-18], ax
    mov ax, word ptr [bp-2]
    add ax, word ptr [bp-14]
    mov word ptr [bp-2], ax
line_skip_x:
    mov ax, word ptr [bp-20]
    cmp ax, word ptr [bp-10]
    jg line_skip_y
    mov ax, word ptr [bp-18]
    add ax, word ptr [bp-10]
    mov word ptr [bp-18], ax
    mov ax, word ptr [bp-4]
    add ax, word ptr [bp-16]
    mov word ptr [bp-4], ax
line_skip_y:
    jmp line_loop

line_done:
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    mov sp, bp
    pop bp
    retf
_hgc_asm_line ENDP

_hgc_asm_rectangle PROC FAR
    push bp
    mov bp, sp
    sub sp, 12
    push ax
    push bx
    push cx
    push dx
    push si
    push di

    cmp word ptr [bp+10], 0
    je rectangle_done
    cmp word ptr [bp+12], 0
    je rectangle_done
    mov ax, word ptr [bp+6]
    cmp ax, 720
    jae rectangle_done
    add ax, word ptr [bp+10]
    jc rectangle_done
    cmp ax, 720
    ja rectangle_done
    dec ax
    mov word ptr [bp-2], ax
    mov ax, word ptr [bp+8]
    cmp ax, 348
    jae rectangle_done
    add ax, word ptr [bp+12]
    jc rectangle_done
    cmp ax, 348
    ja rectangle_done
    dec ax
    mov word ptr [bp-4], ax
    mov ax, word ptr [bp+6]
    mov word ptr [bp-10], ax
    mov ax, word ptr [bp+8]
    mov word ptr [bp-12], ax
    mov word ptr [bp-8], ax

rectangle_row:
    mov ax, word ptr [bp-10]
    mov word ptr [bp-6], ax
rectangle_column:
    cmp word ptr [bp+14], 0
    jne rectangle_draw_pixel
    mov ax, word ptr [bp-6]
    cmp ax, word ptr [bp-10]
    je rectangle_draw_pixel
    cmp ax, word ptr [bp-2]
    je rectangle_draw_pixel
    mov ax, word ptr [bp-8]
    cmp ax, word ptr [bp-12]
    je rectangle_draw_pixel
    cmp ax, word ptr [bp-4]
    jne rectangle_next_pixel

rectangle_draw_pixel:
    xor ax, ax
    push ax
    inc ax
    push ax
    push word ptr [bp-8]
    push word ptr [bp-6]
    call FAR PTR _hgc_asm_pixel
    add sp, 8

rectangle_next_pixel:
    mov ax, word ptr [bp-6]
    cmp ax, word ptr [bp-2]
    jae rectangle_next_row
    inc word ptr [bp-6]
    jmp rectangle_column

rectangle_next_row:
    mov ax, word ptr [bp-8]
    cmp ax, word ptr [bp-4]
    jae rectangle_done
    inc word ptr [bp-8]
    jmp rectangle_row

rectangle_done:
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    mov sp, bp
    pop bp
    retf
_hgc_asm_rectangle ENDP

_hgc_asm_circle PROC FAR
    push bp
    mov bp, sp
    sub sp, 6
    push ax
    push bx
    push cx
    push dx
    push si
    push di

    mov ax, word ptr [bp+10]
    mov word ptr [bp-2], ax
    xor ax, ax
    mov word ptr [bp-4], ax
    mov ax, 1
    sub ax, word ptr [bp+10]
    mov word ptr [bp-6], ax

circle_loop:
    mov ax, word ptr [bp-2]
    cmp ax, word ptr [bp-4]
    jb circle_done
    cmp word ptr [bp+12], 0
    jne circle_fill

    mov ax, word ptr [bp+6]
    add ax, word ptr [bp-2]
    mov dx, word ptr [bp+8]
    add dx, word ptr [bp-4]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp+6]
    add ax, word ptr [bp-4]
    mov dx, word ptr [bp+8]
    add dx, word ptr [bp-2]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-4]
    mov dx, word ptr [bp+8]
    add dx, word ptr [bp-2]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-2]
    mov dx, word ptr [bp+8]
    add dx, word ptr [bp-4]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-2]
    mov dx, word ptr [bp+8]
    sub dx, word ptr [bp-4]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-4]
    mov dx, word ptr [bp+8]
    sub dx, word ptr [bp-2]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp+6]
    add ax, word ptr [bp-4]
    mov dx, word ptr [bp+8]
    sub dx, word ptr [bp-2]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8

    mov ax, word ptr [bp+6]
    add ax, word ptr [bp-2]
    mov dx, word ptr [bp+8]
    sub dx, word ptr [bp-4]
    mov si, ax
    mov di, dx
    xor ax, ax
    push ax
    inc ax
    push ax
    push di
    push si
    call FAR PTR _hgc_asm_pixel
    add sp, 8
    jmp circle_advance

circle_fill:
    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-2]
    mov bx, word ptr [bp+6]
    add bx, word ptr [bp-2]
    mov dx, word ptr [bp+8]
    add dx, word ptr [bp-4]
    push dx
    push bx
    push dx
    push ax
    call FAR PTR _hgc_asm_line
    add sp, 8

    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-2]
    mov bx, word ptr [bp+6]
    add bx, word ptr [bp-2]
    mov dx, word ptr [bp+8]
    sub dx, word ptr [bp-4]
    push dx
    push bx
    push dx
    push ax
    call FAR PTR _hgc_asm_line
    add sp, 8

    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-4]
    mov bx, word ptr [bp+6]
    add bx, word ptr [bp-4]
    mov dx, word ptr [bp+8]
    add dx, word ptr [bp-2]
    push dx
    push bx
    push dx
    push ax
    call FAR PTR _hgc_asm_line
    add sp, 8

    mov ax, word ptr [bp+6]
    sub ax, word ptr [bp-4]
    mov bx, word ptr [bp+6]
    add bx, word ptr [bp-4]
    mov dx, word ptr [bp+8]
    sub dx, word ptr [bp-2]
    push dx
    push bx
    push dx
    push ax
    call FAR PTR _hgc_asm_line
    add sp, 8

circle_advance:
    inc word ptr [bp-4]
    mov ax, word ptr [bp-6]
    cmp ax, 0
    jl circle_inside
    dec word ptr [bp-2]
    mov bx, word ptr [bp-4]
    sub bx, word ptr [bp-2]
    shl bx, 1
    inc bx
    add word ptr [bp-6], bx
    jmp circle_loop
circle_inside:
    mov bx, word ptr [bp-4]
    shl bx, 1
    inc bx
    add word ptr [bp-6], bx
    jmp circle_loop

circle_done:
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    mov sp, bp
    pop bp
    retf
_hgc_asm_circle ENDP

_hgc_asm_shutdown PROC FAR
    push ax
    push dx
    mov dx, 03B8h
    xor al, al
    out dx, al
    mov dx, 03BFh
    xor al, al
    out dx, al
    pop dx
    pop ax
    retf
_hgc_asm_shutdown ENDP

END
