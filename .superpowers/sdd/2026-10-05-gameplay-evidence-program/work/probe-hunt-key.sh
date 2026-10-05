#!/usr/bin/env bash
# 只读：全盘找"游戏服公钥的配对私钥"，并把所有 PEM 的模数打印出来对比。
# 目标模数：BEF69F43FD66A924E68BBA04BE42037E9DE1BF5949C4BA1F868EA8F369BE5266...
set -u
TARGET=BEF69F43FD66A924E68BBA04BE42037E9DE1BF5949C4BA1F868EA8F369BE5266
mkdir -p /tmp/dfl

echo "===== 1. 全盘搜 PEM/KEY 文件（不限目录，排除 /proc /sys）====="
find / -xdev \( -name '*.pem' -o -name '*.key' -o -name '*.pvt' -o -name 'id_rsa*' \
   -o -name '*.der' -o -name '*private*' \) -type f 2>/dev/null \
  | grep -vE '^/(proc|sys|dev)/' | head -40

echo
echo "===== 2. 逐个识别，打印模数（找 TARGET）====="
find / -xdev \( -name '*.pem' -o -name '*.key' -o -name 'id_rsa*' \) -type f 2>/dev/null \
  | grep -vE '^/(proc|sys|dev)/' | while read -r f; do
    m=$(openssl rsa -in "$f" -noout -modulus 2>/dev/null | sed 's/^Modulus=//')
    [ -z "$m" ] && m=$(openssl rsa -pubin -in "$f" -noout -modulus 2>/dev/null | sed 's/^Modulus=//')
    if [ -n "$m" ]; then
      if [ "${m:0:64}" = "$TARGET" ]; then
        echo "  *** 命中 TARGET ***  $f"
      else
        echo "  $f  ->  ${m:0:24}..."
      fi
    fi
  done

echo
echo "===== 3. 游戏服公钥的完整形态（存一份供宿主比对）====="
openssl rsa -pubin -in /home/neople/game/publickey.pem -pubout 2>/dev/null > /tmp/dfl/game_pub.pem
echo "  /tmp/dfl/game_pub.pem: $(wc -c < /tmp/dfl/game_pub.pem) B"
echo "  模数全长: $(openssl rsa -pubin -in /home/neople/game/publickey.pem -noout -modulus 2>/dev/null | wc -c) 字符"

echo
echo "===== 4. HSPub.key 是什么（4864 B，非 PEM）====="
strings -n 8 /home/neople/game/HSPub.key 2>/dev/null | head -20
