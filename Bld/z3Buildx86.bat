REM echo off
call %1 x86
cd ..\Ext\Z3\z3_
python scripts\mk_make.py -b build\x86
cd build\x86
nmake
