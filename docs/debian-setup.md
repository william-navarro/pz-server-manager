# Preparing a Debian 13 host

This guide prepares a generic host for PZ Server Manager. Review every command for your environment.

## Service account and directories

```bash
sudo useradd --system --create-home --home-dir /opt/pzserver --shell /usr/sbin/nologin pzserver
sudo install -d -o pzserver -g pzserver -m 750 /opt/pzserver
sudo install -d -o root -g root -m 755 /etc/pzmanager
sudo install -o root -g root -m 644 deploy/linux/server.env /etc/pzmanager/server.env
sudoedit /etc/pzmanager/server.env
```

`PZ_SERVER_NAME` must match `^[A-Za-z0-9_-]{1,32}$`. Keep `PZ_ROOT` on a local filesystem with enough space for the game, Workshop content and backups.

## SteamCMD and dedicated server

Install the 32-bit runtime dependency and SteamCMD:

```bash
sudo dpkg --add-architecture i386
sudo apt update
sudo apt install -y ca-certificates curl lib32gcc-s1 ufw polkitd
sudo -u pzserver mkdir -p /opt/pzserver/steamcmd /opt/pzserver/pz
sudo -u pzserver bash -c 'curl -fsSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz -C /opt/pzserver/steamcmd'
sudo -u pzserver /opt/pzserver/steamcmd/steamcmd.sh \
  +force_install_dir /opt/pzserver/pz +login anonymous \
  +app_update 380870 validate +quit
```

If `PZ_ROOT` differs, replace `/opt/pzserver` consistently.

Start the dedicated server once with `-servername <PZ_SERVER_NAME>` to create its data files, then stop it before running `pz-parte2.sh`.

## Firewall

Restrict SSH and the panel to your administrative network. The following uses the documentation network `192.168.1.0/24`:

```bash
ADMIN_CIDR=192.168.1.0/24
sudo ufw default deny incoming
sudo ufw allow from "$ADMIN_CIDR" to any port 22 proto tcp comment 'SSH administration'
sudo ufw allow from "$ADMIN_CIDR" to any port 8085 proto tcp comment 'PZ Server Manager'
sudo ufw allow 16261:16262/udp comment 'Project Zomboid'
sudo ufw enable
```

Project Zomboid may require additional UDP ports when the player count exceeds the default allocation. Do not expose the RCON port; bind it to `127.0.0.1`.

## Laptop hosts

For a laptop used as a server, consider disabling suspend on lid close while on AC power. Edit `/etc/systemd/logind.conf`, set `HandleLidSwitchExternalPower=ignore`, and restart `systemd-logind`. Confirm cooling, power-loss behavior and disk health before unattended operation.

Continue with the [quick start](../README.md#quick-start-debian-13).
