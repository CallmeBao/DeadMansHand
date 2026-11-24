#!/usr/bin/env bash
# Publish máy chủ (phụ thuộc framework: máy đích chỉ cần .NET 8 Runtime) vào ServerBin/.
cd "$(dirname "$0")/.."
dotnet publish Server/LastKingServer.csproj -c Release -o ServerBin --no-self-contained -p:UseAppHost=false -p:DebugType=None -p:DebugSymbols=false
