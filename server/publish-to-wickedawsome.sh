#!/usr/bin/env bash
# Publishes the machine lanes on this box and installs their systemd user units (ADR-119, the addendum on the
# third process). Run on WickedAwsome from a clone at the commit to deploy; no workflow ever does this.
#   bash server/publish-to-wickedawsome.sh
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$HOME/.local/share/jobhunt-server"
units="$HOME/.config/systemd/user"

mkdir -p "$target" "$units" "$HOME/.config/jobhunt-server"
dotnet publish "$here/JobHunt.Server/JobHunt.Server.csproj" -c Release -r linux-x64 --self-contained false -o "$target"
cp "$here/systemd/jobhunt-6am.service" "$here/systemd/jobhunt-6am.timer" "$units/"
systemctl --user daemon-reload
systemctl --user enable --now jobhunt-6am.timer
systemctl --user list-timers jobhunt-6am.timer --no-pager
