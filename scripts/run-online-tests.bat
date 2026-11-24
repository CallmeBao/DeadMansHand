@echo off
rem Chay bo kiem thu Online PvP bang dotnet (may chu that + client that qua socket loopback, khoang 1 phut).
rem Them ten nhom de loc:  scripts\run-online-tests.bat tls   hoac   resilience   hoac   list
cd /d "%~dp0.."
dotnet run -c Release --project Tests\OnlineTests -- %*
