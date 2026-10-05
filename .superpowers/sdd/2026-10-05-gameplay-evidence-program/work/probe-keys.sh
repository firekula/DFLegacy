#!/usr/bin/env bash
# 只读：找 VM 上的所有 PEM/密钥，判断能否自己签游戏面令牌。
set -u

echo "===== 1. 游戏服的 publickey.pem（可能就是容器那把的对应公钥）====="
for pem in /home/neople/game/publickey.pem /home/neople/*/publickey.pem /home/neople/*/*.pem; do
  [ -f "$pem" ] || continue
  echo "--- $pem ($(wc -c < "$pem") B) ---"
  head -2 "$pem"
  # 用 openssl 打印模数指纹（老 openssl 也支持 -modulus）
  openssl rsa -pubin -in "$pem" -modulus -noout 2>/dev/null | head -c 120
  echo
  echo
done

echo "===== 2. 全盘找 RSA/私钥（*.pem *.key id_rsa）====="
find /home /opt/lampp /root -maxdepth 4 \( -name '*.pem' -o -name '*.key' -o -name 'id_rsa*' \) \
  -type f 2>/dev/null | head -30

echo
echo "===== 3. 有没有私钥（PRIVATE KEY 头）====="
grep -rl 'PRIVATE KEY' /home /opt/lampp /root --include='*.pem' --include='*.key' 2>/dev/null | head -10
grep -rl 'PRIVATE KEY' /home/neople 2>/dev/null | head -10

echo
echo "===== 4. gate（5505）在哪、它的配置 ====="
find /home /opt/lampp -maxdepth 4 -iname '*gate*' 2>/dev/null | head -15
ls -la /home/neople/ 2>/dev/null | grep -iE 'gate|web|login'
