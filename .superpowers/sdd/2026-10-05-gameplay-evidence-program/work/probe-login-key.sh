#!/usr/bin/env bash
# 只读：判定 rsa.php 里的私钥是不是游戏服 publickey.pem 的配对私钥。
# 上一版失败原因：PEM 在 PHP 源码里带缩进空格，openssl 报 "no start line"。
# 这一版先把每行的空白剥掉。
set -u

GAME_PUB=/home/neople/game/publickey.pem
mkdir -p /tmp/dfl

echo "===== 1. 抠 PEM 并剥掉缩进与 PHP 前缀 ====="
awk '/BEGIN RSA PRIVATE KEY/,/END RSA PRIVATE KEY/' /opt/lampp/htdocs/rsa.php \
  | sed 's/^[[:space:]]*//; s/[[:space:]]*$//' \
  | sed "s/^.*-----BEGIN RSA PRIVATE KEY-----/-----BEGIN RSA PRIVATE KEY-----/" \
  > /tmp/dfl/login_priv.pem
echo "  行数: $(wc -l < /tmp/dfl/login_priv.pem)"
echo "  首行: $(head -1 /tmp/dfl/login_priv.pem)"
echo "  末行: $(tail -1 /tmp/dfl/login_priv.pem)"

echo
echo "===== 2. 私钥模数 ====="
A=$(openssl rsa -in /tmp/dfl/login_priv.pem -noout -modulus 2>/dev/null | sed 's/^Modulus=//')
if [ -z "$A" ]; then
  echo "  仍读不出，原始报错："
  openssl rsa -in /tmp/dfl/login_priv.pem -noout -modulus 2>&1 | head -3
else
  echo "  ${A:0:64}..."
fi

echo
echo "===== 3. 游戏服公钥模数 ====="
B=$(openssl rsa -pubin -in "$GAME_PUB" -noout -modulus 2>/dev/null | sed 's/^Modulus=//')
echo "  ${B:0:64}..."

echo
echo "===== 4. 判定（关键分叉点）====="
if [ -n "$A" ] && [ "$A" = "$B" ]; then
  echo "  ===> 相同！rsa.php 的私钥就是游戏服的配对私钥 ⇒ 我们能自签 VM 会接受的令牌"
else
  echo "  ===> 不同（或私钥仍不可读）⇒ 不能用它签游戏面令牌"
fi

echo
echo "===== 5. 落一份公钥供后续核对 ====="
openssl rsa -in /tmp/dfl/login_priv.pem -pubout -out /tmp/dfl/login_pub.pem 2>/dev/null
echo "  /tmp/dfl/login_pub.pem: $(wc -c < /tmp/dfl/login_pub.pem) B"
