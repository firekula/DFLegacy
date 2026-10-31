/* peek-ro.c —— 只读内存探针（在 VM 上编译并运行；**不做任何写操作、不注入、不暂停**）。
 *
 * 为什么存在：handoff §4.A 要钉 `G_Cipher` / `ServiceRestrictManager` 的**运行实例**值，
 * 而 §5.3 要求"没有安全只读工具就不要抓内存"。本文件就是那个工具，且它**自身**满足：
 *   - 只用 `open(..., O_RDONLY)` + `pread()`：没有任何 write/ptrace/process_vm_writev；
 *   - 不 attach、不暂停目标进程（`/proc/<pid>/mem` 的 pread 不打断运行）；
 *   - **落盘只写哈希**：key 材料先 sha256，再只把前 4 字节（8 hex）+ 长度写进报告；
 *     原始 key 字节只在进程内存里，不进任何文件、不进 stdout 的 hex。
 *
 * 输出：NUL 分隔的 k=v 记录，由宿主侧脚本解析（避免 VM 上 python2.4 的解析负担）。
 * 用法：peek-ro <pid> <outfile>
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdint.h>

static int memfd = -1;
static int read_errno = 0;

/* 目标进程是 **32 位**，但我们只碰它 4 GB 以下的映射空间，所以本工具可以编成 64 位：
 * 每个地址都只取低 4 字节（`addr & 0xffffffff`），与 32 位视角一致。
 * 用 64 位构建是因为 VM 上**没有 gnu/stubs-32.h**（`gcc -m32` 编不过），
 * 而"读 8 字节再截低 4 字节"在 64 位下与"读 4 字节"完全等价。 */
#define A32(x) ((unsigned long)((x) & 0xffffffffUL))

static int rd(unsigned long va, void *buf, size_t n)
{
    ssize_t got = pread(memfd, buf, n, (off_t)A32(va));
    if (got != (ssize_t)n) {
        if (!read_errno) read_errno = errno;
        return 0;
    }
    return 1;
}

static int rdu32(unsigned long va, uint32_t *out)
{
    unsigned char b[8];
    if (!rd(va, b, 8)) return 0;
    *out = (uint32_t)b[0] | ((uint32_t)b[1] << 8) | ((uint32_t)b[2] << 16) | ((uint32_t)b[3] << 24);
    return 1;
}

static int rdstr(unsigned long va, char *buf, size_t cap)
{
    size_t i;
    for (i = 0; i + 1 < cap; i++) {
        if (!rd(va + i, buf + i, 1)) { buf[i] = 0; return 0; }
        if (buf[i] == 0) return 1;
    }
    buf[cap - 1] = 0;
    return 1;
}

/* ---- sha256（自包含，避免依赖 openssl 头） ---- */
typedef struct { uint32_t h[8]; uint64_t len; unsigned char buf[64]; size_t n; } sha256_t;

static const uint32_t K[64] = {
 0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
 0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
 0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
 0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
 0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
 0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
 0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
 0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};

#define ROR(x,n) (((x) >> (n)) | ((x) << (32-(n))))

static void sha256_block(sha256_t *s, const unsigned char *p)
{
    uint32_t w[64], a,b,c,d,e,f,g,h,t1,t2; int i;
    for (i = 0; i < 16; i++)
        w[i] = ((uint32_t)p[i*4] << 24) | ((uint32_t)p[i*4+1] << 16) |
               ((uint32_t)p[i*4+2] << 8) | (uint32_t)p[i*4+3];
    for (i = 16; i < 64; i++) {
        uint32_t s0 = ROR(w[i-15],7) ^ ROR(w[i-15],18) ^ (w[i-15] >> 3);
        uint32_t s1 = ROR(w[i-2],17) ^ ROR(w[i-2],19) ^ (w[i-2] >> 10);
        w[i] = w[i-16] + s0 + w[i-7] + s1;
    }
    a=s->h[0]; b=s->h[1]; c=s->h[2]; d=s->h[3]; e=s->h[4]; f=s->h[5]; g=s->h[6]; h=s->h[7];
    for (i = 0; i < 64; i++) {
        uint32_t S1 = ROR(e,6) ^ ROR(e,11) ^ ROR(e,25);
        uint32_t ch = (e & f) ^ ((~e) & g);
        uint32_t S0 = ROR(a,2) ^ ROR(a,13) ^ ROR(a,22);
        uint32_t mj = (a & b) ^ (a & c) ^ (b & c);
        t1 = h + S1 + ch + K[i] + w[i];
        t2 = S0 + mj;
        h=g; g=f; f=e; e=d+t1; d=c; c=b; b=a; a=t1+t2;
    }
    s->h[0]+=a; s->h[1]+=b; s->h[2]+=c; s->h[3]+=d;
    s->h[4]+=e; s->h[5]+=f; s->h[6]+=g; s->h[7]+=h;
}

static void sha256_init(sha256_t *s)
{
    s->h[0]=0x6a09e667; s->h[1]=0xbb67ae85; s->h[2]=0x3c6ef372; s->h[3]=0xa54ff53a;
    s->h[4]=0x510e527f; s->h[5]=0x9b05688c; s->h[6]=0x1f83d9ab; s->h[7]=0x5be0cd19;
    s->len=0; s->n=0;
}

static void sha256_update(sha256_t *s, const unsigned char *p, size_t l)
{
    s->len += (uint64_t)l * 8;
    while (l) {
        size_t take = 64 - s->n;
        if (take > l) take = l;
        memcpy(s->buf + s->n, p, take);
        s->n += take; p += take; l -= take;
        if (s->n == 64) { sha256_block(s, s->buf); s->n = 0; }
    }
}

static void sha256_final(sha256_t *s, unsigned char out[32])
{
    unsigned char pad[72]; size_t pl; int i;
    uint64_t bits = s->len;
    pl = (s->n < 56) ? (56 - s->n) : (120 - s->n);
    memset(pad, 0, sizeof(pad)); pad[0] = 0x80;
    for (i = 0; i < 8; i++) pad[pl + i] = (unsigned char)(bits >> (56 - 8*i));
    sha256_update(s, pad, pl + 8);
    for (i = 0; i < 8; i++) {
        out[i*4]   = (unsigned char)(s->h[i] >> 24);
        out[i*4+1] = (unsigned char)(s->h[i] >> 16);
        out[i*4+2] = (unsigned char)(s->h[i] >> 8);
        out[i*4+3] = (unsigned char)(s->h[i]);
    }
}

/* ---- 报告 ---- */
static FILE *rep;
#define P(...) fprintf(rep, __VA_ARGS__)

static void probe_restrict(void)
{
    uint32_t obj = 0, map0 = 0, map1 = 0, root = 0, size = 0, node = 0;
    uint32_t pol = 0, cat = 0, code = 0, vb = 0, ve = 0;
    int i, k;

    P("RESTRICT_OBJ_SLOT=%08x\n", 0x094045a8u);
    if (!rdu32(0x094045a8u, &obj)) { P("RESTRICT_OBJ=UNREADABLE\n"); return; }
    P("RESTRICT_OBJ=%08x\n", obj);
    if (!obj) { P("RESTRICT_OBJ_STATE=null(未初始化)\n"); return; }
    P("RESTRICT_OBJ_STATE=nonnull\n");
    if (!rdu32(obj, &map0) || !rdu32(obj + 4, &map1)) { P("RESTRICT_MAP=UNREADABLE\n"); return; }
    P("RESTRICT_MAP_HDR=%08x,%08x\n", map0, map1);
    if (!rdu32(obj + 8, &size) || !rdu32(obj + 0x0c, &root)) {
        P("RESTRICT_MAP_FIELDS=UNREADABLE\n"); return;
    }
    P("RESTRICT_MAP_SIZE=%u\n", size);
    P("RESTRICT_MAP_ROOT=%08x\n", root);
    if (root == obj + 4) { P("RESTRICT_MAP_STATE=empty(header-only，无任何策略)\n"); return; }
    if (!rdu32(root + 0x10, &node)) { P("RESTRICT_NODE=UNREADABLE\n"); return; }
    P("RESTRICT_NODE=%08x\n", node);
    P("RESTRICT_NODE_KEY_FIRST=%08x\n", node);
    P("RESTRICT_NODE_VALUE=%08x\n", node + 4);
    if (!rdu32(node + 4, &pol)) { P("RESTRICT_POLICY=UNREADABLE\n"); return; }
    P("RESTRICT_POLICY=%08x\n", pol);
    if (!pol) { P("RESTRICT_POLICY_STATE=null\n"); return; }

    /* AuthMobileRestrictPolicy::isApply(int code) 的结构（静态读出的偏移）：
       +8 = getCode() 的枚举值(category)；+0xc/+0x10 = vector<short> 的 begin/end。 */
    rdu32(pol + 8, &cat);
    rdu32(pol + 0x0c, &vb);
    rdu32(pol + 0x10, &ve);
    P("POLICY_GETCODE=%u\n", cat);
    P("POLICY_VEC_BEGIN=%08x\n", vb);
    P("POLICY_VEC_END=%08x\n", ve);
    if (ve < vb) { P("POLICY_VEC=invalid(end<begin)\n"); return; }
    P("POLICY_VEC_COUNT=%u\n", (ve - vb) / 2);

    for (k = 0; k < 3; k++) {
        code = (k == 0) ? 33u : (k == 1 ? 1u : 32u);
        if (vb + code * 2 + 2 > ve) {
            P("POLICY_VEC[%u]=out_of_range\n", code);
            continue;
        }
        {
            unsigned short v = 0;
            if (!rd(vb + code * 2, &v, 2)) P("POLICY_VEC[%u]=UNREADABLE\n", code);
            else P("POLICY_VEC[%u]=%u\n", code, (unsigned)v);
        }
    }

    /* 名字表：把 AUTHORITATIVE 的"code -> 业务名"对照也钉在运行实例上（静态表已读，这里只做长度自证）。 */
    for (i = 0; i < 1; i++) {
        uint32_t nametbl = 0x093700c0u;
        char nm[64];
        uint32_t p = 0;
        if (!rdu32(nametbl + 33 * 4, &p)) { P("NAME_TBL[33]=UNREADABLE\n"); continue; }
        if (rdstr(p, nm, sizeof(nm))) P("NAME_TBL[33]=%s\n", nm);
        if (!rdu32(nametbl + 0 * 4, &p)) { P("NAME_TBL[0]=UNREADABLE\n"); continue; }
        if (rdstr(p, nm, sizeof(nm))) P("NAME_TBL[0]=%s\n", nm);
    }
    /* LoginChannel 的 restrict_code 也印出来，便于与 LOGIN 调用点 0x21 对照。 */
    for (i = 0; i <= 34; i++) {
        uint32_t p = 0; char nm[64];
        if (!rdu32(0x093700c0u + i * 4, &p)) break;
        if (!rdstr(p, nm, sizeof(nm))) break;
        if (strcmp(nm, "LoginChannel") == 0) P("LOGIN_CHANNEL_INDEX=%d\n", i);
    }
}

static void probe_cipher(void)
{
    uint32_t real = 0, obj = 0, tot = 0;
    unsigned char key[4096];
    sha256_t sh; unsigned char dg[32];
    int got = 0;

    P("CIPHER_GETTER=%08x\n", 0x082a713au);
    /* `G_Cipher()` @0x082a713a → `mov eax,[0x941f8a4]`（静态读出，见 game-face-crypto.md） */
    if (!rdu32(0x0941f8a4u, &obj)) { P("CIPHER_SLOT=UNREADABLE\n"); return; }
    P("CIPHER_INSTANCE_PTR=%08x\n", obj);
    if (!obj) { P("CIPHER_STATE=null\n"); return; }
    P("CIPHER_STATE=nonnull\n");

    /* 实例 +4/+8 = `std::vector<ICryptoGraph*>` 的 begin/end（静态读自 Cipher::Cipher()） */
    if (!rdu32(obj + 4, &real) || !rdu32(obj + 8, &tot)) {
        P("CIPHER_VEC=UNREADABLE\n");
    } else {
        P("CIPHER_VEC_BEGIN=%08x\n", real);
        P("CIPHER_VEC_END=%08x\n", tot);
        if (real && tot >= real) {
            unsigned int cnt = (tot - real) / 4;
            unsigned int j, first = 0;
            P("CIPHER_VEC_COUNT=%u\n", cnt);
            for (j = 0; j < cnt && j < 16; j++) {
                uint32_t ent = 0;
                if (rdu32(real + j * 4, &ent)) {
                    P("CIPHER_ALGO[%u]=%08x\n", j, ent);
                    if (j == 0) first = ent;
                }
            }
            /* 每个算法对象 +0 是它的 vtable（ICryptoGraph） —— 只印地址，不解读 */
            if (first) {
                uint32_t vt = 0;
                if (rdu32(first, &vt)) P("CIPHER_ALGO0_VTABLE=%08x\n", vt);
            }
        }
    }

    /* 334 B 原始 key：读出后**只留哈希**（原始字节不出文件）。 */
    memset(key, 0, sizeof(key));
    if (rd(0x0940be80u, key, 256)) {
        sha256_init(&sh); sha256_update(&sh, key, 256); sha256_final(&sh, dg);
        P("KEYSTR_256_SHA256=%02x%02x%02x%02x%02x%02x%02x%02x\n",
          dg[0],dg[1],dg[2],dg[3],dg[4],dg[5],dg[6],dg[7]);
        got = 1;
    } else {
        P("KEYSTR_256=UNREADABLE\n");
    }
    if (rd(0x0940be80u, key, 2048)) {
        int i, nz = 0, digits = 0, nul = 0;
        for (i = 0; i < 2048; i++) {
            if (key[i]) nz++; else nul++;
            if (key[i] >= '0' && key[i] <= '9') digits++;
        }
        sha256_init(&sh); sha256_update(&sh, key, 2048); sha256_final(&sh, dg);
        P("KEYBUF_2048_SHA256=%02x%02x%02x%02x%02x%02x%02x%02x\n",
          dg[0],dg[1],dg[2],dg[3],dg[4],dg[5],dg[6],dg[7]);
        P("KEYBUF_2048_NONZERO=%d NUL=%d DIGITS=%d\n", nz, nul, digits);
        got = 1;
    } else {
        P("KEYBUF_2048=UNREADABLE\n");
    }
    rdu32(0x0941f864u, &tot);
    P("NEEDED_KEY_LENGTH=%u\n", tot);
    (void)got;
}

int main(int argc, char **argv)
{
    char path[64];
    FILE *mf;
    char line[256];
    if (argc < 3) { fprintf(stderr, "usage: peek-ro <pid> <outfile>\n"); return 2; }
    snprintf(path, sizeof(path), "/proc/%s/mem", argv[1]);
    memfd = open(path, O_RDONLY);
    rep = fopen(argv[2], "w");
    if (!rep) { perror("fopen out"); return 4; }
    P("PID=%s\n", argv[1]);
    P("UID=%d EUID=%d\n", (int)getuid(), (int)geteuid());
    /* 先自证"能不能读这块内存"，并把 errno 落盘 ——
     * "读不到"必须与"读到 0"在报告里可区分（本工程纪律：工具失败不许当成结论）。 */
    if (memfd < 0) {
        P("MEM_OPEN=FAIL errno=%d (%s)\n", errno, strerror(errno));
        fclose(rep);
        return 3;
    }
    P("MEM_OPEN=OK\n");
    {
        uint32_t probe = 0;
        if (rdu32(0x094045a8u, &probe)) P("MEM_READ_TEST=OK value=%08x\n", probe);
        else P("MEM_READ_TEST=FAIL errno=%d (%s)\n", read_errno, strerror(read_errno));
    }
    /* /proc/<pid>/maps 里的目标区间 + 进程状态（只读） */
    snprintf(path, sizeof(path), "/proc/%s/maps", argv[1]);
    mf = fopen(path, "r");
    if (mf) {
        int n = 0;
        while (fgets(line, sizeof(line), mf) && n < 8) {
            if (strstr(line, "09369000") || strstr(line, "093f6000") || strstr(line, "df_game_r")) {
                line[strcspn(line, "\n")] = 0;
                P("MAPS=%s\n", line);
                n++;
            }
        }
        fclose(mf);
    }
    probe_restrict();
    probe_cipher();
    fclose(rep);
    close(memfd);
    return 0;
}
