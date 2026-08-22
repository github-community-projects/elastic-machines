#!/bin/sh -l

set -eu

DOWNLOAD_URL="{{DOWNLOAD_URL}}"
DOWNLOAD_TOKEN="{{DOWNLOAD_TOKEN}}"
DOT_RUNNER_FILE="{{DOT_RUNNER_FILE}}"
DOT_CREDENTIALS_FILE="{{DOT_CREDENTIALS_FILE}}"
DOT_CREDENTIALS_RSAPARAMS_FILE="{{DOT_CREDENTIALS_RSAPARAMS_FILE}}"

USERNAME="runneruser"
GITHUB_RUNNER_HOME="/home/$USERNAME/runner"

# Create user
if [ ! "$(getent passwd "$USERNAME")" ]; then
    useradd --create-home "$USERNAME"
    echo "Created user '$USERNAME'"
fi

# Add user to groups
for GROUP in "sudo" "docker"; do
    if [ "$(getent group "$GROUP")" ]; then
        usermod --append --groups "$GROUP" "$USERNAME"
        echo "Added user '$USERNAME' to group '$GROUP'"
    fi
done

# Enable passwordless sudo
echo "$USERNAME ALL=(ALL) NOPASSWD: ALL" >"/etc/sudoers.d/$USERNAME"
chmod 0440 "/etc/sudoers.d/$USERNAME"
echo "Enabled passwordless sudo for user '$USERNAME'"

# Install runner if needed
if [ ! -f "$GITHUB_RUNNER_HOME/run.sh" ]; then
    mkdir -p "$GITHUB_RUNNER_HOME"
    curl -fsSL --retry 3 --header "Authorization: Bearer $DOWNLOAD_TOKEN" "$DOWNLOAD_URL" | tar xz -C "$GITHUB_RUNNER_HOME"
    echo "Installed runner to directory '$GITHUB_RUNNER_HOME'"

    # Retry needed for apt-get flakiness
    for _ in $(seq 1 3); do
        "$GITHUB_RUNNER_HOME/bin/installdependencies.sh" && break
    done
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
