#!/bin/bash

set -e

if [ -n "$GITHUB_CONFIG_URL" ] && [ -n "$GITHUB_USERNAME" ] && [ -n "$GITHUB_TOKEN" ]; then
    echo "Downloading configuration file from GitHub"
    curl -sS --fail -u $GITHUB_USERNAME:$GITHUB_TOKEN -o /app_data/lnd.conf $GITHUB_CONFIG_URL
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
                sed -i "s|\${$var_name}|$(echo "$var_value" | sed 's/[&/\]/\\&/g')|g" /app_data/lnd.conf
            fi
        done < /app_data/lnd.conf
    fi
elif [ -e /app_data/lnd.conf ]; then
    echo "Using existing configuration file"
else
    echo "Creating configuration file from template and environment variables"
    envsubst < /app/lnd.conf.template > /app_data/lnd.conf
fi


if [ ! -v INIT ]; then
    if [ -n "$AZURE_KEY_VAULT_URL" ] && [ -n "$AZURE_CLIENT_ID" ] && [ -n "$AZURE_CLIENT_SECRET" ] && [ -n "$AZURE_TENANT_ID" ]; then
        echo "Fetching access token from Azure Key Vault"
        ACCESS_TOKEN=$(curl -sS --fail -X POST -H "Content-Type: application/x-www-form-urlencoded" -d "grant_type=client_credentials&client_id=$AZURE_CLIENT_ID&client_secret=$AZURE_CLIENT_SECRET&resource=https://vault.azure.net" https://login.microsoftonline.com/$AZURE_TENANT_ID/oauth2/token | jq -r .access_token)
        if [ $? -ne 0 ] || [ -z "$ACCESS_TOKEN" ]; then
            echo "Error: Failed to retrieve access token from Azure Key Vault"
            exit 1
        fi
        echo "Access token fetched successfully from Azure Key Vault"
        
        echo "Fetching secret key from Azure Key Vault"
        SECRET_KEY=$(curl -sS --fail -H "Authorization: Bearer $ACCESS_TOKEN" https://$AZURE_KEY_VAULT_URL.vault.azure.net/secrets/secret-key?api-version=7.0 | jq -r .value)
        if [ $? -ne 0 ] || [ -z "$SECRET_KEY" ]; then
            echo "Error: Failed to fetch secret key from Azure Key Vault"
            exit 1
        fi
        echo "$SECRET_KEY" > /secret/password.txt
        echo "Secret key saved to /secret/password.txt"
    elif [ -n "$GITHUB_PASSWORD_URL" ] && [ -n "$GITHUB_USERNAME" ] && [ -n "$GITHUB_TOKEN" ]; then
        echo "Fetching secret key from GitHub"
        curl -sS --fail -u $GITHUB_USERNAME:$GITHUB_TOKEN -o /secret/password.txt $GITHUB_PASSWORD_URL
        if [ $? -ne 0 ]; then
            echo "Error: Failed to fetch secret key from GitHub"
            exit 1
        fi
        echo "Secret key saved to /secret/password.txt"
    elif [ -e /secret/password.txt ]; then
        echo "Using existing secret key file in /secret/password.txt"
    else
        echo "Error: Secret key not provided"
        exit 1
    fi
fi


echo
if [ -v INIT ]; then
    echo "Starting in init mode: lnd --lnddir=/app_data"
    echo
    lnd --lnddir=/app_data
else
    echo "Starting: lnd --lnddir=/app_data --wallet-unlock-password-file=/secret/password.txt"
    echo
    lnd --lnddir=/app_data --wallet-unlock-password-file=/secret/password.txt
fi
