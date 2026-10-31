/* peek5-ptrace.c —— 第五轮**只读**快照：**纯粹的区域转储**，不做任何指针追踪。
 *
 * 教训（留档）：上一轮（peek4）在 VM 上做中序树遍历，循环边界写错 ⇒ 探针空转，
 * 把 `df_game_r` 按在 T (tracing stop) 状态、报告 0 字节。已 kill 探针并确认进程恢复
 * （`State: S`, `TracerPid: 0`）。
 * ⇒ 新纪律：**VM 上只做 O(n) 的顺序转储，不做图遍历**；结构解析放到宿主侧（可反复重试、零风险）。
 *
 * 本工具：按 <va> <len> 顺序读，写 `<out>.hex`（每行 16 字节，`%08lx ` 前缀 + 32 hex）。
 * 读失败即停（并记 errno），绝不死循环。
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
static long last_errno = 0;

static int rd32(unsigned long va, uint32_t *o)
{
    long v = ptrace(PTRACE_PEEKDATA, pid, (void *)va, 0);
    if (v == -1 && errno) { last_errno = errno; return 0; }
    *o = (uint32_t)((unsigned long)v & 0xffffffffUL);
    return 1;
}

/* 返回实际可读的 4 字节字数。
 * ⚠️ 两个必须的稳健性措施（上一版就是死在这两点上，输出 0 字节）：
 *   ① `setvbuf(...,_IONBF,...)`：无缓冲。否则探针一旦被杀/超时，整段输出留在 stdio 缓冲里丢掉；
 *   ② 读失败**不等于放弃**：跳到下一个 4 KB 页继续（区间可能跨未映射空洞），只在连续失败过多次才停。 */
static unsigned long dump(const char *path, unsigned long va, unsigned long len)
{
    FILE *f = fopen(path, "ab");
    unsigned long off, words = 0, maxw = len / 4, fails = 0;
    if (!f) return 0;
    setvbuf(f, NULL, _IONBF, 0);
    fprintf(f, "REGION %08lx %lu\n", va, len);
    for (off = 0; off < maxw; off++) {
        uint32_t w;
        if (!rd32(va + off * 4, &w)) {
            fails++;
            if (fails <= 8)
                fprintf(f, "UNREADABLE_AT %08lx errno=%ld\n", va + off * 4, last_errno);
            if (fails > 64) { fprintf(f, "GIVE_UP_AFTER_FAILS=%lu\n", fails); break; }
            /* 跳到下一个页边界 */
            {
                unsigned long nxt = ((va + off * 4) & ~0xfffUL) + 0x1000UL;
                if (nxt <= va + off * 4) break;
                off = (nxt - va) / 4;
                if (off >= maxw) break;
            }
            continue;
        }
        fails = 0;
        fprintf(f, "%08lx %08x\n", va + off * 4, w);
        words++;
        if ((off & 0x3ff) == 0) fflush(f);
    }
    fprintf(f, "REGION_END %08lx words=%lu\n", va, words);
    fflush(f);
    fclose(f);
    return words;
}

int main(int argc, char **argv)
{
    int status;
    const char *out;
    if (argc < 3) { fprintf(stderr, "usage: peek5-ptrace <pid> <outfile>\n"); return 2; }
    pid = (pid_t)atoi(argv[1]);
    out = argv[2];
    {
        FILE *f = fopen(out, "w");
        if (!f) { perror("fopen"); return 4; }
        fclose(f);
    }
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0) {
        printf("ATTACH=FAIL errno=%ld\n", (long)errno);
        return 3;
    }
    if (waitpid(pid, &status, 0) < 0) {
        ptrace(PTRACE_DETACH, pid, 0, 0);
        return 5;
    }

    /* 1) 全局单例/守卫所在的 .bss 窗口（W1：manager 就在这一片） */
    dump(out, 0x094045a0UL, 0x100UL);
    /* 1b) 静态符号给出的其它候选（initialize/getProfile 之类引用的全局） */
    dump(out, 0x09404700UL, 0x100UL);
    /* 2) Cipher 实例头（含 vector） */
    dump(out, 0x11804f00UL, 0x1000UL);
    /* 3) manager 候选对象 + 紧邻的"策略对象"（W1 定案用） */
    dump(out, 0x1187e900UL, 0x900UL);
    /* 4) RB 树节点所在堆片 */
    dump(out, 0x11830000UL, 0x1000UL);
    /* 5) 名字表（静态指针数组）在 .data，运行期也读一遍 */
    dump(out, 0x093700a0UL, 0x100UL);

    if (ptrace(PTRACE_DETACH, pid, 0, 0) != 0) printf("DETACH=FAIL errno=%ld\n", (long)errno);
    else printf("PTRACE_DETACH=OK\n");
    printf("READS_ONLY=yes\n");
    return 0;
}
