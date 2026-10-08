#!/bin/bash
# Configura o INI e cria o serviço systemd. Uso: sudo bash pz-parte2.sh <senha-entrada> <senha-rcon>
set -euo pipefail
[[ $# -eq 2 ]] || { echo "uso: $0 <senha-entrada> <senha-rcon>"; exit 2; }
SCRIPT_DIR=$(cd -- "$(dirname -- "$0")" && pwd)
ENV_FILE=/etc/pzmanager/server.env
install -d -o root -g root -m 755 /etc/pzmanager
[[ -f "$ENV_FILE" ]] || install -o root -g root -m 644 "$SCRIPT_DIR/../server.env" "$ENV_FILE"
# shellcheck disable=SC1091
. "$ENV_FILE"
: "${PZ_SERVER_NAME:=servertest}"
: "${PZ_ROOT:=/opt/pzserver}"
[[ "$PZ_SERVER_NAME" =~ ^[A-Za-z0-9_-]{1,32}$ ]] || { echo "PZ_SERVER_NAME inválido"; exit 2; }
[[ "$PZ_ROOT" == /* && "$PZ_ROOT" != / && "$PZ_ROOT" != *..* ]] || { echo "PZ_ROOT inválido"; exit 2; }
install -m 755 "$SCRIPT_DIR/pz-minusculas.py" /usr/local/sbin/pz-minusculas
INI="$PZ_ROOT/Zomboid/Server/$PZ_SERVER_NAME.ini"
cp -n "$INI" "$INI.original"
set_ini() { sed -i "s|^$1=.*|$1=$2|" "$INI"; grep "^$1=" "$INI" | sed 's/Password=.*/Password=***/'; }
set_ini Open false
set_ini Password "$1"
set_ini Public false
set_ini PublicName "My PZ Server"
set_ini UPnP false
set_ini MaxPlayers 8
set_ini SaveWorldEveryMinutes 10
set_ini RCONPassword "$2"
set_ini ServerWelcomeMessage "Bem-vindo ao servidor! <LINE> Tab abre o chat."
chown pzserver:pzserver "$INI"*
chmod 600 "$INI"*

cat > /etc/systemd/system/pzserver.socket <<'UNIT'
[Unit]
Description=Console do servidor Project Zomboid
[Socket]
ListenFIFO=/run/pzserver/console
SocketUser=pzserver
SocketGroup=pzserver
SocketMode=0620
RemoveOnStop=true
UNIT

cat > /usr/local/sbin/pz-console <<'SCRIPT'
#!/bin/sh
[ -p /run/pzserver/console ] || { echo "servidor parado"; exit 1; }
echo "$*" > /run/pzserver/console
SCRIPT
chmod 755 /usr/local/sbin/pz-console

cat > /etc/systemd/system/pzserver.service <<UNIT
[Unit]
Description=Servidor dedicado Project Zomboid
After=network-online.target
Wants=network-online.target
Requires=pzserver.socket

[Service]
Type=simple
User=pzserver
Group=pzserver
Environment=HOME=$PZ_ROOT
WorkingDirectory=$PZ_ROOT/pz
ExecStartPre=/bin/sed -i "s/\"-Xmx[0-9]*[gGmM]\"/\"-Xmx5g\"/" $PZ_ROOT/pz/ProjectZomboid64.json
ExecStartPre=/usr/local/sbin/pz-minusculas
ExecStart=$PZ_ROOT/pz/start-server.sh -servername $PZ_SERVER_NAME
StandardInput=socket
StandardOutput=journal
StandardError=journal
ExecStop=/bin/sh -c 'echo save > /run/pzserver/console; sleep 10; echo quit > /run/pzserver/console; while kill -0 \$MAINPID 2>/dev/null; do sleep 1; done'
TimeoutStopSec=120
Restart=on-failure
RestartSec=30
LimitNOFILE=65535
NoNewPrivileges=yes
ProtectSystem=strict
ReadWritePaths=$PZ_ROOT
ProtectHome=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectKernelLogs=yes
ProtectControlGroups=yes
ProtectClock=yes
ProtectHostname=yes
ProtectProc=invisible
RestrictSUIDSGID=yes
RestrictNamespaces=yes
RestrictRealtime=yes
LockPersonality=yes
CapabilityBoundingSet=
AmbientCapabilities=
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX AF_NETLINK
UMask=0027

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable -q pzserver.service
systemctl start pzserver.service
echo "== iniciado; aguardando SERVER STARTED"
for i in $(seq 1 300); do
  journalctl -u pzserver --since "-6min" -o cat | grep -q "SERVER STARTED" && break
  systemctl is-active -q pzserver || { echo "serviço caiu"; break; }
  sleep 1
done
echo "== após ${i}s"
systemctl is-active pzserver
journalctl -u pzserver -o cat | grep -E "version=|SERVER STARTED|listening on port" | tail -3
ss -ulnp | grep -E "1626[12]"
