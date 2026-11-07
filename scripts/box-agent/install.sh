#!/usr/bin/env bash
# Installs the box agent (ADR: The box reports itself): the script under ~/.local/bin, the unit under
# ~/.config/systemd/user, the site's address under ~/.config/theyard. It never writes the key: with the key file in
# place it enables the unit and posts once; without it, it installs both, leaves the unit off and says how to add it.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
config="$HOME/.config/theyard"
site="https://app-theyard-one-ss-zmnetj67bn5h2.azurewebsites.net"

mkdir -p "$HOME/.local/bin" "$HOME/.config/systemd/user" "$config"
chmod 700 "$config"
install -m 755 "$here/theyard_box_agent.py" "$HOME/.local/bin/theyard_box_agent.py"
install -m 644 "$here/theyard-box-agent.service" "$HOME/.config/systemd/user/theyard-box-agent.service"
printf '%s\n' "$site" > "$config/site"
systemctl --user daemon-reload

if [ -s "$config/admin-key" ] && [ "$(stat -c %a "$config/admin-key")" = "600" ]; then
  python3 "$HOME/.local/bin/theyard_box_agent.py" --once
  systemctl --user enable --now theyard-box-agent.service
  echo "theyard-box-agent: enabled and posting"
else
  echo "theyard-box-agent: installed, not enabled: the key file is missing or not mode 600."
  echo "Put the web app's Admin__Key in it with:"
  echo "  (umask 077; printf '%s' '<the key>' > $config/admin-key)"
  echo "then: systemctl --user enable --now theyard-box-agent.service"
fi
