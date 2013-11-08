REM echo off
call %1 x64
cd ..\Ext\Z3\z3_
python scripts\mk_make.py -b build\x64 -x
cd build\x64
nmake
