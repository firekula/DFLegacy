# CMD1 token 字段离线复核

来源：目标 ELF SHA256 `486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043`；命令 `python work/verify-static-password.py`。

## [静态]

`GarenaAuthData` accessor：

| 字段 | 偏移/规则 |
|---|---|
| UID | offset 0，`ntohl(u32)` |
| uname | offset 4，返回 char* |
| timestamp | `ntohl(u32@0x24) + u32@0x64` |
| GCA type | `ntohs(u16@0x28)` |
| mobile auth | `byte@0x2c == 1` |
| patch time | `u32@0x64` |

`AuthGarenaToken::decrypt` 清零 0x68 B，然后复制 `recovered_vector.size()-1` B；本次 recovered vector size=46，因此实际复制长度=45。

## [回放]

两条既有 VM 游戏面会话均得到相同的匿名摘要：

```text
UID_BE=18000008
UNAME_32_SHA256=72cd6e8422c407fb6d098690f1130b7ded7ec2f7f5e1d30bd9d521f015363793
TIMESTAMP_BE_PLUS_PATCH=1435583760
GCA_BE=260
MOBILE_IS_ONE=True
AUTH_COPY_LEN=45
```

token 长度=344，base64 解码=256 B，RSA 公钥运算=PKCS#1 type-1，恢复 vector=46 B，复制到 auth data=45 B。Python 大整数运算与 Git OpenSSL 两种 `pkeyutl -verifyrecover` 路径逐字节相同；AES 重加密也恢复原 CMD1 密文。

## 扩展样本回放

[verify-static-password.txt](<verify-static-password.txt>) 已扫描 11 个可配对流、8 个唯一内容对；其中 VM/10013 样本使用 VM 公钥，容器/30011 样本使用容器公钥。所有可配对样本通过 AES 双实现、密文 CRC、OpenSSL raw/type1 recover、base64、token 长度与错误 key 负例。重复文件保留并标明，不当作独立部署证据。

## 限制

UID 是 token accessor 解出的值，不等于已由数据库或登录账号归因的事实；用户名只保存 SHA256，不输出明文。尚未与 G_Cipher singleton 逐字节交叉。