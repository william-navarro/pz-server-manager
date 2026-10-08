# Architecture study

PZ Server Manager combines the interfaces exposed by a Project Zomboid dedicated server instead of relying on an in-process plugin API:

- systemd and a FIFO provide lifecycle control and console input;
- journalctl provides live logs;
- Source RCON provides typed player and administration commands;
- INI and SandboxVars parsers preserve unknown data and write atomically;
- SteamCMD and Workshop metadata provide game and mod updates;
- narrowly scoped root-owned services perform backup, restore, reset and update operations;
- the optional Cardinal bridge exchanges allow-listed actions through JSON stored in permitted `.txt` files.

The solution separates Domain, Application, Infrastructure and Blazor Server Web projects. The managed Linux mode is the supported deployment target. The older multi-instance abstractions remain buildable and are the starting point for future Windows support.
