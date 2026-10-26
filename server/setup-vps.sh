#!/usr/bin/env bash
# Настройка VPS (Debian/Ubuntu) под приложение Camelot.
#
#   sudo ./setup-vps.sh fix   — у вас уже есть sing-box: перевести inbound с "0.0.0.0" на "::"
#                               (приём IPv4 и IPv6), включить MTU/MSS-фиксы для сотовых сетей.
#   sudo ./setup-vps.sh new   — чистая установка: sing-box 1.14.4, VLESS Reality (443) +
#                               Shadowsocks-2022 (8443), оба на "::", вывод ссылок для приложения.
#
# Переменные: VLESS_PORT, SS_PORT, REALITY_SNI, CLAMP_MSS=0 (отключить MSS-clamping),
#             PUBLIC_IPV6 / PUBLIC_IPV4 (если адреса не определились сами),
#             CONFIG_DIR, SING_BOX_BIN, DRY_RUN=1 (только сгенерировать конфиг и ссылки).
set -euo pipefail

MODE="${1:-}"
SING_BOX_VERSION="${SING_BOX_VERSION:-1.14.4}"
DRY_RUN="${DRY_RUN:-0}"
if [ "$DRY_RUN" = 1 ]; then
  CONFIG_DIR="${CONFIG_DIR:-$PWD/camelot-dry-run}"
else
  CONFIG_DIR="${CONFIG_DIR:-/etc/sing-box}"
fi
CONFIG="$CONFIG_DIR/config.json"
VLESS_PORT="${VLESS_PORT:-443}"
SS_PORT="${SS_PORT:-8443}"
REALITY_SNI="${REALITY_SNI:-www.microsoft.com}"
CLAMP_MSS="${CLAMP_MSS:-1}"
SB="${SING_BOX_BIN:-sing-box}"

log()  { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!!\033[0m  %s\n' "$*" >&2; }
die()  { printf '\033[1;31mОшибка:\033[0m %s\n' "$*" >&2; exit 1; }
run()  { if [ "$DRY_RUN" = 1 ]; then echo "[dry-run] $*"; else "$@"; fi; }

usage() {
  sed -n "2,11p" "$0" | sed 's/^# \{0,1\}//'
  exit 1
}

need_root() {
  [ "$DRY_RUN" = 1 ] || [ "$(id -u)" = 0 ] || die "запустите через sudo"
  command -v python3 >/dev/null || die "нужен python3 (apt install -y python3)"
}

# ---------------------------------------------------------------- адреса VPS

detect_ips() {
  IPV6="${PUBLIC_IPV6:-$(ip -6 addr show scope global 2>/dev/null \
    | awk '/inet6/ && !/temporary|deprecated/ {print $2}' | cut -d/ -f1 \
    | grep -viE '^(fc|fd|fe80)' | head -n1 || true)}"
  IPV4="${PUBLIC_IPV4:-$(curl -4 -fsS --max-time 5 https://api.ipify.org 2>/dev/null \
    || ip -4 addr show scope global 2>/dev/null | awk '/inet / {print $2}' | cut -d/ -f1 | head -n1 || true)}"
  if [ -n "$IPV6" ]; then
    log "IPv6 VPS: $IPV6"
  else
    warn "У VPS нет глобального IPv6. Включите IPv6 в панели хостера — для МТС это ключевое."
  fi
  if [ -n "$IPV4" ]; then
    log "IPv4 VPS: $IPV4"
  fi
  return 0
}

# ---------------------------------------------------------------- сеть: MTU / MSS

setup_sysctl() {
  log "sysctl: TCP MTU probing, dual-stack сокеты, BBR"
  local file=/etc/sysctl.d/99-camelot.conf
  local body
  body='# Camelot: устойчивость к «чёрным дырам» MTU в сотовых сетях
net.ipv4.tcp_mtu_probing = 1
# Сокет на "::" принимает и IPv4, и IPv6
net.ipv6.bindv6only = 0
net.core.default_qdisc = fq
net.ipv4.tcp_congestion_control = bbr'
  if [ "$DRY_RUN" = 1 ]; then
    echo "[dry-run] записать $file"
  else
    printf '%s\n' "$body" > "$file"
    sysctl --system >/dev/null 2>&1 || warn "часть параметров sysctl не применилась (старое ядро?)"
  fi
}

# Подменяем MSS в TCP SYN на портах прокси: сегменты укладываются в пакет 1280 байт,
# поэтому крупные пакеты не теряются, даже если сеть оператора режет ICMP «Packet Too Big».
setup_mss() {
  [ "$CLAMP_MSS" = 1 ] || { log "MSS-clamping пропущен (CLAMP_MSS=0)"; return; }
  local ports="$*"
  [ -n "$ports" ] || return
  if ! command -v iptables >/dev/null || ! command -v ip6tables >/dev/null; then
    run apt-get install -y iptables >/dev/null 2>&1 || { warn "iptables не установлен — MSS-clamping пропущен"; return; }
  fi
  log "MSS-clamping для TCP-портов: $ports (IPv6: 1220, IPv4: 1240)"
  local script=/usr/local/sbin/camelot-mss.sh
  local unit=/etc/systemd/system/camelot-mss.service
  if [ "$DRY_RUN" = 1 ]; then
    echo "[dry-run] записать $script и $unit"
    return
  fi
  cat > "$script" <<EOF
#!/bin/sh
# Создано setup-vps.sh (Camelot). Порты: $ports
for port in $ports; do
  for tool in iptables ip6tables; do
    mss=1240; [ "\$tool" = ip6tables ] && mss=1220
    for rule in "PREROUTING -p tcp --dport \$port" "OUTPUT -p tcp --sport \$port"; do
      \$tool -t mangle -C \$rule --tcp-flags SYN,RST SYN -j TCPMSS --set-mss \$mss 2>/dev/null \\
        || \$tool -t mangle -A \$rule --tcp-flags SYN,RST SYN -j TCPMSS --set-mss \$mss
    done
  done
done
EOF
  chmod 755 "$script"
  cat > "$unit" <<EOF
[Unit]
Description=Camelot: TCP MSS clamping for proxy ports
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=$script
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
EOF
  systemctl daemon-reload
  systemctl enable --now camelot-mss.service >/dev/null
}

open_firewall() {
  if command -v ufw >/dev/null && ufw status 2>/dev/null | grep -q "Status: active"; then
    for p in "$@"; do run ufw allow "$p" >/dev/null; done
    log "ufw: открыты порты $* (IPv4 и IPv6)"
  fi
  if command -v ip6tables >/dev/null && ip6tables -S INPUT 2>/dev/null | grep -q -- "-P INPUT DROP"; then
    warn "ip6tables INPUT по умолчанию DROP — убедитесь, что порты прокси открыты и для IPv6"
  fi
}

restart_and_verify() {
  local ports="$*"
  if [ "$DRY_RUN" = 1 ]; then
    echo "[dry-run] systemctl restart sing-box"
    return
  fi
  systemctl enable sing-box >/dev/null 2>&1 || true
  systemctl restart sing-box
  sleep 2
  systemctl is-active --quiet sing-box || { journalctl -u sing-box -n 30 --no-pager; die "sing-box не запустился"; }
  log "Слушающие сокеты (нужно [::]:порт или *:порт — это IPv4+IPv6; 0.0.0.0:порт — только IPv4):"
  for p in $ports; do
    ss -lntu 2>/dev/null | awk -v p=":$p" '$5 ~ p"$" {print "    " $1 "  " $5}'
  done
}

# ---------------------------------------------------------------- fix: "0.0.0.0" → "::"

mode_fix() {
  need_root
  [ -f "$CONFIG" ] || die "не найден $CONFIG (укажите CONFIG_DIR=...)"
  command -v "$SB" >/dev/null || die "не найден sing-box"
  log "Исправляю listen в $CONFIG"
  local backup="$CONFIG.bak.$(date +%Y%m%d-%H%M%S)"
  local result
  result="$(python3 - "$CONFIG" "$backup" "$DRY_RUN" <<'PY'
import json, shutil, sys
path, backup, dry = sys.argv[1], sys.argv[2], sys.argv[3] == "1"
try:
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
except json.JSONDecodeError as e:
    sys.exit(f"Не удалось прочитать JSON ({e}). Если в конфиге есть комментарии — уберите их или поправьте listen вручную.")
ports, changed, local = [], [], []
for ib in cfg.get("inbounds", []):
    if "listen_port" not in ib:
        continue
    tag = ib.get("tag") or ib.get("type")
    listen = ib.get("listen")
    if listen == "0.0.0.0":
        ib["listen"] = "::"
        changed.append(f"{tag}:{ib['listen_port']}")
    elif listen in (None, "", "127.0.0.1", "::1", "localhost"):
        local.append(f"{tag}:{ib['listen_port']}")
        continue
    if ib.get("type") != "tun":
        ports.append(str(ib["listen_port"]))
if changed and not dry:
    shutil.copy2(path, backup)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2, ensure_ascii=False)
        f.write("\n")
print("CHANGED=" + ",".join(changed))
print("LOCAL=" + ",".join(local))
print("PORTS=" + " ".join(sorted(set(ports), key=int)))
PY
)" || die "не удалось обработать $CONFIG"
  local changed local_only ports
  changed="$(printf '%s\n' "$result" | sed -n 's/^CHANGED=//p')"
  local_only="$(printf '%s\n' "$result" | sed -n 's/^LOCAL=//p')"
  ports="$(printf '%s\n' "$result" | sed -n 's/^PORTS=//p')"
  if [ -n "$changed" ]; then
    if [ "$DRY_RUN" = 1 ]; then
      log "Будет исправлено listen \"0.0.0.0\" → \"::\": $changed"
    else
      log "listen \"0.0.0.0\" → \"::\": $changed (резервная копия: $backup)"
    fi
  else
    log "Inbound с \"0.0.0.0\" не найдено — менять нечего"
  fi
  [ -n "$local_only" ] && warn "Только для localhost (не трогаю): $local_only"
  [ "$DRY_RUN" = 1 ] || "$SB" check -c "$CONFIG" || die "sing-box check не прошёл — верните $backup"
  setup_sysctl
  setup_mss $ports
  open_firewall $ports
  restart_and_verify $ports
  detect_ips
  log "Готово. В приложении укажите IPv6-адрес VPS (или домен с AAAA-записью)."
}

# ---------------------------------------------------------------- new: чистая установка

install_sing_box() {
  if command -v "$SB" >/dev/null 2>&1 && "$SB" version 2>/dev/null | head -n1 | grep -q " $SING_BOX_VERSION\$"; then
    log "sing-box $SING_BOX_VERSION уже установлен"
    return
  fi
  log "Установка sing-box $SING_BOX_VERSION"
  run sh -c "curl -fsSL https://sing-box.app/install.sh | sh -s -- --version $SING_BOX_VERSION"
}

urlencode() {
  python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1], safe=""))' "$1"
}

host_for_link() {
  case "$1" in *:*) echo "[$1]" ;; *) echo "$1" ;; esac
}

mode_new() {
  need_root
  install_sing_box
  command -v "$SB" >/dev/null || die "не найден sing-box после установки"

  log "Генерация ключей"
  local uuid keypair private_key public_key short_id ss_key
  uuid="$("$SB" generate uuid)"
  keypair="$("$SB" generate reality-keypair)"
  private_key="$(printf '%s\n' "$keypair" | awk '/PrivateKey/ {print $2}')"
  public_key="$(printf '%s\n' "$keypair" | awk '/PublicKey/ {print $2}')"
  short_id="$("$SB" generate rand --hex 8)"
  ss_key="$("$SB" generate rand --base64 16)"
  [ -n "$private_key" ] && [ -n "$public_key" ] || die "не удалось сгенерировать ключи Reality"

  mkdir -p "$CONFIG_DIR"
  local tmp
  tmp="$(mktemp)"
  UUID="$uuid" PRIV="$private_key" SID="$short_id" SSKEY="$ss_key" SNI="$REALITY_SNI" \
  VLESS_PORT="$VLESS_PORT" SS_PORT="$SS_PORT" python3 - "$tmp" <<'PY'
import json, os, sys
e = os.environ
config = {
    "log": {"level": "warn", "timestamp": True},
    "inbounds": [
        {
            "type": "vless",
            "tag": "vless-reality",
            # "::" — один сокет принимает и IPv4, и IPv6 (с "0.0.0.0" sing-box слушает только IPv4)
            "listen": "::",
            "listen_port": int(e["VLESS_PORT"]),
            "users": [{"name": "camelot", "uuid": e["UUID"], "flow": "xtls-rprx-vision"}],
            "tls": {
                "enabled": True,
                "server_name": e["SNI"],
                "reality": {
                    "enabled": True,
                    "handshake": {"server": e["SNI"], "server_port": 443},
                    "private_key": e["PRIV"],
                    "short_id": [e["SID"]],
                },
            },
        },
        {
            "type": "shadowsocks",
            "tag": "ss-2022",
            "listen": "::",
            "listen_port": int(e["SS_PORT"]),
            "method": "2022-blake3-aes-128-gcm",
            "password": e["SSKEY"],
        },
    ],
    "outbounds": [{"type": "direct", "tag": "direct"}],
}
with open(sys.argv[1], "w", encoding="utf-8") as f:
    json.dump(config, f, indent=2)
    f.write("\n")
PY
  "$SB" check -c "$tmp" || die "sing-box check не прошёл"

  if [ -f "$CONFIG" ]; then
    local backup="$CONFIG.bak.$(date +%Y%m%d-%H%M%S)"
    cp "$CONFIG" "$backup"
    log "Старый конфиг сохранён: $backup"
  fi
  install -m 640 "$tmp" "$CONFIG"
  rm -f "$tmp"
  [ "$DRY_RUN" = 1 ] || chown root:sing-box "$CONFIG" 2>/dev/null || true
  log "Конфиг записан: $CONFIG"

  setup_sysctl
  setup_mss "$VLESS_PORT" "$SS_PORT"
  open_firewall "$VLESS_PORT" "$SS_PORT"
  restart_and_verify "$VLESS_PORT" "$SS_PORT"
  detect_ips

  if [ -z "$IPV6$IPV4" ]; then
    warn "Не удалось определить адрес VPS — в ссылках будет ВАШ_IP; задайте PUBLIC_IPV6=... и перезапустите"
    IPV4="ВАШ_IP"
  fi
  local links="$CONFIG_DIR/camelot-links.txt"
  local ss_pass sni_enc
  ss_pass="$(urlencode "2022-blake3-aes-128-gcm:$ss_key")"
  sni_enc="$(urlencode "$REALITY_SNI")"
  {
    for ip in $IPV6 $IPV4; do
      local tag host
      case "$ip" in *:*) tag="IPv6" ;; *) tag="IPv4" ;; esac
      host="$(host_for_link "$ip")"
      echo "vless://$uuid@$host:$VLESS_PORT?encryption=none&flow=xtls-rprx-vision&security=reality&sni=$sni_enc&fp=chrome&pbk=$public_key&sid=$short_id&type=tcp#Camelot-Reality-$tag"
      echo "ss://$ss_pass@$host:$SS_PORT#Camelot-SS-$tag"
    done
  } > "$links"
  chmod 600 "$links"

  echo
  log "Ссылки для приложения (скопируйте на iPhone и нажмите «+» → «Вставить ссылку из буфера»):"
  echo
  cat "$links"
  echo
  log "Они же сохранены в $links. Для МТС используйте ссылки с IPv6."
}

case "$MODE" in
  fix) mode_fix ;;
  new) mode_new ;;
  *) usage ;;
esac
