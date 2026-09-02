#!/usr/bin/env bash
dotnet publish -r linux-x64 -c Release /p:UseAppHost=true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true --self-contained true
