@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist %CSC% set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
%CSC% /out:DesktopRestore.exe restore.cs
echo Compilation complete.
pause