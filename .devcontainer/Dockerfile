FROM mcr.microsoft.com/vscode/devcontainers/dotnet:10.0

USER vscode
SHELL ["/bin/bash", "-c"]

RUN source /usr/local/share/nvm/nvm.sh \
    && nvm install --lts \
    && npm install -g smee-client markdownlint-cli markdownlint-rule-github-internal-links
