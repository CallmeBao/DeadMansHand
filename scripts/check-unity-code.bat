@echo off
rem Kiem tra BIEN DICH ma Unity (Scripts + Net + Editor check) bang stub UnityEngine, truoc khi mo Unity.
cd /d "%~dp0.."
dotnet build Tools\UnityStubs\UnityCompileCheck.csproj -nologo -v q && echo Bien dich stub: OK (khong thay the build trong Unity that)
