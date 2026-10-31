# Antibot Ten* 算法静态推进

## [静态]

`Cipher @0x1934c0` 的跳表选择号范围是0..0x13，共20个槽；实际分支代码覆盖choice 0..19，其中 `choice=0` 进入 `TenIdea`，choice=1..19 进入Ten*封装。`GenKey @0x193420` 为每次choice/seed生成32B：两次调用TenMd5，每次输入8B、输出16B。

已确认部分Ten wrapper实际调用的核心实现：

| wrapper | 核心实现 | wrapper参数 |
|---|---|---|
| TenRijnDael | CRijndael::RijnDael | data, flag, keylen, outlen |
| TenIdea | CIdea::Idea | 同上 |
| TenRc6 | CRc6::Rc6 | 同上 |
| TenBlowFish24 | CBlowFish::BlowFish24 | 同上 |
| TenTwofish16/24/32 | CTwofish::Twofish16/24/32 | 同上 |
| TenMars | CMars::Mars | 同上 |
| TenMd5 | md5_init/append/finish | 8B input → 16B output |

wrapper未调用“标准名库”别名；其底层实现均在同一 SO 内。

来源：[ten-algorithm-entrypoints.txt](<ten-algorithm-entrypoints.txt>)、[antibot-cipher-static.txt](<antibot-cipher-static.txt>)、[antibot-genkey-crc-static.txt](<antibot-genkey-crc-static.txt>)。

## [未验证]

- choice/seed的运行值与57个NOTI561帧之间的映射；
- 19个Ten wrapper的完整加密模式、padding与key interpretation；
- 从抓包外层头反推GenKey输入仍未成功；不能以输出长度或可打印率命名明文。

下一步仍可静态读取所有Ten wrapper的核心函数和 `Cipher` 的跳表/调用者，建立完全的算法表；若无运行时seed，抓包解密仍可能存在不可消除的多解。
