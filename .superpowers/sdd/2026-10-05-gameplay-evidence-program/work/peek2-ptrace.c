/* peek2-ptrace.c —— 第二轮**只读**快照（同一纪律：ATTACH/PEEKDATA/DETACH，无写、无 CONT）。
 *
 * 第一轮（peek-ptrace.c）拿到了三个决定性事实，但留下两处要补：
 *   ① `G_Cipher` 实例 = 0x11804fe8，vector 14 项 ✓，`needed_key_length = 334` ✓
 *      ⇒ 但 334 B **原始 key 本体**还没定位（它不在全局，而在某个 Cipher/结构体里）。
 *      本轮：把 Cipher 实例前 1 KB 全量 dump，并解引用实例内的指针找 key 缓冲。
 *   ② `ServiceRestrictManager` 单例的静态地址疑为 0x94045d4，但读出来首 dword = 0
 *      ⇒ 要么地址错（那是别的全局），要么对象在别处。本轮：dump 0x9404500..0x9404700 整片，
 *      并在其中**自动搜索**指针指向可读、且 +4/+8 形如 vector begin/end 的候选对象。
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
        buf[i]=(unsigned char)w; buf[i+1]=(unsigned char)(w>>8);
        buf[i+2]=(unsigned char)(w>>16); buf[i+3]=(unsigned char)(w>>24);
    }
    return 1;
}

static int rdstr(unsigned long va, char *buf, size_t cap)
{
    size_t i;
    for (i = 0; i + 1 < cap; i++) {
        if (!rd(va + i, (unsigned char *)(buf + i), 1)) { buf[i]=0; return 0; }
        if (!buf[i]) return 1;
    }
    buf[cap-1]=0; return 1;
}

static void dump_words(const char *tag, unsigned long va, int n)
{
    int i;
    P("\n## %s @%08lx (%d dwords)\n", tag, va, n);
    for (i = 0; i < n; i++) {
        uint32_t w;
        if (!rd32(va + i*4, &w)) { P("  [+%03x] UNREADABLE errno=%ld\n", i*4, last_errno); return; }
        P("  [+%03x] %08x\n", i*4, w);
    }
}

/* 把 256 B 当 4 个 64 B 行打印（ascii 只打可打印） */
static void dump_bytes(const char *tag, unsigned long va, int n)
{
    unsigned char b[512];
    int i, k;
    if (n > (int)sizeof(b)) n = (int)sizeof(b);
    if (!rd(va, b, (size_t)n)) { P("\n## %s @%08lx UNREADABLE errno=%ld\n", tag, va, last_errno); return; }
    P("\n## %s @%08lx (%d bytes)\n", tag, va, n);
    for (i = 0; i < n; i += 16) {
        P("  %08lx  ", va + i);
        for (k = 0; k < 16 && i + k < n; k++) P("%02x ", b[i+k]);
        for (; k < 16; k++) P("   ");
        P(" |");
        for (k = 0; k < 16 && i + k < n; k++)
            P("%c", (b[i+k] >= 32 && b[i+k] < 127) ? b[i+k] : '.');
        P("|\n");
    }
}

int main(int argc, char **argv)
{
    int status;
    uint32_t obj = 0, v;
    if (argc < 3) { fprintf(stderr, "usage: peek2-ptrace <pid> <outfile>\n"); return 2; }
    pid = (pid_t)atoi(argv[1]);
    rep = fopen(argv[2], "w");
    if (!rep) { perror("fopen"); return 4; }
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0) {
        P("PTRACE_ATTACH=FAIL errno=%ld\n", (long)errno); fclose(rep); return 3;
    }
    if (waitpid(pid, &status, 0) < 0) { P("WAITPID=FAIL\n"); ptrace(PTRACE_DETACH,pid,0,0); fclose(rep); return 5; }
    P("PID=%d ATTACHED sig=%d\n", (int)pid, WIFSTOPPED(status) ? WSTOPSIG(status) : -1);

    /* ---------- Cipher 实例 ---------- */
    if (!rd32(0x0941f8a4UL, &obj) || !obj) { P("CIPHER_SLOT=UNREADABLE/null\n"); goto out; }
    P("CIPHER_INSTANCE=%08x\n", obj);
    dump_bytes("Cipher 实例前 512 B", obj, 512);

    /* 实例内每个看起来像指针的 dword，试着解引用；形如 C 字符串就打印 */
    {
        int i, found = 0;
        for (i = 0; i < 128 && found < 12; i++) {
            uint32_t p = 0; char s[80];
            if (!rd32(obj + i*4, &p)) break;
            if (p < 0x00100000UL || p > 0x7fffffffUL) continue;
            if (rdstr(p, s, sizeof(s))) {
                int printable = 1, k;
                if (!s[0]) continue;
                for (k = 0; s[k]; k++) if (s[k] < 32 || s[k] > 126) { printable = 0; break; }
                if (printable && k >= 4) {
                    P("CIPHER_FIELD[+%03x]=PTR %08x -> \"%s\" (len=%d)\n", i*4, p, s, k);
                    found++;
                }
            }
        }
    }

    /* ---------- key 结构体表 0x940be80 ---------- */
    dump_words("key 结构体表 0x940be80", 0x0940be80UL, 40);
    {
        int i;
        for (i = 0; i < 10; i++) {
            uint32_t ptr = 0, len = 0;
            char s[400];
            if (!rd32(0x0940be80UL + i*16, &ptr)) break;
            if (!rd32(0x0940be80UL + i*16 + 4, &len)) break;
            if (!ptr || len == 0 || len > 300) continue;
            memset(s, 0, sizeof(s));
            if (rd(ptr, (unsigned char *)s, len)) {
                int k, printable = 1;
                for (k = 0; k < (int)len; k++) if (s[k] < 32 || s[k] > 126) { printable = 0; break; }
                P("KEYENTRY[%d] ptr=%08x len=%u printable=%d value=\"%.60s\"\n",
                  i, ptr, len, printable, s);
            }
        }
    }

    /* ---------- ServiceRestrictManager 片区 ---------- */
    dump_words("0x9404500 片区（找单例）", 0x09404500UL, 96);

    /* 自动搜索：在 0x09404500..0x09404700 里找 dword 指向的对象，其 +8/+0xc 形如 map 头 */
    {
        int i;
        P("\n## 自动搜索单例候选（0x09404500..0x09404700）\n");
        for (i = 0; i < 128; i++) {
            unsigned long slot = 0x09404500UL + i*4;
            uint32_t cand = 0, f8 = 0, fc = 0;
            if (!rd32(slot, &cand)) break;
            if (cand < 0x00100000UL || cand > 0x7fffffffUL) continue;
            if (!rd32(cand + 8, &f8) || !rd32(cand + 0x0c, &fc)) continue;
            /* std::map = { _Rb_tree<...> { _Rb_tree_impl { node_count, header } , comparer } }
               libstdc++ 布局：+0 _M_impl(_M_key_compare)(1B 空基类, 占 1B) + padding；
               +4 node_count? 视实现。这里只报"看起来像"的候选，不判真伪。 */
            P("CAND slot=%08lx obj=%08x  [+0]=%08x [+4]=%08x [+8]=%08x [+0xc]=%08x\n",
              slot, cand, 0u, 0u, f8, fc);
        }
    }

    /* ---------- 当前连接（谁在线） ---------- */
    {
        FILE *f;
        char line[256];
        P("\n## 10013/20013 established\n");
        f = fopen("/proc/net/tcp", "r");
        if (f) {
            while (fgets(line, sizeof(line), f)) {
                if (strstr(line, "2719") || strstr(line, "4E2D"))   /* 10013 / 20013 hex */
                    P("TCP %s", line);
            }
            fclose(f);
        }
    }

out:
    if (ptrace(PTRACE_DETACH, pid, 0, 0) != 0) P("PTRACE_DETACH=FAIL errno=%ld\n", (long)errno);
    else P("PTRACE_DETACH=OK\n");
    P("READS_ONLY=yes\n");
    fclose(rep);
    return 0;
}
