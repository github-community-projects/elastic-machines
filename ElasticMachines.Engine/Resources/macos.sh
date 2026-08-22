#!/bin/sh -l

set -eu

DOWNLOAD_URL="{{DOWNLOAD_URL}}"
DOWNLOAD_TOKEN="{{DOWNLOAD_TOKEN}}"
DOT_RUNNER_FILE="{{DOT_RUNNER_FILE}}"
DOT_CREDENTIALS_FILE="{{DOT_CREDENTIALS_FILE}}"
DOT_CREDENTIALS_RSAPARAMS_FILE="{{DOT_CREDENTIALS_RSAPARAMS_FILE}}"

USERNAME="runneruser"
GITHUB_RUNNER_HOME="/home/$USERNAME/runner"

# Create user and add to admin group
if [ ! "$(sudo dscl . -search /Users RecordName "$USERNAME")" ]; then
    sudo dscl . -create /Users/"$USERNAME"                                     # Creating user
    sudo dscl . -create /Users/"$USERNAME" UniqueID 1000                       # Set Unique ID (First Id is 501 going up, so 1000 is pretty safe)
    sudo dscl . -create /Users/"$USERNAME" PrimaryGroupID 20                   # 20 is regular staff account
    sudo dscl . -create /Users/"$USERNAME" UserShell /bin/zsh                  # Set shell
    sudo dscl . -create /Users/"$USERNAME" NFSHomeDirectory "/Users/$USERNAME" # Set user home directory
    sudo dscl . -append /Groups/admin GroupMembership $USERNAME                # Add user to admin group
    sudo dseditgroup -o edit -a $USERNAME -t user admin                        # Add user to admin group
    echo "Created user '$USERNAME'"
fi

# Install runner if needed
if [ ! -f "$GITHUB_RUNNER_HOME/run.sh" ]; then
    mkdir -p "$GITHUB_RUNNER_HOME"
    curl -fsSL --retry 3 --header "Authorization: Bearer $DOWNLOAD_TOKEN" "$DOWNLOAD_URL" | tar xz -C "$GITHUB_RUNNER_HOME"
    echo "Installed runner to directory '$GITHUB_RUNNER_HOME'"
fi

# Create configuration files
echo "$DOT_RUNNER_FILE" | base64 --decode >"$GITHUB_RUNNER_HOME/.runner"
echo "$DOT_CREDENTIALS_FILE" | base64 --decode >"$GITHUB_RUNNER_HOME/.credentials"
echo "$DOT_CREDENTIALS_RSAPARAMS_FILE" | base64 --decode >"$GITHUB_RUNNER_HOME/.credentials_rsaparams"
echo "Created configuration files"

chown -R "$USERNAME" "$GITHUB_RUNNER_HOME"
su -l "$USERNAME" -c "$GITHUB_RUNNER_HOME/run.sh >/dev/null 2>&1 &"

# The orchestrator will look for this final output to determine if the this script succeeded
echo "Started runner in background"
