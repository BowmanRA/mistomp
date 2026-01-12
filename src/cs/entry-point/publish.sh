#!/bin/bash

echo linux arm64 ...
dotnet publish --runtime linux-arm64 --self-contained

echo linux amd64 ...
dotnet publish --runtime linux-amd64 --self-contained
