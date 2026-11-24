@echo off
rem Publish may chu (phu thuoc framework: may dich chi can .NET 8 Runtime) vao ServerBin\
cd /d "%~dp0.."
dotnet publish Server\LastKingServer.csproj -c Release -o ServerBin --no-self-contained -p:UseAppHost=false -p:DebugType=None -p:DebugSymbols=false
