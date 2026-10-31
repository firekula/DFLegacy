#!/usr/bin/env bash
# 只读：这台 VM 有没有"发令牌"的组件（登录链的 gate / IPG / secsvr），
# 以及游戏服公钥有没有配套私钥。这决定我们能不能自己造一个能登录的令牌。
set -u

echo "===== 1. 全盘找 secsvr / gunnersvr / zergsvr / IPG（登录链与反外挂）====="
ls -la /home/neople/secsvr/ 2>/dev/null
find /home/neople/secsvr -maxdepth 3 -type f 2>/dev/null | head -20

echo
echo "===== 2. 找任何监听/包含 5505、9006、auth、token 的东西 ====="
grep -rliE '5505|9006|api/v1/auth|game-server-ip' /home/neople /opt/lampp/htdocs 2>/dev/null | head -20
echo "--- 9006 / 5505 有没有在听 ---"
netstat -lntp 2>/dev/null | grep -E ':(9006|5505|7001|7000)\b' || echo "(都没在听)"

echo
echo "===== 3. XAMPP 网站内容（登录接口可能是个 PHP）====="
ls -la /opt/lampp/htdocs/ 2>/dev/null | head -25
echo "--- 找 php 里的 token 生成 ---"
grep -rliE 'token|login|auth|publickey|openssl_sign' /opt/lampp/htdocs 2>/dev/null | head -15

echo
echo "===== 4. 游戏服公钥的配套私钥在不在（决定能否自签令牌）====="
echo "--- 全盘搜 PRIVATE KEY（限常见位置）---"
grep -rl 'PRIVATE KEY' /home /root /opt/lampp/etc 2>/dev/null | head -20

echo
echo "===== 5. HSPub.key / 其它密钥文件 ====="
for f in /home/neople/game/HSPub.key /home/neople/game/*.key; do
  [ -f "$f" ] && { echo "--- $f ($(wc -c < "$f") B) ---"; head -c 200 "$f" | strings | head -5; echo; }
done

echo
echo "===== 6. 历史上这台机的 7001/5505 是谁在听（看日志或配置）====="
grep -rhiE 'this_tcp_port|tcp_port *= *7001' /home/neople/*/cfg/*.cfg 2>/dev/null | sort -u | head -20
