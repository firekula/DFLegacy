/* peek-ptrace.c —— 经**用户明确批准**的只读内存快照（attach → PEEKDATA → 立即 detach）。
 *
 * 为什么必须是 ptrace：本 VM 是 2.6.18，`/proc/<pid>/mem` 走 `PTRACE_MODE_ATTACH` 检查，
 * root 也被拒（实测 `pread` → `errno=3 ESRCH`，见 work/out/vm-peek-report-3567.txt），
 * 而且这台 VM **没有 gdb/gcore**（`command -v gdb` = not found）。
 *
 * 只读纪律（本文件里的每一行都遵守）：
 *   * 仅 `PTRACE_ATTACH` / `PTRACE_PEEKDATA` / `PTRACE_DETACH`；
 *   * **没有** PTRACE_POKEDATA / SETREGS / CONT / 断点 / 信号注入；
 *   * 读完立刻 DETACH，进程继续（暂停窗口 = 若干次 PEEKDATA 的时间）；
 *   * 目标若是 32 位进程，`PTRACE_PEEKDATA` 返回 4 字节有效数据（高位为零），
 *     本工具统一只取低 4 字节。
 *
 * 用法: peek-ptrace <pid> <outfile>
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <stdint.h>
#include <sys/ptrace.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>

static pid_t pid;
static FILE *rep;
static long last_errno = 0;

#define P(...) fprintf(rep, __VA_ARGS__)

/* 4 字节读（低 32 位；兼容 32 位目标） */
static int rd32(unsigned long va, uint32_t *out)
{
    long v = ptrace(PTRACE_PEEKDATA, pid, (void *)va, 0);
    if (v == -1 && errno) { last_errno = errno; return 0; }
    *out = (uint32_t)((unsigned long)v & 0xffffffffUL);
    return 1;
}

static int rd(unsigned long va, unsigned char *buf, size_t n)
{
    size_t i;
    for (i = 0; i + 4 <= n; i += 4) {
        uint32_t w;
        if (!rd32(va + i, &w)) return 0;
        buf[i]   = (unsigned char)(w & 0xff);
        buf[i+1] = (unsigned char)((w >> 8) & 0xff);
        buf[i+2] = (unsigned char)((w >> 16) & 0xff);
        buf[i+3] = (unsigned char)((w >> 24) & 0xff);
    }
    if (i < n) {
        uint32_t w;
        if (!rd32(va + i, &w)) return 0;
        for (; i < n; i++) { buf[i] = (unsigned char)(w & 0xff); w >>= 8; }
    }
    return 1;
}

static int rdstr(unsigned long va, char *buf, size_t cap)
{
    size_t i;
    for (i = 0; i + 1 < cap; i++) {
        if (!rd(va + i, (unsigned char *)(buf + i), 1)) { buf[i] = 0; return 0; }
        if (buf[i] == 0) return 1;
    }
    buf[cap - 1] = 0;
    return 1;
}

/* ---- sha256（自包含；GCC 4.1 上 unsigned long long 的格式化用两次 %lx）---- */
typedef struct { uint32_t h[8]; unsigned long long len; unsigned char buf[64]; size_t n; } sha256_t;
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
    s->len += (unsigned long long)l * 8;
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
    unsigned char pad[72]; size_t pl; int i; unsigned long long bits = s->len;
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
static void hex8(const unsigned char *dg)
{
    P("%02x%02x%02x%02x%02x%02x%02x%02x",
      dg[0],dg[1],dg[2],dg[3],dg[4],dg[5],dg[6],dg[7]);
}

static void probe_cipher(void)
{
    uint32_t obj = 0, vb = 0, ve = 0, first = 0, nk = 0;
    unsigned char buf[2048];
    sha256_t sh; unsigned char dg[32];
    unsigned int j;

    P("\n## A. G_Cipher 运行实例\n");
    P("CIPHER_SLOT_VA=0941f8a4\n");
    if (!rd32(0x0941f8a4UL, &obj)) { P("CIPHER_INSTANCE=UNREADABLE errno=%ld\n", last_errno); return; }
    P("CIPHER_INSTANCE_PTR=%08x\n", obj);
    if (!obj) { P("CIPHER_STATE=null\n"); return; }
    P("CIPHER_STATE=nonnull\n");
    if (!rd32(obj + 4, &vb) || !rd32(obj + 8, &ve)) { P("CIPHER_VEC=UNREADABLE\n"); return; }
    P("CIPHER_VEC_BEGIN=%08x\nCIPHER_VEC_END=%08x\n", vb, ve);
    if (vb && ve >= vb) {
        P("CIPHER_VEC_COUNT=%u\n", (unsigned)((ve - vb) / 4));
        for (j = 0; j < (ve - vb) / 4 && j < 16; j++) {
            uint32_t ent = 0;
            if (rd32(vb + j * 4, &ent)) {
                P("CIPHER_ALGO[%u]=%08x\n", j, ent);
                if (j == 0) first = ent;
            }
        }
        if (first) {
            uint32_t vt = 0;
            if (rd32(first, &vt)) P("CIPHER_ALGO0_VTABLE=%08x\n", vt);
        }
    }
    if (rd32(0x0941f864UL, &nk)) P("NEEDED_KEY_LENGTH=%u\n", nk);

    P("\n## A2. key 结构体数组 0x940be80 前 16 dword\n");
    for (j = 0; j < 16; j++) {
        uint32_t w = 0;
        if (!rd32(0x0940be80UL + j * 4, &w)) { P("KEYSTRUCT[%u]=UNREADABLE errno=%ld\n", j, last_errno); break; }
        P("KEYSTRUCT[%u]=%08x\n", j, w);
    }
    if (rd(0x0940be80UL, buf, sizeof(buf))) {
        sha256_init(&sh); sha256_update(&sh, buf, sizeof(buf)); sha256_final(&sh, dg);
        P("KEYBUF_2048_SHA256="); hex8(dg); P("\n");
        {
            int i, nul = 0, digits = 0, firstnz = -1, lastnz = -1;
            for (i = 0; i < (int)sizeof(buf); i++) {
                if (buf[i] == 0) nul++;
                if (buf[i] >= '0' && buf[i] <= '9') digits++;
                if (buf[i] && firstnz < 0) firstnz = i;
                if (buf[i]) lastnz = i;
            }
            P("KEYBUF_2048_NUL=%d DIGITS=%d FIRST_NONZERO=%d LAST_NONZERO=%d\n",
              nul, digits, firstnz, lastnz);
        }
        /* 前 32 字节：这是"选中串"的开头，够判长度而不泄整key */
        P("KEYBUF_HEAD32=");
        for (j = 0; j < 32; j++) P("%02x", buf[j]);
        P("\n");
        /* 判"是否同一个串反复铺满"：取前 64 B 作为候选周期，看整段是否由它重复构成 */
        {
            int k, period = -1;
            for (k = 1; k <= 64 && (int)sizeof(buf) > k; k++) {
                int ok = 1, m;
                for (m = 0; m + k < (int)sizeof(buf); m++) {
                    if (buf[m + k] != buf[m]) { ok = 0; break; }
                }
                if (ok) { period = k; break; }
            }
            P("KEYBUF_PERIODIC_FIRST=%d\n", period);
        }
    } else {
        P("KEYBUF_2048=UNREADABLE errno=%ld\n", last_errno);
    }
}

static void probe_restrict(void)
{
    uint32_t obj = 0, size = 0, root = 0, node = 0, pol = 0, cat = 0, vb = 0, ve = 0;
    unsigned int i;

    P("\n## B. ServiceRestrictManager 运行实例\n");
    P("RESTRICT_SINGLETON_VA=094045d4\n");
    if (!rd32(0x094045d4UL, &obj)) { P("RESTRICT_OBJ=UNREADABLE errno=%ld\n", last_errno); return; }
    P("RESTRICT_GETCODE_first_dword=%08x\n", obj);   /* == vtable, initialize 里第一个字段是? */
    if (!rd32(0x094045d4UL + 8, &size) || !rd32(0x094045d4UL + 0x0c, &root)) {
        P("RESTRICT_MAP_FIELDS=UNREADABLE\n"); return;
    }
    P("RESTRICT_MAP_SIZE=%u\nRESTRICT_MAP_ROOT=%08x\n", size, root);
    if (root == 0x094045d4UL + 4) { P("RESTRICT_MAP_STATE=empty(header-only)\n"); return; }
    if (!rd32(root + 0x10, &node)) { P("RESTRICT_NODE=UNREADABLE\n"); return; }
    P("RESTRICT_ROOT_NODE=%08x\n", root);
    P("RESTRICT_FIRST_NODE=%08x\n", node);
    P("RESTRICT_NODE_KEY=%08x\n", node);
    P("RESTRICT_NODE_VALUE_PTR=%08x\n", node + 4);
    if (!rd32(node + 4, &pol)) { P("RESTRICT_POLICY=UNREADABLE\n"); return; }
    P("RESTRICT_POLICY(%u号策略对象)=%08x\n", 0u, pol);
    if (!pol) { P("RESTRICT_POLICY_STATE=null\n"); return; }
    if (!rd32(pol + 8, &cat)) cat = 0xffffffff;
    if (!rd32(pol + 0x0c, &vb) || !rd32(pol + 0x10, &ve)) {
        P("POLICY_VEC=UNREADABLE\n"); return;
    }
    P("POLICY_GETCODE=%u\n", cat);
    P("POLICY_VEC_BEGIN=%08x\nPOLICY_VEC_END=%08x\n", vb, ve);
    if (ve >= vb) {
        unsigned int cnt = (unsigned)((ve - vb) / 2);
        P("POLICY_VEC_COUNT=%u\n", cnt);
        if (cnt && cnt <= 40) {
            for (i = 0; i < cnt; i++) {
                uint32_t w = 0;
                if (!rd32(vb + i * 2, &w)) break;
                P("POLICY_VEC[%u]=%u\n", i, (unsigned)(w & 0xffff));
            }
        }
    }
    /* 名字表（.data 里的静态指针数组，运行期也读一遍以自证） */
    P("\n## B2. MobileRestrict::enumStringMobiles 运行期读\n");
    for (i = 0; i <= 34; i++) {
        uint32_t p = 0; char nm[64];
        if (!rd32(0x093700c0UL + i * 4, &p)) { P("NAMETBL[%u]=UNREADABLE\n", i); break; }
        if (rdstr(p, nm, sizeof(nm))) P("NAMETBL[%u]=%s\n", i, nm);
        else P("NAMETBL[%u]=<unreadable str @%08x>\n", i, p);
    }
}

int main(int argc, char **argv)
{
    int status;
    if (argc < 3) { fprintf(stderr, "usage: peek-ptrace <pid> <outfile>\n"); return 2; }
    pid = (pid_t)atoi(argv[1]);
    rep = fopen(argv[2], "w");
    if (!rep) { perror("fopen"); return 4; }

    P("PID=%d UID=%d EUID=%d\n", (int)pid, (int)getuid(), (int)geteuid());
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0) {
        P("PTRACE_ATTACH=FAIL errno=%ld (%s)\n", (long)errno, strerror(errno));
        fclose(rep); return 3;
    }
    P("PTRACE_ATTACH=OK\n");
    if (waitpid(pid, &status, 0) < 0) {
        P("WAITPID=FAIL errno=%ld\n", (long)errno);
        ptrace(PTRACE_DETACH, pid, 0, 0);
        fclose(rep); return 5;
    }
    P("STOPPED_STATUS=%d (signaled=%d sig=%d)\n", status, WIFSTOPPED(status),
      WIFSTOPPED(status) ? WSTOPSIG(status) : -1);

    probe_cipher();
    probe_restrict();

    if (ptrace(PTRACE_DETACH, pid, 0, 0) != 0)
        P("PTRACE_DETACH=FAIL errno=%ld\n", (long)errno);
    else
        P("PTRACE_DETACH=OK\n");
    P("READS_ONLY=yes (no POKEDATA/SETREGS/CONT/CONT)\n");
    fclose(rep);
    return 0;
}
