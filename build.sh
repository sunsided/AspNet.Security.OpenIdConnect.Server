#!/bin/bash

if test `uname` = Darwin; then
    cachedir=~/Library/Caches/KBuild
else
    if [ -z $XDG_DATA_HOME ]; then
        cachedir=$HOME/.local/share
    else
        cachedir=$XDG_DATA_HOME;
    fi
fi
mkdir -p $cachedir

url=https://www.nuget.org/nuget.exe

if test ! -f $cachedir/nuget.exe; then
    wget -O $cachedir/nuget.exe $url 2>/dev/null || curl -o $cachedir/nuget.exe --location $url /dev/null
fi

if test ! -e .nuget; then
    mkdir .nuget
    cp $cachedir/nuget.exe .nuget/NuGet.exe
fi

if test ! -d packages/Sake; then
    mono .nuget/NuGet.exe install Sake -version 0.2 -o packages -ExcludeVersion
fi

mono .nuget/NuGet.exe restore
mono packages/Sake/tools/Sake.exe -f makefile.shade "$@"

