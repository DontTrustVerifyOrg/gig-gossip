#!/bin/sh

if [ -e $2 ]
then
    echo "Using existing configuration file"
else
    echo "Creating configuration file from template and environment variables"
    envsubst < $2.template > $2
fi

echo "\nConfiguration file:"
cat $2

echo "\n\nStarting: dotnet $1 --basedir=/app/data"
dotnet $1 --basedir=/app/data