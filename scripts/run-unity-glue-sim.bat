@echo off
rem Mo phong phan keo dan Unity (controller + giao dien online that, stub UnityEngine, may chu that, bot doi thu).
cd /d "%~dp0.."
dotnet run -c Release --project Tests\UnityGlueSim -- %*
