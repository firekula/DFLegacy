/* peek7-ptrace.c —— 第七轮**只读**快照（W1 定案用；仍然只做有限次直接读，不遍历）。
 *
 * 现场事实（第六轮 + 区域转储解析得到）：
 *   * `0x11830018` 与 `0x11830058` 两处，`+0x20` 分别是 `0x08b82f28` / `0x08b82ee8`
 *     （都在策略 vtable 区），`+0x1c` 都 = `0x21` = **33** —— 与 `InterfaceServicePolicy`
 *     的 `vector<short>` 大小字段形状吻合。
 *   * `0x11830058` 被 `0x1187e9f0+0x14` 指到；后者 `+0x20` = `_ZTV29CreateCharacterRestrictPolicy`。
 * ⇒ 本轮把这两处的 ±0x60 全量读出来，宿主侧即可确定 begin/end 与 `vec[33]`。
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

static pid_t pid; static FILE *rep; static long last_errno = 0;
#define P(...) fprintf(rep, __VA_ARGS__)

static int rd32(unsigned long va, uint32_t *o)
{
    long v;
    if (va < 0x1000UL) { last_errno = EFAULT; return 0; }
    v = ptrace(PTRACE_PEEKDATA, pid, (void *)va, 0);
    if (v == -1 && errno) { last_errno = errno; return 0; }
    *o = (uint32_t)((unsigned long)v & 0xffffffffUL);
    return 1;
}

static void dump_range(unsigned long va, unsigned int nwords)
{
    unsigned int i;
    P("\n## RANGE %08lx %u words\n", va, nwords);
    for (i = 0; i < nwords; i++) {
        uint32_t w = 0;
        if (!rd32(va + i * 4, &w)) { P("UNREADABLE_AT %08lx errno=%ld\n", va + i * 4, last_errno); return; }
        P("%08lx %08x\n", va + i * 4, w);
    }
}

/* 把"候选策略对象"的 begin/end/count 解出来并解释 */
static void explain_policy(unsigned long pol, const char *tag)
{
    uint32_t vt = 0, f8 = 0, fb = 0, fe = 0, sz = 0, cap = 0;
    P("\n-- explain %s @%08lx\n", tag, pol);
    rd32(pol + 0x00, &vt);
    rd32(pol + 0x08, &f8);
    rd32(pol + 0x0c, &fb);
    rd32(pol + 0x10, &fe);
    rd32(pol + 0x1c, &sz);
    rd32(pol + 0x20, &vt);
    P("   [+0x00]=%08x [+0x08]=%08x [+0x0c]=%08x [+0x10]=%08x [+0x1c]=%08x [+0x20]=%08x\n",
      vt, f8, fb, fe, sz, vt);
    /* 若 [+0x1c] 是"元素个数"，按 short 试算 begin = ? 我们直接把 [+0xc]/[+0x10] 当 begin/end 试 */
    if (fb && fe > fb) {
        unsigned int cnt = (fe - fb) / 2, i;
        P("   试读: begin=[+0x0c]=%08x end=[+0x10]=%08x -> count=%u\n", fb, fe, cnt);
        if (cnt <= 48) {
            for (i = 0; i < cnt; i++) {
                uint32_t w = 0;
                if (!rd32(fb + i * 2, &w)) { P("   vec[%u]=UNREADABLE\n", i); break; }
                P("   vec[%u]=%u\n", i, (unsigned)(w & 0xffff));
            }
        }
    }
}

int main(int argc, char **argv)
{
    int status;
    if (argc < 3) { fprintf(stderr, "usage: peek7-ptrace <pid> <outfile>\n"); return 2; }
    pid = (pid_t)atoi(argv[1]);
    rep = fopen(argv[2], "w");
    if (!rep) { perror("fopen"); return 4; }
    setvbuf(rep, NULL, _IONBF, 0);
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0) { P("ATTACH=FAIL errno=%ld\n", (long)errno); fclose(rep); return 3; }
    if (waitpid(pid, &status, 0) < 0) { P("WAITPID=FAIL\n"); ptrace(PTRACE_DETACH,pid,0,0); fclose(rep); return 5; }
    P("PID=%d ATTACHED\n", (int)pid);

    dump_range(0x11830000UL, 0x60);
    dump_range(0x1187e900UL, 0x80);
    explain_policy(0x11830018UL, "obj@11830018");
    explain_policy(0x11830058UL, "obj@11830058");
    /* 两层解引用：policy 里的 vector begin 也可能在堆上，这里对每个 begin/end 再读一小段 */
    {
        uint32_t begins[2], i;
        begins[0] = 0; begins[1] = 0;
        rd32(0x11830018UL + 0xc, &begins[0]);
        rd32(0x11830058UL + 0xc, &begins[1]);
        for (i = 0; i < 2; i++) {
            if (begins[i] >= 0x1000UL) dump_range(begins[i], 24);
        }
    }
    if (ptrace(PTRACE_DETACH, pid, 0, 0) != 0) P("DETACH=FAIL errno=%ld\n", (long)errno);
    else P("PTRACE_DETACH=OK\n");
    P("READS_ONLY=yes\n");
    fclose(rep);
    return 0;
}
