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
# The cross-post lane's timer runs a program on the share (J:\crosspost\crosspost.py), not this build.
cp "$here/systemd/jobhunt-crosspost.service" "$here/systemd/jobhunt-crosspost.timer" "$units/"
# The 05:15 canaries, the 05:00 restart and the after-boot line run programs on the share (J:\dispatch\canary.py and
# box_restart.py); the canaries run this build's `run --canary`.
cp "$here/systemd/jobhunt-canary.service" "$here/systemd/jobhunt-canary.timer" "$here/systemd/jobhunt-restart.service" \
  "$here/systemd/jobhunt-restart.timer" "$here/systemd/jobhunt-boot.service" "$units/"
# The 03:40 archive job runs this build's `archive` (ADR: Old folders are zipped, not kept).
cp "$here/systemd/jobhunt-archive.service" "$here/systemd/jobhunt-archive.timer" "$units/"
systemctl --user daemon-reload
systemctl --user enable --now jobhunt-6am.timer jobhunt-crosspost.timer jobhunt-canary.timer jobhunt-restart.timer jobhunt-archive.timer
systemctl --user enable jobhunt-boot.service
systemctl --user list-timers jobhunt-6am.timer jobhunt-crosspost.timer jobhunt-canary.timer jobhunt-restart.timer jobhunt-archive.timer --no-pager
