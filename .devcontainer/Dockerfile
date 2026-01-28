FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

# FIX: Remove the broken Yarn repository list that is causing the "not signed" error
# We also run apt-get update to ensure the package list is clean for subsequent features
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get update