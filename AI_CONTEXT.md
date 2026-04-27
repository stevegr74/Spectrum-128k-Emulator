Project: ZX Spectrum 128K Emulator (C# WinForms)

Current issue:
Exolon crashes due to interrupt stall:
- IFF1=0
- INTP=1
- No INT_ACCEPT

Facts:
- EI works correctly
- Stack bug was fixed
- Current loop at B8CD <-> C119 <-> C1D3
- Interrupts never re-enabled

Goal:
Find why interrupts are not re-enabled in this loop.
