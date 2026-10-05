@echo off
setlocal
pushd "%~dp0"
if not exist bin mkdir bin
set "CXX=g++"
if exist "C:\MinGW32\bin\g++.exe" set "CXX=C:\MinGW32\bin\g++.exe"
if defined DFR_CXX set "CXX=%DFR_CXX%"
"%CXX%" -std=c++11 -O2 main.cpp native_ui.cpp -o bin\DFRNativePrototype.exe -lsetupapi -lgdi32 -lhid -lwinmm -lpowrprof -lole32
if errorlevel 1 (
	set "buildResult=%errorlevel%"
	popd
	exit /b %buildResult%
)
popd
echo Built bin\DFRNativePrototype.exe