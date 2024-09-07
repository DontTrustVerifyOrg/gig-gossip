#!/bin/sh

if [ -e $2 ]
then
    echo "Using existing configuration file"
else
    echo "Creating configuration file from template and environment variables"
    envsubst < $2.template > $2
fi


echo "\nStarting: dotnet $1 --basedir=/app/data\n"
dotnet $1 --basedir=/app/data