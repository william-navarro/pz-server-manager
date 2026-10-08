#!/bin/bash
# Instala/atualiza o painel. Uso: sudo PANEL_ALLOW_CIDR=192.168.1.0/24 bash pz-painel-instalar.sh <pacote.tgz> <deploy-dir>
set -euo pipefail
[[ $# -eq 2 ]] || { echo "uso: $0 <pacote.tgz> <deploy-dir>"; exit 2; }
PACOTE=$1
DEP=$2
PANEL_ALLOW_CIDR=${PANEL_ALLOW_CIDR:-192.168.1.0/24}
[[ "$PANEL_ALLOW_CIDR" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}/([0-9]|[12][0-9]|3[0-2])$ ]] || { echo "PANEL_ALLOW_CIDR inválido"; exit 2; }
IFS='./' read -r CIDR_A CIDR_B CIDR_C CIDR_D CIDR_PREFIX <<< "$PANEL_ALLOW_CIDR"
for OCTET in "$CIDR_A" "$CIDR_B" "$CIDR_C" "$CIDR_D"; do
  ((10#$OCTET <= 255)) || { echo "PANEL_ALLOW_CIDR inválido"; exit 2; }
done
export DEBIAN_FRONTEND=noninteractive

install -d -o root -g root -m 755 /etc/pzmanager
[[ -f /etc/pzmanager/server.env ]] || install -o root -g root -m 644 "$DEP/server.env" /etc/pzmanager/server.env
# shellcheck disable=SC1091
. /etc/pzmanager/server.env
: "${PZ_SERVER_NAME:=servertest}"
: "${PZ_ROOT:=/opt/pzserver}"
[[ "$PZ_SERVER_NAME" =~ ^[A-Za-z0-9_-]{1,32}$ ]] || { echo "PZ_SERVER_NAME inválido"; exit 2; }
[[ "$PZ_ROOT" == /* && "$PZ_ROOT" != / && "$PZ_ROOT" != *..* ]] || { echo "PZ_ROOT inválido"; exit 2; }

echo "== polkit"
dpkg -s polkitd >/dev/null 2>&1 || apt-get install -y -qq polkitd >/dev/null
echo "== usuário pzmanager"
id pzmanager >/dev/null 2>&1 || useradd --system --home-dir /var/lib/pzmanager --shell /usr/sbin/nologin pzmanager
usermod -aG systemd-journal,pzserver pzmanager
install -d -o pzmanager -g pzmanager -m 700 /var/lib/pzmanager

echo "== programa em /opt/pzmanager"
systemctl stop pzmanager 2>/dev/null || true
rm -rf -- /opt/pzmanager.novo
mkdir -p /opt/pzmanager.novo
tar -xzf "$PACOTE" -C /opt/pzmanager.novo
cp "$DEP/appsettings.Production.json" /opt/pzmanager.novo/
chown -R root:root /opt/pzmanager.novo
chmod -R go-w /opt/pzmanager.novo
chmod 755 /opt/pzmanager.novo/PzServerManager.Web
rm -rf -- /opt/pzmanager.antigo
[[ -d /opt/pzmanager ]] && mv /opt/pzmanager /opt/pzmanager.antigo
mv /opt/pzmanager.novo /opt/pzmanager

echo "== console do jogo"
if ! grep -q "^SocketGroup=pzserver" /etc/systemd/system/pzserver.socket; then
  sed -i 's/^SocketMode=0600/SocketGroup=pzserver\nSocketMode=0620/' /etc/systemd/system/pzserver.socket
  systemctl daemon-reload
  systemctl stop pzserver.service
  systemctl restart pzserver.socket
  systemctl start pzserver.service
fi

echo "== regras, scripts e serviços"
RULES_TMP=$(mktemp)
SERVICE_TMP=$(mktemp)
trap 'rm -f "$RULES_TMP" "$SERVICE_TMP"' EXIT
sed "s/__PZ_SERVER_NAME__/$PZ_SERVER_NAME/g" "$DEP/50-pzmanager.rules" > "$RULES_TMP"
sed "s|__PZ_ROOT__|$PZ_ROOT|g" "$DEP/pzmanager.service" > "$SERVICE_TMP"
install -m 644 "$RULES_TMP" /etc/polkit-1/rules.d/50-pzmanager.rules
install -m 644 "$SERVICE_TMP" /etc/systemd/system/pzmanager.service
install -m 755 "$DEP/servidor/pz-atualizar" /usr/local/sbin/pz-atualizar
install -m 755 "$DEP/servidor/pz-minusculas.py" /usr/local/sbin/pz-minusculas
install -d -o pzserver -g pzserver -m 750 "$PZ_ROOT/cardinal"
install -o pzserver -g pzserver -m 640 "$DEP/servidor/cardinal/CardinalBridge.lua" "$PZ_ROOT/cardinal/CardinalBridge.lua"
install -m 755 "$DEP/servidor/pz-cardinal-preparar" /usr/local/sbin/pz-cardinal-preparar
install -d -o pzserver -g pzserver -m 2750 "$PZ_ROOT/Zomboid/Lua/cardinal"
install -d -o pzserver -g pzserver -m 2770 "$PZ_ROOT/Zomboid/Lua/cardinal/acoes"
install -d -m 755 /etc/systemd/system/pzserver.service.d
install -m 644 "$DEP/pzserver-cardinal.conf" /etc/systemd/system/pzserver.service.d/cardinal.conf
install -m 644 "$DEP/pz-atualizar.service" /etc/systemd/system/pz-atualizar.service
install -m 644 "$DEP/pz-backup@.service" /etc/systemd/system/pz-backup@.service
systemctl daemon-reload
systemctl restart polkit.service
systemctl enable -q pzmanager.service

echo "== firewall: painel restrito à LAN configurada"
ufw allow from "$PANEL_ALLOW_CIDR" to any port 8085 proto tcp comment 'PZ Server Manager' >/dev/null
ufw status | grep 8085
echo "== fim"
