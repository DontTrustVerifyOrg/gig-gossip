#!/bin/bash

set -e

if [ -n "$GITHUB_CONFIG_URL" ] && [ -n "$GITHUB_USERNAME" ] && [ -n "$GITHUB_TOKEN" ]; then
    echo "Downloading configuration file from GitHub"
    curl -sS --fail -u $GITHUB_USERNAME:$GITHUB_TOKEN -o /app/data/$2 $GITHUB_CONFIG_URL
    echo "Configuration file downloaded successfully from GitHub"

    if [ -n "$AZURE_KEY_VAULT_URL" ] && [ -n "$AZURE_CLIENT_ID" ] && [ -n "$AZURE_CLIENT_SECRET" ] && [ -n "$AZURE_TENANT_ID" ]; then
        echo "Fetching access token from Azure Key Vault"
        ACCESS_TOKEN=$(curl -sS --fail -X POST -H "Content-Type: application/x-www-form-urlencoded" -d "grant_type=client_credentials&client_id=$AZURE_CLIENT_ID&client_secret=$AZURE_CLIENT_SECRET&resource=https://vault.azure.net" https://login.microsoftonline.com/$AZURE_TENANT_ID/oauth2/token | jq -r .access_token)
        if [ $? -ne 0 ] || [ -z "$ACCESS_TOKEN" ]; then
            echo "Error: Failed to retrieve access token from Azure Key Vault"
            exit 1
        fi
        echo "Access token fetched successfully from Azure Key Vault"
        
        # Read the config file and replace placeholders
        while IFS= read -r line; do
            # Find placeholders in the format ${VAR_NAME}
            if [[ $line =~ \$\{[a-zA-Z_][a-zA-Z0-9_-]*\} ]]; then

                var_name=${BASH_REMATCH[0]}
                var_name=${var_name:2:-1}
                echo "Fetching $var_name from Azure Key Vault"
                # Fetch the value from Azure Key Vault
                var_value=$(curl -sS --fail -H "Authorization: Bearer $ACCESS_TOKEN" https://$AZURE_KEY_VAULT_URL.vault.azure.net/secrets/$var_name?api-version=7.0 | jq -r .value)
                if [ $? -ne 0 ] || [ -z "$var_value" ]; then
                    echo "Error: Failed to fetch $var_name from Azure Key Vault"
                    exit 1
                fi
                echo "$var_name fetched successfully from Azure Key Vault"
                # Replace the placeholder with the actual value
                sed -i "s|\${$var_name}|$(echo "$var_value" | sed 's/[&/\]/\\&/g')|g" /app/data/$2
            fi
        done < /app/data/$2
        
    fi
elif [ -e /app/data/$2 ]; then
    echo "Using existing configuration file"
else
    echo "Creating configuration file from template and environment variables"
    envsubst < /app/$2.template > /app/data/$2
fi


echo
echo "Starting: dotnet $1 --basedir=/app/data"
echo
dotnet $1 --basedir=/app/data
