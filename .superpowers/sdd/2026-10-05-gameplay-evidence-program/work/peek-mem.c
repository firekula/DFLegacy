/*
 * peek-mem.c —— 通过 PTRACE_ATTACH 读另一个进程的指定 VA（Linux 2.6 需要先 attach）。
 *
 * ## 为什么需要它
 *
 * VM 是 CentOS 5.8 / 内核 2.6.18。这个内核上**直接 open("/proc/<pid>/mem") 会 ESRCH**
 * （实测 `dd` 读 0 字节），必须先 `PTRACE_ATTACH`。VM 上没有 gdb，但**有 gcc**。
 *
 * ## 安全性（这是对活靶子的写操作吗？）
 *
 * `PTRACE_ATTACH` 会让目标**短暂停止**（毫秒级），读完后 `PTRACE_DETACH` 立即恢复。
 * **不改任何内存、不改寄存器、不注入代码**。属于"只读但会短暂暂停目标"——
 * 按活靶纪律要在批次清单里显式列出。
 *
 * 用法: peek-mem <pid> <hex-va> <len> [<hex-va> <len> ...]
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <unistd.h>
#include <fcntl.h>
#include <sys/ptrace.h>
#include <sys/wait.h>
#include <sys/types.h>

static int read_at(int memfd, unsigned long va, unsigned char *buf, size_t len)
{
    if (lseek(memfd, (off_t)va, SEEK_SET) == (off_t)-1) return -1;
    ssize_t n = read(memfd, buf, len);
    if (n < 0) return -1;
    return (int)n;
}

int main(int argc, char **argv)
{
    if (argc < 4 || (argc % 2) != 0) {
        fprintf(stderr, "用法: %s <pid> <hex-va> <len> [<hex-va> <len> ...]\n", argv[0]);
        return 2;
    }
    pid_t pid = (pid_t)atoi(argv[1]);
    char mempath[64];
    snprintf(mempath, sizeof(mempath), "/proc/%d/mem", (int)pid);

    if (ptrace(PTRACE_ATTACH, pid, NULL, NULL) == -1) {
        fprintf(stderr, "PTRACE_ATTACH 失败: %s\n", strerror(errno));
        return 3;
    }
    int status = 0;
    waitpid(pid, &status, 0);          /* 等它停下来 */

    int memfd = open(mempath, O_RDONLY);
    int rc = 0;
    if (memfd < 0) {
        fprintf(stderr, "打开 %s 失败: %s\n", mempath, strerror(errno));
        rc = 4;
    } else {
        int i;
        for (i = 2; i + 1 < argc; i += 2) {
            unsigned long va = strtoul(argv[i], NULL, 16);
            size_t len = (size_t)strtoul(argv[i + 1], NULL, 10);
            unsigned char *buf = malloc(len);
            if (!buf) { rc = 5; break; }
            memset(buf, 0, len);
            int n = read_at(memfd, va, buf, len);
            printf("=== VA 0x%08lx  len=%zu  ", va, len);
            if (n < 0) {
                printf("读失败: %s\n", strerror(errno));
            } else {
                size_t k;
                printf("实读 %d 字节\n", n);
                for (k = 0; k < (size_t)n; k += 16) {
                    size_t j;
                    printf("  %08lx  ", va + (unsigned long)k);
                    for (j = 0; j < 16 && k + j < (size_t)n; j++)
                        printf("%02x ", buf[k + j]);
                    for (; j < 16; j++) printf("   ");
                    printf(" |");
                    for (j = 0; j < 16 && k + j < (size_t)n; j++) {
                        unsigned char c = buf[k + j];
                        printf("%c", (c >= 32 && c < 127) ? c : '.');
                    }
                    printf("|\n");
                }
            }
            free(buf);
        }
        close(memfd);
    }

    ptrace(PTRACE_DETACH, pid, NULL, NULL);
    return rc;
}
