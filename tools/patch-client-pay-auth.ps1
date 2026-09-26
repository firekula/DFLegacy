param(
    [string]$Path = 'G:\Game\DNF_ACT1\DFLegacy\src\DFLegacy.Server\wwwroot\client\client-auth.js',
    [string]$HtmlPath = 'G:\Game\DNF_ACT1\DFLegacy\src\DFLegacy.Server\wwwroot\client'
)

# 1) 把 client-auth.js 里的非 ASCII 字符转成 \uXXXX 转义（保持纯 ASCII 源文件）。
$text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
$builder = New-Object System.Text.StringBuilder
foreach ($ch in $text.ToCharArray()) {
    if ([int][char]$ch -gt 126) {
        [void]$builder.AppendFormat('\u{0:X4}', [int][char]$ch)
    } else {
        [void]$builder.Append($ch)
    }
}
[System.IO.File]::WriteAllText($Path, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("escaped: {0} (non-ascii now = {1})" -f (Split-Path $Path -Leaf), (([System.IO.File]::ReadAllText($Path) -replace '[^\x00-\x7F]', '').Length))

# 2) 给三个充值页插入脚本引用，并把裸 XHR 换成 PayAuth.post（按 gb2312 原编码读回写，
#    补丁串本身是纯 ASCII，避免把老页面的中文重新编码成乱码）。
$encoding = [System.Text.Encoding]::GetEncoding(936)

$pages = @(
    @{
        File = 'client_pay.htm'
        Api = '/api/client-pay/cera'
        Body = '{ account: account, amount: amount }'
        OkMsg = '"\u5145\u503c\u6210\u529f\uff0c\u8bf7\u70b9\u51fb\u70b9\u5238\u5237\u65b0\u6309\u94ae\u67e5\u770b\u6700\u65b0\u4f59\u989d\u3002"'
        BadMsg = '"\u5145\u503c\u5931\u8d25\uff1a\u9700\u8981\u7ba1\u7406\u5458\u767b\u5f55\uff08\u70b9\u5238\u5145\u503c\u4ec5\u7ba1\u7406\u5458\u53ef\u7528\uff09\u3002"'
        FormName = 'submitPayForm'
    },
    @{
        File = 'client_pay_bank.htm'
        Api = '/api/client-pay/cera'
        Body = '{ account: account, amount: amount }'
        OkMsg = '"\u5145\u503c\u6210\u529f\uff0c\u8bf7\u70b9\u51fb\u70b9\u5238\u5237\u65b0\u6309\u94ae\u67e5\u770b\u6700\u65b0\u4f59\u989d\u3002"'
        BadMsg = '"\u5145\u503c\u5931\u8d25\uff1a\u9700\u8981\u7ba1\u7406\u5458\u767b\u5f55\uff08\u70b9\u5238\u5145\u503c\u4ec5\u7ba1\u7406\u5458\u53ef\u7528\uff09\u3002"'
        FormName = 'submitPayForm'
    },
    @{
        File = 'client_diamond.htm'
        Api = '/api/client-pay/diamond'
        Body = '{ account: account, days: days }'
        OkMsg = '"\u5f00\u901a\u6210\u529f\uff0c\u8bf7\u91cd\u65b0\u767b\u5f55\u6e38\u620f\u67e5\u770b\u9ed1\u94bb\u72b6\u6001\u3002"'
        BadMsg = '"\u5f00\u901a\u5931\u8d25\uff1a\u9700\u8981\u7ba1\u7406\u5458\u767b\u5f55\uff08\u9ed1\u94bb\u5f00\u901a\u4ec5\u7ba1\u7406\u5458\u53ef\u7528\uff09\u3002"'
        FormName = 'submitDiamondForm'
    }
)

foreach ($page in $pages) {
    $full = Join-Path $HtmlPath $page.File
    $content = [System.IO.File]::ReadAllText($full, $encoding)

    # 脚本引用（插在第一个 <script 之前；页面里已有脚本时确保 PayAuth 先定义）
    if ($content -notmatch 'client-auth\.js') {
        $content = $content -replace '(<script type="text/javascript">)', '<script type="text/javascript" src="client-auth.js"></script>$1'
    }

    # 找到函数体里的裸 XHR 段并整体替换成 PayAuth.post
    $pattern = '(?s)\s*var request = createHttpRequest\(\);.*?request\.send\([^;]*\);\r?\n(\s*)return false;'
    $replacement = "`r`n`t`tPayAuth.post(`"$($page.Api)`", $($page.Body), function (ok, status) {`r`n" +
        "`t`t`tif (ok) { setPayMessage($($page.OkMsg), false); }`r`n" +
        "`t`t`telse if (status === 401 || status === 403) { setPayMessage($($page.BadMsg), true); }`r`n" +
        "`t`t`telse { setPayMessage(`"\u64cd\u4f5c\u5931\u8d25\uff08HTTP `" + status + `"\uff09\u3002`", true); }`r`n" +
        "`t`t});`r`n`t`treturn false;"

    $updated = [regex]::Replace($content, $pattern, $replacement)
    if ($updated -eq $content) {
        Write-Host ("!! {0}: no XHR block matched - nothing changed" -f $page.File)
        continue
    }

    [System.IO.File]::WriteAllText($full, $updated, $encoding)
    Write-Host ("patched: {0}" -f $page.File)
}
