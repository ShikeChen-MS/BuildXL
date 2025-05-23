// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <dirent.h>
#include <errno.h>
#include <limits.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <dlfcn.h>
#include <unistd.h>
#include <gnu/lib-names.h>
#include <sys/vfs.h>
#include <sys/types.h>
#include <sys/param.h>
#include <sys/mount.h>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <sys/fcntl.h>
#include <sys/xattr.h>

#include "bxl_observer.hpp"
#include "observer_utilities.hpp"
#include "PTraceSandbox.hpp"

#define ERROR_RETURN_VALUE -1

// Propagates errno when the result is -1.
static int get_errno_from_result(result_t<int> result) {
    return result.get() == -1 ? result.get_errno() : 0;
}

INTERPOSE(int, statx, int dirfd, const char * pathname, int flags, unsigned int mask, struct statx * statxbuf)({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname,
        /* src_fd */        dirfd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_statx(event, ERROR_RETURN_VALUE, dirfd, pathname, flags, mask, statxbuf);
})

INTERPOSE(int, scandir, const char * dirp,
                   struct dirent *** namelist,
                   int (*filter)(const struct dirent *),
                   int (*compar)(const struct dirent **, const struct dirent **))
({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      dirp);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_scandir(event, ERROR_RETURN_VALUE, dirp, namelist, filter, compar);
})

INTERPOSE(int, scandir64, const char * dirp,
                   struct dirent64 *** namelist,
                   int (*filter)(const struct dirent64  *),
                   int (*compar)(const dirent64 **, const dirent64 **))
({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      dirp);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_scandir64(event, ERROR_RETURN_VALUE, dirp, namelist, filter, compar);
})

INTERPOSE(int, scandirat, int dirfd, const char * dirp,
                   struct dirent *** namelist,
                   int (*filter)(const struct dirent *),
                   int (*compar)(const struct dirent **, const struct dirent **))
({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      dirp,
        /* src_fd */        dirfd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_scandirat(event, ERROR_RETURN_VALUE, dirfd, dirp, namelist, filter, compar);
})

INTERPOSE(int, scandirat64, int dirfd, const char * dirp,
                   struct dirent64 *** namelist,
                   int (*filter)(const struct dirent64  *),
                   int (*compar)(const dirent64 **, const dirent64 **))
({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      dirp,
        /* src_fd */        dirfd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_scandirat64(event, ERROR_RETURN_VALUE, dirfd, dirp, namelist, filter, compar);
})

INTERPOSE(struct dirent *, readdir, DIR *dirp)
({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        dirfd(dirp));
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_readdir(event, (struct dirent *)NULL, dirp);
})

INTERPOSE(struct dirent64 *, readdir64, DIR *dirp)
({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        dirfd(dirp));
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_readdir64(event, (struct dirent64 *)NULL, dirp);
})

INTERPOSE(int, readdir_r, DIR *dirp, struct dirent *entry, struct dirent **result)
({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        dirfd(dirp));
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_readdir_r(event, ERROR_RETURN_VALUE, dirp, entry, result);
})

INTERPOSE(int, readdir64_r, DIR *dirp, struct dirent64 *entry, struct dirent64 **result)
({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericRead,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        dirfd(dirp));
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_readdir64_r(event, ERROR_RETURN_VALUE, dirp, entry, result);
})

INTERPOSE(void, _exit, int status)({
    bxl->SendExitReport(getpid(), getppid());
    bxl->real__exit(status);
})

static void report_child_process(const char *syscall, BxlObserver *bxl, pid_t childPid, pid_t parentPid)
{
    auto event = buildxl::linux::SandboxEvent::CloneSandboxEvent(syscall, childPid, parentPid, bxl->GetProgramPath());
    bxl->CreateAndReportAccess(event, /* check_cache */ false);
}

static void HandleForkOrCloneReporting(const char *syscall, BxlObserver *bxl, pid_t forkOrCloneChildPidResult)
{
    // We report process creation for both parent and child cases. These generates two reports, but we actually need both to avoid some race conditions:
    // - Process creation is reported on the child to guarantee that we see the process creation arriving as a report line before
    //   any other access report coming from the child (the process creation reported from the parent may non deterministically arrive later than reports from the child).
    //   If reports from the child arrive before the process start report, we won't know which executable to assign those reports to, and for example, allow list entries 
    //   that operate on the exec name won't kick in.
    // - Process creation is reported on the parent to avoid the case where the parent process is terminated, the active process count on managed side reaches 0, and we haven't
    //   seen the child process creation report yet. In this case we'll send an EOM sentinel to the FIFO that we want to arrive *after* the process creation report, so we can actually be
    //   sure whether we can tear down the FIFO (if we reported on the child only, we could detect that the parent process is not alive anymore and send the sentinel only to get the process
    //   start report - reported by the child - after we decided that no more messages should arrive).
    if (forkOrCloneChildPidResult == 0)
    {
        // Clear the file descriptor table when we are in the child process
        // File descriptors are unique to a process, so this cache needs to be invalidated on the child
        bxl->reset_fd_table();
        report_child_process(syscall, bxl, getpid(), getppid());
    }
    else
    {
        report_child_process(syscall, bxl, forkOrCloneChildPidResult, getpid());
    }
}

static int ret_fd(int fd, BxlObserver *bxl) 
{
    // when returning a new file descriptor we remove it from our cache, 
    // because presumably the path has changed
    bxl->reset_fd_table_entry(fd);
    return fd;
}

INTERPOSE(pid_t, fork, void)({
    result_t<pid_t> childPid = bxl->fwd_fork();

    HandleForkOrCloneReporting(__func__, bxl, childPid.get());

    return childPid.restore();
})

INTERPOSE(pid_t, vfork, void)({
    // Observe that we explicitly call fork and not vfork.
    // The reason is that vfork is only designed to call exec* or _exit in the child and was made available for perf reasons. 
    // The stack of the parent is reused for the child in vfork, and therefore nothing else should happen beyond exec* or _exit,
    // including returning from the interpose callback.
    // On the other hand, vfork is almost obsolete at this point and has been removed from the POSIX.1-2008 already.
    // Modern Linux distributions should be able to call fork directly with none or minimal perf differences. 
    result_t<pid_t> childPid = bxl->fwd_fork();

    HandleForkOrCloneReporting(__func__, bxl, childPid.get());

    return childPid.restore();
})

INTERPOSE(int, clone, int (*fn)(void *), void *child_stack, int flags, void *arg, ... /* pid_t *ptid, void *newtls, pid_t *ctid */ )({
    va_list args;
    va_start(args, arg);
    pid_t *ptid = va_arg(args, pid_t*);
    void *newtls = va_arg(args, void*);
    pid_t *ctid = va_arg(args, pid_t*);
    va_end(args);

    result_t<int> result = bxl->fwd_clone(fn, child_stack, flags, arg, ptid, newtls, ctid);
    
    // We don't want to report any process creation if clone was asked to create a new thread (and not a new process)
    if (!(flags & CLONE_THREAD))
    {
        HandleForkOrCloneReporting(__func__, bxl, result.get());
    }

    return result.restore();
})

static int handle_exec_with_ptrace(const char *file, char *const argv[], char *const envp[], BxlObserver *bxl)
{
    // fdtable will not longer be valid because the process will be forked for ptrace
    bxl->reset_fd_table();
    
    // Before we enable the ptrace sandbox, make sure we disable the interposed sandbox
    // This shouldn't make a difference for real builds (we are enabling the ptrace sandbox because
    // we are about to run a statically linked process, and therefore libc is not there) but for tests
    // we may use the ptrace sandbox even for dynamically linked processes.
    envp = bxl->RemoveLDPreloadFromEnv(envp);

    PTraceSandbox ptraceSandbox(bxl);
    auto result = ptraceSandbox.ExecuteWithPTraceSandbox(file, argv, envp, bxl->getFamPath());

    // This would only be reported if the execve inside the ptrace sandbox failed.
    auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   "execve",
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      file,
        /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
    event.SetErrno(errno);
    BxlObserver::GetInstance()->CreateAndReportAccess(event);

    return result;
}

static int handle_exec_with_ptrace(int fd, char *const argv[], char *const envp[], BxlObserver *bxl)
{
    auto resolvedPath = bxl->fd_to_path(fd).c_str();
    return handle_exec_with_ptrace(resolvedPath, argv, envp, bxl);
}

INTERPOSE(int, fexecve, int fd, char *const argv[], char *const envp[])({
    // exec* functions start a new instance of the sandbox and therefore the process creation report
    // is sent on __init__

    result_t<int> result(0);
    if (bxl->SendBreakawayReportIfNeeded(fd, argv))
    {
        result = bxl->fwd_fexecve(fd, argv, bxl->removeEnvs(envp));
    }
    else if (bxl->check_and_report_process_requires_ptrace(fd))
    {
        return handle_exec_with_ptrace(fd, argv, bxl->ensureEnvs(envp), bxl);
    }
    else 
    {
        result = bxl->fwd_fexecve(fd, argv, bxl->ensureEnvs(envp));
    }

    // This will only execute if exec failed
    auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   __func__,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      bxl->fd_to_path(fd).c_str(),
        /* command_line */  bxl->GetProcessCommandLine((char **)argv));
    event.SetErrno(result.get_errno());
    BxlObserver::GetInstance()->CreateAndReportAccess(event);

    return result.restore();
})

INTERPOSE(int, execv, const char *file, char *const argv[])({
    // exec* functions start a new instance of the sandbox and therefore the process creation report
    // is sent on __init__

    result_t<int> result(0);
    if (bxl->SendBreakawayReportIfNeeded(file, argv))
    {
        result = bxl->fwd_execve(file, argv, bxl->removeEnvs(environ));
    }
    else if (bxl->check_and_report_process_requires_ptrace(file))
    {
        return handle_exec_with_ptrace(file, argv, bxl->ensureEnvs(environ), bxl);
    }
    else
    {
        result =  bxl->fwd_execve(file, argv, bxl->ensureEnvs(environ));
    }

    // This will only execute if exec failed
    auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   __func__,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      file,
        /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
    event.SetErrno(result.get_errno());
    BxlObserver::GetInstance()->CreateAndReportAccess(event);

    return result.restore();
})

INTERPOSE(int, execve, const char *file, char *const argv[], char *const envp[])({
    // exec* functions start a new instance of the sandbox and therefore the process creation report
    // is sent on __init__

    result_t<int> result(0);
    if (bxl->SendBreakawayReportIfNeeded(file, argv))
    {
        result =  bxl->fwd_execve(file, argv, bxl->removeEnvs(envp));
    }
    else if (bxl->check_and_report_process_requires_ptrace(file))
    {
        return handle_exec_with_ptrace(file, argv, bxl->ensureEnvs(envp), bxl);
    }
    else 
    {
        result =  bxl->fwd_execve(file, argv, bxl->ensureEnvs(envp));
    }

    // This will only execute if exec failed
    auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   __func__,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      file,
        /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
    event.SetErrno(result.get_errno());
    BxlObserver::GetInstance()->CreateAndReportAccess(event);

    return result.restore();
})

INTERPOSE(int, execvp, const char *file, char *const argv[])({
    // exec* functions start a new instance of the sandbox and therefore the process creation report
    // is sent on __init__

    mode_t mode = 0;
    std::string pathname;
    auto path_resolution_result = resolve_filename_with_env(file, mode, pathname);

    if (path_resolution_result)
    {
        result_t<int> result(0);
        if (bxl->SendBreakawayReportIfNeeded(pathname.c_str(), argv))
        {
            result = bxl->fwd_execve(pathname.c_str(), argv, bxl->removeEnvs(environ));
        }
        else if (bxl->check_and_report_process_requires_ptrace(pathname.c_str()))
        {
            return handle_exec_with_ptrace(pathname.c_str(), argv, bxl->ensureEnvs(environ), bxl);
        }
        else 
        {
            result = bxl->fwd_execve(pathname.c_str(), argv, bxl->ensureEnvs(environ));
        }

        // This will only execute if exec failed
        auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
            /* system_call */   __func__,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* src_path */      pathname.c_str(),
            /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
        event.SetErrno(result.get_errno());
        event.SetMode(mode);
        BxlObserver::GetInstance()->CreateAndReportAccess(event);
        
        return result.restore();
    }
    else
    {
        // exec* functions don't return unless they fail (the executing image gets replaced
        // by the specified one). So we cannot actually report back the errno
        result_t<int> result = bxl->fwd_execvpe(file, argv, bxl->ensureEnvs(environ));

        auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
            /* system_call */   __func__,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* src_path */      file,
            /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
        event.SetErrno(result.get_errno());
        event.SetMode(mode);
        BxlObserver::GetInstance()->CreateAndReportAccess(event);

        return result.restore();
    }
})

INTERPOSE(int, execvpe, const char *file, char *const argv[], char *const envp[])({
    // exec* functions start a new instance of the sandbox and therefore the process creation report
    // is sent on __init__

    mode_t mode = 0;
    std::string pathname;
    auto path_resolution_result = resolve_filename_with_env(file, mode, pathname);

    // If the path couldn't be resolved, then the exec will likely fail anyways
    if (path_resolution_result)
    {
        result_t<int> result(0);
        if (bxl->SendBreakawayReportIfNeeded(pathname.c_str(), argv))
        {
            result = bxl->fwd_execve(pathname.c_str(), argv, bxl->removeEnvs(envp));
        }
        else if (bxl->check_and_report_process_requires_ptrace(pathname.c_str()))
        {
            return handle_exec_with_ptrace(pathname.c_str(), argv, bxl->ensureEnvs(envp), bxl);
        }
        else 
        {
            result = bxl->fwd_execve(pathname.c_str(), argv, bxl->ensureEnvs(envp));
        }

        // This will only execute if exec failed
        auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
            /* system_call */   __func__,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* src_path */      pathname.c_str(),
            /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
        event.SetErrno(result.get_errno());
        event.SetMode(mode);
        BxlObserver::GetInstance()->CreateAndReportAccess(event);

        return result.restore();
    }
    else
    {
        result_t<int> result = bxl->fwd_execve(file, argv, bxl->ensureEnvs(envp));

        // This will only execute if exec failed
        auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
            /* system_call */   __func__,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* src_path */      file,
            /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
        event.SetErrno(result.get_errno());
        event.SetMode(mode);
        BxlObserver::GetInstance()->CreateAndReportAccess(event);

        return result.restore();
    }
})

INTERPOSE(int, execl, const char *pathname, const char *arg, ...)({
    va_list args;
    va_start(args, arg);
    ptrdiff_t argc = get_variadic_argc(args);
    va_end(args);

    if (argc == -1)
    {
        return -1;
    }

    va_start(args, arg);
    char *argv[argc + 1];
    parse_variadic_args(arg, argc, args, argv);
    va_end(args);

    result_t<int> result(0);
    if (bxl->SendBreakawayReportIfNeeded(pathname, (char **)argv))
    {
        result = bxl->fwd_execve(pathname, (char **)argv, bxl->removeEnvs(environ));
    }
    else if (bxl->check_and_report_process_requires_ptrace(pathname))
    {
        return handle_exec_with_ptrace(pathname, (char **)argv, bxl->ensureEnvs(environ), bxl);
    }
    else
    {
        result = bxl->fwd_execve(pathname, (char **)argv, bxl->ensureEnvs(environ));
    }

    // This will only execute if exec failed
    auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   __func__,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      pathname,
        /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
    event.SetErrno(result.get_errno());
    BxlObserver::GetInstance()->CreateAndReportAccess(event);

    return result.restore();
})

INTERPOSE(int, execlp, const char *file, const char *arg, ...)({
    va_list args;
    va_start(args, arg);
    ptrdiff_t argc = get_variadic_argc(args);
    va_end(args);

    if (argc == -1)
    {
        return -1;
    }

    va_start(args, arg);
    char *argv[argc + 1];
    parse_variadic_args(arg, argc, args, argv);
    va_end(args);

    mode_t mode = 0;
    std::string pathname;
    auto path_resolution_result = resolve_filename_with_env(file, mode, pathname);

    if (path_resolution_result)
    {
        result_t<int> result(0);
        if (bxl->SendBreakawayReportIfNeeded(pathname.c_str(), (char **)argv))
        {
            result = bxl->fwd_execve(pathname.c_str(), (char **)argv, bxl->removeEnvs(environ));
        }
        else if (bxl->check_and_report_process_requires_ptrace(pathname.c_str()))
        {
            return handle_exec_with_ptrace(pathname.c_str(), (char **)argv, bxl->ensureEnvs(environ), bxl);
        }
        else
        {
            result = bxl->fwd_execve(pathname.c_str(), (char **)argv, bxl->ensureEnvs(environ));
        }

        // This will only execute if exec failed
        auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
            /* system_call */   __func__,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* src_path */      pathname.c_str(),
            /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
        event.SetErrno(result.get_errno());
        event.SetMode(mode);
        BxlObserver::GetInstance()->CreateAndReportAccess(event);

        return result.restore();
    }
    else
    {
        result_t<int> result = bxl->fwd_execvp(file, (char **)argv);

        // This will only execute if exec failed
        auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
            /* system_call */   __func__,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* src_path */      file,
            /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
        event.SetErrno(result.get_errno());
        event.SetMode(mode);
        BxlObserver::GetInstance()->CreateAndReportAccess(event);

        return result.restore();
    }
})

INTERPOSE(int, execle, const char *pathname, const char *arg, ...)({
    va_list args;
    va_start(args, arg);
    ptrdiff_t argc = get_variadic_argc(args);
    va_end(args);

    if (argc == -1)
    {
        return -1;
    }

    va_start(args, arg);
    char *argv[argc + 1];
    char **envp;
    parse_variadic_args(arg, argc, args, argv);
    envp = va_arg(args, char **);
    va_end(args);

    result_t<int> result(0);
    if (bxl->SendBreakawayReportIfNeeded(pathname, (char **)argv))
    {
        result = bxl->fwd_execve(pathname, (char **)argv, bxl->removeEnvs(envp));
    }
    else if (bxl->check_and_report_process_requires_ptrace(pathname))
    {
        return handle_exec_with_ptrace(pathname, (char **)argv, bxl->ensureEnvs(envp), bxl);
    }
    else 
    {
        result = bxl->fwd_execve(pathname, (char **)argv, bxl->ensureEnvs(envp));
    }

    // This will only execute if exec failed
    auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   __func__,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      pathname,
        /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine((char **)argv));
    event.SetErrno(result.get_errno());
    BxlObserver::GetInstance()->CreateAndReportAccess(event);

    return result.restore();
})

#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
INTERPOSE(int, __fxstat, int __ver, int fd, struct stat *__stat_buf)({
    result_t<int> result = bxl->fwd___fxstat(__ver, fd, __stat_buf);

    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_fd */        fd);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE_SOMETIMES(int, __fxstat64,
if (BxlObserver::GetInstance()->IsPerformingInit())
{
    // During initialization, the sandbox may create a semaphore using sem_open.
    // sem_open will call __fxstat64 behind the scenes causing us to hit this codepath before init is complete.
    // However, since the file access is for an internal semaphore, we don't need to report it.
    // Additionally, the process creation access report has not been sent yet, meaning that the managed side
    // will consider this an unexpected access if we send it.
    // So during initialization we will just call the real function here using the short circuit check.
    // This is an unconventional use of the short circuit check since we get an instance of the bxlobserver here,
    // however in our case it should be okay to do because we know the bxlobserver doesn't call fxstat anywhere else during init
    // and we know that during semaphore creation the bxlobserver object has already been created.
    // Note: We can't call fwd___fxstat64 here because that will send a log message which we can't do until the semaphore has been created.
    return BxlObserver::GetInstance()->real___fxstat64(__ver, fd, buf);
},
int __ver, int fd, struct stat64 *buf)({
    result_t<int> result(bxl->fwd___fxstat64(__ver, fd, buf));
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_fd */        fd);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, __fxstatat, int __ver, int fd, const char *pathname, struct stat *__stat_buf, int flag)({
    result_t<int> result = bxl->fwd___fxstatat(__ver, fd, pathname, __stat_buf, flag);

    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname,
        /* src_fd */        fd);
    
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, __fxstatat64, int __ver, int fd, const char *pathname, struct stat64 *buf, int flag)({
    result_t<int> result = bxl->fwd___fxstatat64(__ver, fd, pathname, buf, flag);
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname,
        /* src_fd */        fd);
    
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, __xstat, int __ver, const char *pathname, struct stat *buf)({
    result_t<int> result = bxl->fwd___xstat(__ver, pathname, buf);

    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, __xstat64, int __ver, const char *pathname, struct stat64 *buf)({
    result_t<int> result(bxl->fwd___xstat64(__ver, pathname, buf));
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, __lxstat, int __ver, const char *pathname, struct stat *buf)({
    result_t<int> result = bxl->fwd___lxstat(__ver, pathname, buf);
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, __lxstat64, int __ver, const char *pathname, struct stat64 *buf)({
    result_t<int> result(bxl->fwd___lxstat64(__ver, pathname, buf));
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

#else
INTERPOSE(int, stat, const char *pathname, struct stat *statbuf)({
    result_t<int> result = bxl->fwd_stat(pathname, statbuf);
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, stat64, const char *pathname, struct stat64 *statbuf)({
    result_t<int> result = bxl->fwd_stat64(pathname, statbuf);
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, lstat, const char *pathname, struct stat *statbuf)({
    result_t<int> result = bxl->fwd_lstat(pathname, statbuf);
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, lstat64, const char *pathname, struct stat64 *statbuf)({
    result_t<int> result = bxl->fwd_lstat64(pathname, statbuf);
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_path */      pathname);
    
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, fstat, int fd, struct stat *statbuf)({
    result_t<int> result = bxl->fwd_fstat(fd, statbuf);
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_fd */        fd);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})

INTERPOSE(int, fstat64, int fd, struct stat64 *statbuf)({
    result_t<int> result = bxl->fwd_fstat64(fd, statbuf);
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         get_errno_from_result(result),
        /* src_fd */        fd);
    bxl->CreateAndReportAccess(event);
    return result.restore();
})
#endif

static buildxl::linux::EventType get_event_from_open_mode(const char *mode) {
    const char *pMode = mode;
    while (pMode && *pMode) {
        if (*pMode == 'a' || *pMode == 'w' || *pMode == '+') {
            return buildxl::linux::EventType::kGenericWrite;
        }
        ++pMode;
    }
    return buildxl::linux::EventType::kOpen;
}

INTERPOSE(FILE*, fdopen, int fd, const char *mode)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    get_event_from_open_mode(mode),
        /* pid */           getpid(),
        /* ppid */          getppid(),        
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fdopen(event, (FILE*)NULL, fd, mode);
})

INTERPOSE(FILE*, fopen, const char *pathname, const char *mode)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    get_event_from_open_mode(mode),
        /* pid */           getpid(),        
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    bxl->CreateAccess(event);
    FILE *f = bxl->check_fwd_and_report_fopen(event, (FILE*)NULL, pathname, mode);
    if (f) { bxl->reset_fd_table_entry(fileno(f)); }
    return f;
})

INTERPOSE(FILE*, fopen64, const char *pathname, const char *mode)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    get_event_from_open_mode(mode),
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    bxl->CreateAccess(event);
    FILE *f = bxl->check_fwd_and_report_fopen64(event, (FILE*)NULL, pathname, mode);
    if (f) { bxl->reset_fd_table_entry(fileno(f)); }
    return f;
})

INTERPOSE(FILE*, freopen, const char *pathname, const char *mode, FILE *stream)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    get_event_from_open_mode(mode),
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    bxl->CreateAccess(event);
    FILE *f = bxl->check_fwd_and_report_freopen(event, (FILE*)NULL, pathname, mode, stream);
    if (f) { bxl->reset_fd_table_entry(fileno(f)); }
    return f;
})

INTERPOSE(FILE*, freopen64, const char *pathname, const char *mode, FILE *stream)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    get_event_from_open_mode(mode),
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    bxl->CreateAccess(event);
    FILE *f = bxl->check_fwd_and_report_freopen64(event, (FILE*)NULL, pathname, mode, stream);
    if (f) { bxl->reset_fd_table_entry(fileno(f)); }
    return f;
})

INTERPOSE(size_t, fread, void *ptr, size_t size, size_t nmemb, FILE *stream)({
    auto stream_fd = fileno(stream);
    if (stream_fd == -1) {
        // fileno failed: the stream is not associated with a file
        // just forward the access without reporting anything
        return bxl->fwd_fread(ptr, size, nmemb, stream).restore();
    }
    
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kOpen,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        stream_fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fread(event, (size_t)0, ptr, size, nmemb, stream);
})

INTERPOSE(size_t, fwrite, const void *ptr, size_t size, size_t nmemb, FILE *stream)({
    auto stream_fd = fileno(stream);
    if (stream_fd == -1) {
        // fileno failed: the stream is not associated with a file
        // just forward the access without reporting anything
        return bxl->fwd_fwrite(ptr, size, nmemb, stream).restore();
    }

    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        stream_fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fwrite(event, (size_t)0, ptr, size, nmemb, stream);
})

INTERPOSE(int, fputc, int c, FILE *stream)({
    auto stream_fd = fileno(stream);
    if (stream_fd == -1) {
        // fileno failed: the stream is not associated with a file
        // just forward the access without reporting anything
        return bxl->fwd_fputc(c, stream).restore();
    }

    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        stream_fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fputc(event, ERROR_RETURN_VALUE, c, stream);
})

INTERPOSE(int, fputs, const char *s, FILE *stream)({
    auto stream_fd = fileno(stream);
    if (stream_fd == -1) {
        // fileno failed: the stream is not associated with a file
        // just forward the access without reporting anything
        return bxl->fwd_fputs(s, stream).restore();
    }

    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        stream_fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fputs(event, ERROR_RETURN_VALUE, s, stream);
})

INTERPOSE(int, putc, int c, FILE *stream)({
    auto stream_fd = fileno(stream);
    if (stream_fd == -1) {
        // fileno failed: the stream is not associated with a file
        // just forward the access without reporting anything
        return bxl->fwd_putc(c, stream).restore();
    }

    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        stream_fd);
    // Logging the forward calls on this sytem call are disabled because some processes make a lot of calls to putc
    event.DisableLogging();
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_putc(event, ERROR_RETURN_VALUE, c, stream);
})

INTERPOSE(int, putchar, int c)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fileno(stdout));
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_putchar(event, ERROR_RETURN_VALUE, c);
})

INTERPOSE(int, puts, const char *s)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fileno(stdout));
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_puts(event, ERROR_RETURN_VALUE, s);
})

INTERPOSE(int, access, const char *pathname, int mode)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_access(event, ERROR_RETURN_VALUE, pathname, mode);
})

INTERPOSE(int, faccessat, int dirfd, const char *pathname, int mode, int flags)({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname,
        /* src_fd */        dirfd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_faccessat(event, ERROR_RETURN_VALUE, dirfd, pathname, mode, flags);
})

// report "Create" if path does not exist and O_CREAT or O_TRUNC is specified
// report "Write" if path exists and O_CREAT or O_TRUNC is specified (because this truncates the file regardless of its content)
// otherwise, report "Read"
static buildxl::linux::SandboxEvent CreateFileOpen(BxlObserver *bxl, string &pathStr, int oflag)
{
    mode_t pathMode = bxl->get_mode(pathStr.c_str());
    bool pathExists = pathMode != 0;
    bool isCreate = !pathExists && (oflag & (O_CREAT|O_TRUNC));
    bool hasWriteAccess = ((oflag & O_ACCMODE) == O_WRONLY) || ((oflag & O_ACCMODE) == O_RDWR);
    bool isWrite = pathExists && (oflag & (O_CREAT|O_TRUNC) && hasWriteAccess);

    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    isCreate ? buildxl::linux::EventType::kCreate : isWrite ? buildxl::linux::EventType::kGenericWrite : buildxl::linux::EventType::kOpen,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathStr.c_str());
    
    event.SetMode(pathMode);

    // If O_NOFOLLOW is set and the file exists as a symlink, the call to open will fail,
    // but we should report the attempt of the access on the path to the symlink, without resolving the final component.
    if (oflag & O_NOFOLLOW != 0) {
        event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    }

    bxl->CreateAccess(event);

    return event;
}

INTERPOSE(int, open, const char *path, int oflag, ...)({
    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    std::string pathStr = bxl->normalize_path(path, getpid(), getppid());
    auto event = CreateFileOpen(bxl, pathStr, oflag);
    return ret_fd(bxl->check_fwd_and_report_open(event, ERROR_RETURN_VALUE, path, oflag, mode), bxl);
})

INTERPOSE(int, open64, const char *path, int oflag, ...)({
    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    std::string pathStr = bxl->normalize_path(path, getpid(), getppid());
    auto event = CreateFileOpen(bxl, pathStr, oflag);
    return ret_fd(bxl->check_fwd_and_report_open64(event, ERROR_RETURN_VALUE, path, oflag, mode), bxl);
})

INTERPOSE(int, openat, int dirfd, const char *pathname, int flags, ...)({
    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    std::string pathStr = bxl->normalize_path_at(dirfd, pathname, getpid(), getppid());
    auto event = CreateFileOpen(bxl, pathStr, flags);
    return ret_fd(bxl->check_fwd_and_report_openat(event, ERROR_RETURN_VALUE, dirfd, pathname, flags, mode), bxl);
})

INTERPOSE(int, openat64, int dirfd, const char *pathname, int flags, ...)({
    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    std::string pathStr = bxl->normalize_path_at(dirfd, pathname, getpid(), getppid());
    auto event = CreateFileOpen(bxl, pathStr, flags);
    return ret_fd(bxl->check_fwd_and_report_openat(event, ERROR_RETURN_VALUE, dirfd, pathname, flags, mode), bxl);
})

INTERPOSE(int, creat, const char *pathname, mode_t mode)({
    return open(pathname, O_CREAT | O_WRONLY | O_TRUNC, mode);
})

INTERPOSE(ssize_t, write, int fd, const void *buf, size_t bufsiz)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_write(event, (ssize_t)ERROR_RETURN_VALUE, fd, buf, bufsiz);
})

INTERPOSE(ssize_t, pwrite, int fd, const void *buf, size_t count, off_t offset)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_pwrite(event, (ssize_t)ERROR_RETURN_VALUE, fd, buf, count, offset);
})

INTERPOSE(ssize_t, writev, int fd, const struct iovec *iov, int iovcnt)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_writev(event, (ssize_t)ERROR_RETURN_VALUE, fd, iov, iovcnt);
})

INTERPOSE(ssize_t, pwritev, int fd, const struct iovec *iov, int iovcnt, off_t offset)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_pwritev(event, (ssize_t)ERROR_RETURN_VALUE, fd, iov, iovcnt, offset);
})

INTERPOSE(ssize_t, pwritev2, int fd, const struct iovec *iov, int iovcnt, off_t offset, int flags)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_pwritev2(event, (ssize_t)ERROR_RETURN_VALUE, fd, iov, iovcnt, offset, flags);
})

INTERPOSE(ssize_t, pwrite64, int fd, const void *buf, size_t count, off_t offset)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_pwrite64(event, (ssize_t)ERROR_RETURN_VALUE, fd, buf, count, offset);
})

INTERPOSE(int, remove, const char *pathname)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kUnlink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_remove(event, ERROR_RETURN_VALUE, pathname);
})

INTERPOSE(int, truncate, const char *path, off_t length)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      path);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_truncate(event, (ssize_t)ERROR_RETURN_VALUE, path, length);
})

INTERPOSE(int, ftruncate, int fd, off_t length)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_ftruncate(event, (ssize_t)ERROR_RETURN_VALUE, fd, length);
})

INTERPOSE(int, truncate64, const char *path, off_t length)({
    return truncate(path, length);
})

INTERPOSE(int, ftruncate64, int fd, off_t length)({
    return ftruncate(fd, length);
})

INTERPOSE(int, rmdir, const char *pathname)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kUnlink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);

    // We need to know all the rmdir attempts so we can identify which failed/succeeded, so don't use the cache
    // This is so we can track directory creation/deletion flow. Using the cache lumps all these operations into one report line
    bxl->CreateAccess(event, /*check_cache*/ false);
    return bxl->check_fwd_and_report_rmdir(event, ERROR_RETURN_VALUE, pathname);
})

static AccessCheckResult handle_renameat(BxlObserver *bxl, int olddirfd, const char *oldpath, int newdirfd, const char *newpath, std::vector<buildxl::linux::SandboxEvent> &events_to_report) {
    string old_path_normalized = bxl->normalize_path_at(olddirfd, oldpath, getpid(), getppid(), O_NOFOLLOW);
    string new_path_normalized = bxl->normalize_path_at(newdirfd, newpath, getpid(), getppid(), O_NOFOLLOW);

    mode_t mode = bxl->get_mode(old_path_normalized.c_str());    
    AccessCheckResult check = AccessCheckResult::Invalid();
    std::vector<std::string> files_and_directories;

    if (S_ISDIR(mode)) {
        bool enumerate_result = bxl->EnumerateDirectory(old_path_normalized, /*recursive*/true, files_and_directories);

        if (enumerate_result) {
            // reserve all the content for both source and destination
            events_to_report.reserve(files_and_directories.size() * 2);

            for (auto file_or_directory : files_and_directories) {
                // Access check for the source file
                auto source_event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   __func__,
                    /* event_type */    buildxl::linux::EventType::kUnlink,
                    /* pid */           getpid(),
                    /* ppid */          getppid(),
                    /* error */         0,
                    /* src_path */      file_or_directory.c_str());
                source_event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
                check = bxl->CreateAccess(source_event);
                events_to_report.emplace_back(source_event);

                // Access check for the destination file
                file_or_directory.replace(0, old_path_normalized.length(), new_path_normalized);
                auto target_event = CreateFileOpen(bxl, file_or_directory, O_CREAT | O_WRONLY);

                check = AccessCheckResult::Combine(check, target_event.GetSourceAccessCheckResult());
                events_to_report.emplace_back(target_event);

                // If access is denied to any of the files in the enumeration, we can break the loop right away here because check_and_fwd_renameat will also fail
                if (bxl->should_deny(check)) {
                    break;
                }
            }
        }
    }
    else {
        auto source_event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
            /* system_call */   __func__,
            /* event_type */    buildxl::linux::EventType::kUnlink,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* error */         0,
            /* src_path */      old_path_normalized.c_str());
        source_event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
        check = bxl->CreateAccess(source_event);

        events_to_report.emplace_back(source_event);

        auto target_event = CreateFileOpen(bxl, new_path_normalized, O_CREAT | O_WRONLY);
        check = AccessCheckResult::Combine(check, target_event.GetSourceAccessCheckResult());

        events_to_report.emplace_back(target_event);
    }

    return check;
}

INTERPOSE(int, renameat, int olddirfd, const char *oldpath, int newdirfd, const char *newpath)({
    std::vector<buildxl::linux::SandboxEvent> accesses_to_report;
    AccessCheckResult check = handle_renameat(bxl, olddirfd, oldpath, newdirfd, newpath, accesses_to_report);
    result_t<int> result(ERROR_RETURN_VALUE, EPERM);
    
    if (bxl->should_deny(check))
    {
        // It is enough that we send a single report as a witness for the denial
        // The last one in the array, in particular, is what should have triggered the denial
        bxl->SendReport(accesses_to_report.back());
    }
    else 
    {
        result = bxl->fwd_renameat(olddirfd, oldpath, newdirfd, newpath);
        for (auto access : accesses_to_report)
        {
            access.SetErrno(get_errno_from_result(result));
            bxl->SendReport(access);
        }
    }

    return result.restore();
})

INTERPOSE(int, renameat2, int olddirfd, const char *oldpath, int newdirfd, const char *newpath, unsigned int flags)({
    std::vector<buildxl::linux::SandboxEvent> accesses_to_report;
    AccessCheckResult check = handle_renameat(bxl, olddirfd, oldpath, newdirfd, newpath, accesses_to_report);
    result_t<int> result(ERROR_RETURN_VALUE, EPERM);
    
    if (bxl->should_deny(check))
    {
        // It is enough that we send a single report as a witness for the denial
        // The last one in the array, in particular, is what should have triggered the denial
        bxl->SendReport(accesses_to_report.back());
    }
    else 
    {
        result = bxl->fwd_renameat2(olddirfd, oldpath, newdirfd, newpath, flags);
        for (auto access : accesses_to_report)
        {
            access.SetErrno(get_errno_from_result(result));
            bxl->SendReport(access);
        }
    }

    return result.restore();
})

INTERPOSE(int, rename, const char *oldpath, const char *newpath)({ 
    return renameat(AT_FDCWD, oldpath, AT_FDCWD, newpath);
})

INTERPOSE(int, link, const char *path1, const char *path2)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kLink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      bxl->normalize_path(path1, getpid(), getppid(), O_NOFOLLOW).c_str(),
        /* dst_path */      bxl->normalize_path(path2, getpid(), getppid(), O_NOFOLLOW).c_str());
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_link(event, ERROR_RETURN_VALUE, path1, path2);
})

INTERPOSE(int, linkat, int fd1, const char *name1, int fd2, const char *name2, int flag)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kLink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      bxl->normalize_path_at(fd1, name1, getpid(), getppid(), O_NOFOLLOW).c_str(),
        /* dst_path */      bxl->normalize_path_at(fd2, name2, getpid(), getppid(), O_NOFOLLOW).c_str());
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_linkat(event, ERROR_RETURN_VALUE, fd1, name1, fd2, name2, flag);
})

INTERPOSE(int, unlink, const char *path)({
    if (path && *path == '\0')
    {
        return bxl->fwd_unlink(path).restore();
    }
    
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kUnlink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      path);
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_unlink(event, ERROR_RETURN_VALUE, path);
})

INTERPOSE(int, unlinkat, int dirfd, const char *path, int flags)({
    if (dirfd == AT_FDCWD && path && *path == '\0')
    {
        return bxl->fwd_unlinkat(dirfd, path, flags).restore();
    }

    int oflags = (flags & AT_REMOVEDIR) ? 0 : O_NOFOLLOW;
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kUnlink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      path,
        /* src_fd */        dirfd);

    if (oflags & O_NOFOLLOW != 0) {
        event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    }

    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_unlinkat(event, ERROR_RETURN_VALUE, dirfd, path, flags);
})

INTERPOSE(int, symlink, const char *target, const char *linkPath)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kCreate,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      bxl->normalize_path(linkPath, getpid(), getppid(), O_NOFOLLOW).c_str());
    event.SetMode(S_IFLNK);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_symlink(event, ERROR_RETURN_VALUE, target, linkPath);
})

INTERPOSE(int, symlinkat, const char *target, int dirfd, const char *linkPath)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kCreate,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      bxl->normalize_path_at(dirfd, linkPath, getpid(), getppid(), O_NOFOLLOW).c_str());
    event.SetMode(S_IFLNK);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_symlinkat(event, ERROR_RETURN_VALUE, target, dirfd, linkPath);
})

INTERPOSE_SOMETIMES(
    ssize_t,
    readlink,
    // rustc uses jemalloc
    // During it's initialization, jemalloc grabs a lock and then calls readlink on "/etc/malloc.conf"
    // libDomino hooks readlink
    // libDomino's readlink hook calls dlsym
    // dlsym calls calloc
    // calloc calls jemalloc
    // jemalloc tries to grab the lock, but this same thread already holds it.
    // To break this deadlock, we ideally would route this to real_readlink, but it is not initialized yet.
    // As a stopgap, we just assume it doesn't exist.
    if (path != NULL && (strcmp(path, "/etc/malloc.conf") == 0)) {
        errno = ENOENT;
        return -1;
    }, 
    const char *path, char *buf, size_t bufsize)(
{
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kReadLink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      path);
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_readlink(event, (ssize_t)ERROR_RETURN_VALUE, path, buf, bufsize);
})

INTERPOSE(ssize_t, readlinkat, int fd, const char *path, char *buf, size_t bufsize)({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kReadLink,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      path,
        /* src_fd */        fd);
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_readlinkat(event, (ssize_t)ERROR_RETURN_VALUE, fd, path, buf, bufsize);
})

INTERPOSE(char *, realpath, const char *path, char *resolved_path)({
    // realpath is a glibc wrapper around readlink, however glibc calls an internal readlink that 
    // is different from the wrapper we interpose, so it is necessary to interpose realpath directly,
    // and we need to simulate and report any symlink resolutions that happen in the call. 
    // It would be wrong to report a readlink on the full path or on intermediate paths
    // that are not actually symlinks, because the intention of the caller of 'realpath' 
    // is not to actually resolve a "known" symlink, but rather canonicalize a path that might or might
    // not contain intermediate symlinks (and the implementation of the function itself only calls readlink on actual symlinks).
    // So we should only report readlinks on the intermediate paths that actually end up being symlinks.
    // We optimize by only doing this "realpath simulation" if the resolved path that is returned is different
    // than the original path. 
    // NOTE: Since this isn't a write operation, it shouldn't be an issue that we can't block this call.
    // NOTE: There's no need for a corresponding interception in the PTrace sandbox, as this is not a syscall,
    //       (the function will call the readlink syscall which we do intercept).
    char *result = bxl->fwd_realpath(path, resolved_path).restore();
    
    if (path == nullptr) 
    {
        // This should have failed, nothing to do here
        return result;
    }

    // We should report a probe on the path passed to realpath:
    // if the full path is a symlink, we will report a readlink in the logic below,
    // but when it's not, we must count this as a probe because realpath will
    // indicate to the caller if this path was absent or not. 
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      path);
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAndReportAccess(event);

    if (result == nullptr)
    {
        // realpath returned an error, but the original path is not null
        // Let's try to report the intermediate symlinks anyway ourselves, because
        // technically they could have been probed before any failure.
        bxl->report_intermediate_symlinks(path, getpid(), getppid());
        return result;
    }

    // realpath succeeded. Let's report the intermediate symlinks if the result is
    // different than the original path.
    if (strcmp(path, result) != 0)
    {
        BXL_LOG_DEBUG(bxl, "[realpath] Resolving intermediate symlinks for '%s'", path);
        bxl->report_intermediate_symlinks(path, getpid(), getppid());

        // Report a probe on the returned path, as the success of this function
        // indicates to the caller that the path exists. 
        auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
            /* system_call */   __func__,
            /* event_type */    buildxl::linux::EventType::kGenericProbe,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* error */         0,
            /* src_path */      result);
        bxl->CreateAndReportAccess(event);
    }
    else
    {
        BXL_LOG_DEBUG(bxl, "[realpath] Skipping sandbox symlink resolution for path '%s'", path);
    }

    return result;
})

INTERPOSE(DIR*, opendir, const char *name)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      name);
    bxl->CreateAccess(event);
    DIR *d = bxl->check_fwd_and_report_opendir(event, (DIR*)NULL, name);
    if (d) { bxl->reset_fd_table_entry(dirfd(d)); }
    return d;
})

INTERPOSE(DIR*, fdopendir, int fd)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericProbe,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fdopendir(event, (DIR*)NULL, fd);
})

INTERPOSE(int, utime, const char *filename, const struct utimbuf *times)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      filename);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_utime(event, ERROR_RETURN_VALUE, filename, times);
})

INTERPOSE(int, utimes, const char *filename, const struct timeval times[2])({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      filename);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_utimes(event, ERROR_RETURN_VALUE, filename, times);
})

INTERPOSE(int, utimensat, int dirfd, const char *pathname, const struct timespec times[2], int flags)({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname,
        /* src_fd */        dirfd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_utimensat(event, ERROR_RETURN_VALUE, dirfd, pathname, times, flags);
})

INTERPOSE(int, futimens, int fd, const struct timespec times[2])({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_futimens(event, ERROR_RETURN_VALUE, fd, times);
})

INTERPOSE(int, futimesat, int dirfd, const char *pathname, const struct timeval times[2])({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname,
        /* src_fd */        dirfd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_futimesat(event, ERROR_RETURN_VALUE, dirfd, pathname, times);
})

static buildxl::linux::SandboxEvent ReportCreate(const char *syscall, BxlObserver *bxl, int dirfd, const char *pathname, mode_t mode, bool checkCache = true)
{
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kCreate,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      bxl->normalize_path_at(dirfd, pathname, getpid(), getppid()).c_str());
    event.SetMode(mode);
    bxl->CreateAccess(event, checkCache);

    return event;
}

INTERPOSE(int, mkdir, const char *pathname, mode_t mode)({
    // We don't want to use the cache. Check comment in rmdir interposing for details.
    auto event = ReportCreate(__func__, bxl, AT_FDCWD, pathname, S_IFDIR, /* checkCache */ false);
    return bxl->check_fwd_and_report_mkdir(event, ERROR_RETURN_VALUE, pathname, mode);
})

INTERPOSE(int, mkdirat, int dirfd, const char *pathname, mode_t mode)({
    // We don't want to use the cache. Check comment in rmdir interposing for details.
    auto event = ReportCreate(__func__, bxl, dirfd, pathname, S_IFDIR, /* checkCache */ false);
    return bxl->check_fwd_and_report_mkdirat(event, ERROR_RETURN_VALUE, dirfd, pathname, mode);
})

INTERPOSE(int, mknod, const char *pathname, mode_t mode, dev_t dev)({
    auto event = ReportCreate(__func__, bxl, AT_FDCWD, pathname, S_IFREG);
    return bxl->check_fwd_and_report_mknod(event, ERROR_RETURN_VALUE, pathname, mode, dev);
})

INTERPOSE(int, mknodat, int dirfd, const char *pathname, mode_t mode, dev_t dev)({
    auto event = ReportCreate(__func__, bxl, dirfd, pathname, S_IFREG);
    return bxl->check_fwd_and_report_mknodat(event, ERROR_RETURN_VALUE, dirfd, pathname, mode, dev);
})

#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
INTERPOSE(int, __xmknod, int ver, const char * path, mode_t mode, dev_t * dev)({
    if (mode == 0 || mode & S_IFREG)
    {
        auto event = ReportCreate(__func__, bxl, AT_FDCWD, path, S_IFREG);
        return bxl->check_fwd_and_report___xmknod(event, ERROR_RETURN_VALUE, ver, path, mode, dev);
    }
    
    // the type of block being created is a non-file (eg: fifo, socket, etc.), we don't have to report it
    return bxl->fwd___xmknod(ver, path, mode, dev).restore();
})

INTERPOSE(int, __xmknodat, int ver, int dirfd, const char * path, mode_t mode, dev_t * dev)({
    if (mode == 0 || mode & S_IFREG)
    {
        auto event = ReportCreate(__func__, bxl, dirfd, path, S_IFREG);
        return bxl->check_fwd_and_report___xmknodat(event, ERROR_RETURN_VALUE, ver, dirfd, path, mode, dev);
    }

    // the type of block being created is a non-file (eg: fifo, socket, etc.), we don't have to report it
    return bxl->fwd___xmknodat(ver, dirfd, path, mode, dev).restore();
})
#endif

INTERPOSE(int, vprintf, const char *fmt, va_list args)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        1);
    bxl->CreateAccess(event);
    return bxl->fwd_vprintf(fmt, args).restore();
})

INTERPOSE(int, vfprintf, FILE *f, const char *fmt, va_list args)({
    auto stream_fd = fileno(f);
    if (stream_fd == -1) {
        // fileno failed: the stream is not associated with a file
        // just forward the access without reporting anything
        return bxl->fwd_vfprintf(f, fmt, args).restore();
    }

    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        stream_fd);
    bxl->CreateAccess(event);
    return bxl->fwd_vfprintf(f, fmt, args).restore();
})

INTERPOSE(int, vdprintf, int fd, const char *fmt, va_list args)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->fwd_and_report_vdprintf(event, -1, fd, fmt, args).restore();
})

INTERPOSE(int, printf, const char *fmt, ...)({
    va_list args;
    va_start(args, fmt);
    result_t<int> result = vprintf(fmt, args);
    va_end(args);
    return result.restore();
})

INTERPOSE(int, fprintf, FILE *f, const char *fmt, ...)({
    va_list args;
    va_start(args, fmt);
    result_t<int> result = vfprintf(f, fmt, args);
    va_end(args);
    return result.restore();
})

INTERPOSE(int, dprintf, int fd, const char *fmt, ...)({
    va_list args;
    va_start(args, fmt);
    result_t<int> result = vdprintf(fd, fmt, args);
    va_end(args);
    return result.restore();
})

INTERPOSE(int, chmod, const char *pathname, mode_t mode)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_chmod(event, ERROR_RETURN_VALUE, pathname, mode);
})

INTERPOSE(int, fchmod, int fd, mode_t mode)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fchmod(event, ERROR_RETURN_VALUE, fd, mode);
})

INTERPOSE(int, fchmodat, int dirfd, const char *pathname, mode_t mode, int flags)({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname,
        /* src_fd */        dirfd);

    if ((flags & AT_SYMLINK_NOFOLLOW) != 0) {
        event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    }

    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fchmodat(event, ERROR_RETURN_VALUE, dirfd, pathname, mode, flags);
})

INTERPOSE(void*, dlopen, const char *filename, int flags)({
    static int libcSoNameLength = -1;
    if (libcSoNameLength == -1) libcSoNameLength = strlen(LIBC_SO);

    if (filename && (strncmp(filename, LIBC_SO, libcSoNameLength) == 0))
    {
        BXL_LOG_DEBUG(bxl, "NOT forwarding dlopen(\"%s\", %d); returning dlopen(NULL, %d)", filename, flags, flags);
        return bxl->real_dlopen((char*)NULL, flags);
    }
    else
    {
        return bxl->fwd_dlopen(filename, flags).restore();
    }
})

INTERPOSE(int, chown, const char *pathname, uid_t owner, gid_t group)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_chown(event, ERROR_RETURN_VALUE, pathname, owner, group);
})

INTERPOSE(int, fchown, int fd, uid_t owner, gid_t group)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fchown(event, ERROR_RETURN_VALUE, fd, owner, group);
})

INTERPOSE(int, lchown, const char *pathname, uid_t owner, gid_t group)({
    auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname);
    event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_lchown(event, ERROR_RETURN_VALUE, pathname, owner, group);
})

INTERPOSE(int, chown32, const char *pathname, uid_t owner, gid_t group)({ return chown(pathname, owner, group); })
INTERPOSE(int, fchown32, int fd, uid_t owner, gid_t group)({ return fchown(fd, owner, group); })
INTERPOSE(int, lchown32, const char *pathname, uid_t owner, gid_t group)({ return lchown(pathname, owner, group); })

INTERPOSE(int, fchownat, int dirfd, const char *pathname, uid_t owner, gid_t group, int flags)({
    auto event = buildxl::linux::SandboxEvent::RelativePathSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_path */      pathname,
        /* src_fd */        dirfd);

    if ((flags & AT_SYMLINK_NOFOLLOW) != 0) {
        event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kResolveNoFollow);
    }
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_fchownat(event, ERROR_RETURN_VALUE, dirfd, pathname, owner, group, flags);
})

INTERPOSE(ssize_t, sendfile, int out_fd, int in_fd, off_t *offset, size_t count)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        out_fd);
    bxl->CreateAccess(event);
    return bxl->check_fwd_and_report_sendfile(event, (ssize_t)ERROR_RETURN_VALUE, out_fd, in_fd, offset, count);
})

INTERPOSE(ssize_t, sendfile64, int out_fd, int in_fd, off_t *offset, size_t count)({
    return sendfile(out_fd, in_fd, offset, count);
})

INTERPOSE(ssize_t, copy_file_range, int fd_in, loff_t *off_in, int fd_out, loff_t *off_out, size_t len, unsigned int flags)({
    auto event = buildxl::linux::SandboxEvent::FileDescriptorSandboxEvent(
        /* system_call */   __func__,
        /* event_type */    buildxl::linux::EventType::kGenericWrite,
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* error */         0,
        /* src_fd */        fd_out);
    bxl->CreateAccess(event);

    AccessCheckResult check = event.GetEventAccessCheckResult();

    ssize_t result;
    if (bxl->should_deny(check)) {
        errno = EPERM;
        result = (ssize_t)ERROR_RETURN_VALUE;
        goto exit;
    }

    // TODO: Remove the following workaround when the kernel bug is fixed.
    //
    // Due to (possibly) kernel bug, copy_file_range does no longer work when the file descriptors are not mounted on the same
    // filesystems, despite what is said in the manual https://man7.org/linux/man-pages/man2/copy_file_range.2.html.
    // This bug breaks AnyBuild virtual filesystem (VFS) because the source file will be in the read-only (lower) layer of overlayfs, and this
    // layer is mounted on AnyBuild FUSE, and the target file will be in the writable (upper) layer of overlayfs.
    //
    // In the commented code below, we try to check if the file descriptors are mounted on the same filesystem, and if so, we simply call
    // copy_file_range. On the user space, the descriptors are mounted on the same filesystem. However, when copy_file_range is called,
    // and the call goes into the kernel space, those descriptors are identified from different filesystems, and so the call will fail with EXDEV.
    //
    // ------------------------------------------------------------------------------------------
    // struct stat st_in;
    // if (fstat(fd_in, &st_in) == -1)
    //     return (ssize_t)ERROR_RETURN_VALUE;
    // struct stat st_out;
    // if (fstat(fd_out, &st_out) == -1)
    //     return (ssize_t)ERROR_RETURN_VALUE;
    // errno = 0;
    // if ((uintmax_t)major(st_in.st_dev) == (uintmax_t)major(st_out.st_dev)
    //     && (uintmax_t)minor(st_in.st_dev) == (uintmax_t)minor(st_out.st_dev))
    //     return bxl->fwd_copy_file_range(fd_in, off_in, fd_out, off_out, len, flags).restore();
    // ------------------------------------------------------------------------------------------

    // The code below implements copy_file_range using splice(2). The idea is the content is first
    // copied to the pipe and then transferred to the target.

    // Check for flags.
    if (flags != 0) {
        errno = EINVAL;
        result = (ssize_t)ERROR_RETURN_VALUE;
        goto exit;
    }

    // Check for overlapped range.
    if (fd_in == fd_out) {
        off64_t start_off_in = off_in == NULL ? lseek(fd_in, 0, SEEK_CUR) : *off_in;
        off64_t end_off_in = start_off_in + len;
        off64_t start_off_out = off_out == NULL ? lseek(fd_out, 0, SEEK_CUR) : *off_out;
        off64_t end_off_out = start_off_out + len;
        if ((start_off_in <= end_off_out && end_off_in >= start_off_out)
            || (start_off_out <= end_off_in && end_off_out >= start_off_in)) {
                errno = EINVAL;
                result = (ssize_t)ERROR_RETURN_VALUE;
                goto exit;
            }
    }

    errno = 0;

    // Creates a pipe.
    int pipefd[2];
    result = pipe(pipefd);
    if (result < 0)
        goto exit;

    // Copy from input to pipe.
    result = splice(fd_in, off_in, pipefd[1], NULL, len, 0);
    if (result < 0)
        goto exit;

    // Copy from pipe to output.
    result = splice(pipefd[0], NULL, fd_out, off_out, result, 0);

exit:
    close(pipefd[0]);
    close(pipefd[1]);

    event.SetErrno(result == -1? errno : 0);
    bxl->ReportAccess(event);
    
    return result;
})

INTERPOSE(int, name_to_handle_at, int dirfd, const char *pathname, struct file_handle *handle, int *mount_id, int flags)({
    int oflags = (flags & AT_SYMLINK_FOLLOW) ? 0 : O_NOFOLLOW;
    string pathStr = bxl->normalize_path_at(dirfd, pathname, getpid(), getppid(), oflags);
    auto event = CreateFileOpen(bxl, pathStr, oflags);

    return ret_fd(bxl->check_fwd_and_report_name_to_handle_at(event, ERROR_RETURN_VALUE, dirfd, pathname, handle, mount_id, flags), bxl);
})

INTERPOSE(int, close, int fd) ({ 
    bxl->reset_fd_table_entry(fd);
    return bxl->fwd_close(fd).restore();
})

INTERPOSE(int, fclose, FILE *f) ({
    bxl->reset_fd_table_entry(fileno(f));
    return bxl->fwd_fclose(f).restore();
})

INTERPOSE(int, closedir, DIR *dirp) ({ 
    bxl->reset_fd_table_entry(dirfd(dirp));
    return bxl->fwd_closedir(dirp).restore();
})

INTERPOSE(int, dup, int fd) ({ 
    return ret_fd(bxl->real_dup(fd), bxl);    
    // Sometimes useful (for debugging) to interpose without access checking:
    // return bxl->fwd_dup(fd).restore();     
})

INTERPOSE(int, dup2, int oldfd, int newfd)({
    // If the file descriptor newfd was previously open, it is closed
    // before being reused; the close is performed silently, so we should reset the fd table.
    bxl->reset_fd_table_entry(newfd);

    return bxl->real_dup2(oldfd, newfd); 
    // Sometimes useful (for debugging) to interpose without access checking:
    // return bxl->fwd_dup2(oldfd, newfd).restore();  
})

INTERPOSE(int, dup3, int oldfd, int newfd, int flags)({
    // If the file descriptor newfd was previously open, it is closed
    // before being reused; the close is performed silently, so we should reset the fd table.
    bxl->reset_fd_table_entry(newfd);

    return bxl->real_dup3(oldfd, newfd, flags); 
    // Sometimes useful (for debugging) to interpose without access checking:
    //return bxl->fwd_dup3(oldfd, newfd).restore();  
})

static void report_exit(int exitCode, void *args)
{
    BxlObserver::GetInstance()->SendExitReport(getpid(), getppid());
}

// invoked by the loader when our shared library is dynamically loaded into a new host process
void __attribute__ ((constructor)) _bxl_linux_sandbox_init(void)
{
    // set up an on-exit handler
    on_exit(report_exit, NULL);

    BxlObserver::GetInstance()->Init();

    // TODO: we shouldn't be sending a clone event here and we should remove this event. 
    // This code path is hit after an exec or after launching the sandbox for the root process, no 'clone' happens here.
    // What prevents us from removing it is the case of launching the sandbox for the root process. Our managed side tracking does
    // expect a 'clone/fork' event before an exec in order to assign the right pids and update the active process collection. Doing
    // this on managed side is racy (since the pid to use will be available only after the root process has started and events may have arrived already)
    // One alternative is using some hidden env var to distinguish the 'root process case' here and only send a clone in that case. 
    // On the other hand, sending an extra clone event is just confusing, but harmless. Maybe couple this change with some other related refactoring.
    auto fork_event = buildxl::linux::SandboxEvent::CloneSandboxEvent(
        /* system_call */   "__init__fork",
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      BxlObserver::GetInstance()->GetProgramPath());
    BxlObserver::GetInstance()->CreateAndReportAccess(fork_event);

    // Report the command line args
    auto event = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   "__init__exec",
        /* pid */           getpid(),
        /* ppid */          getppid(),
        /* src_path */      BxlObserver::GetInstance()->GetProgramPath(),
        /* command_line */  BxlObserver::GetInstance()->GetProcessCommandLine(getpid()));

    BxlObserver::GetInstance()->CreateAndReportAccess(event);
}

// ==========================

// having a main function is useful for various local testing
int main(int argc, char **argv)
{
    BxlObserver *inst = BxlObserver::GetInstance();
    printf("Path: %s\n", inst->GetReportsPath());
}

/* ============ don't need to be interposed =======================

INTERPOSE(int, statfs, const char *pathname, struct statfs *buf)({
    result_t<int> result = bxl->fwd_statfs(pathname, buf);
    // ... report buildxl::linux::EventType::kGenericProbe
    return result.restore();
})

INTERPOSE(int, statfs64, const char *pathname, struct statfs64 *buf)({
    result_t<int> result = bxl->fwd_statfs64(pathname, buf);
    // ... report buildxl::linux::EventType::kGenericProbe
    return result.restore();
})

INTERPOSE(int, fstatfs, int fd, struct statfs *buf)({
    result_t<int> result = bxl->fwd_fstatfs(fd, buf);
    // ... report buildxl::linux::EventType::kGenericProbe
    return result.restore();
})

INTERPOSE(int, fstatfs64, int fd, struct statfs64 *buf)({
    result_t<int> result = bxl->fwd_fstatfs64(fd, buf);
    // ... report buildxl::linux::EventType::kGenericProbe
    return result.restore();
})

=================================================================== */

/* ============ old/obsolete/unavailable ==========================

INTERPOSE(int, execveat, int dirfd, const char *pathname, char *const argv[], char *const envp[], int flags)({
    int oflags = (flags & AT_SYMLINK_NOFOLLOW) ? O_NOFOLLOW : 0;
    string exe_path = bxl->normalize_path_at(dirfd, pathname, oflags);
    // ... report exec
    return bxl->fwd_execveat(dirfd, pathname, argv, bxl->ensureEnvs(envp), flags).restore();
})

INTERPOSE(int, getdents, unsigned int fd, struct linux_dirent *dirp, unsigned int count)({
    // ... report buildxl::linux::EventType::kGenericRead
    return bxl->check_and_fwd_getdents(check, ERROR_RETURN_VALUE, fd, dirp, count);
})

INTERPOSE(int, getdents64, unsigned int fd, struct linux_dirent64 *dirp, unsigned int count)({
    // ... report buildxl::linux::EventType::kGenericRead
    return bxl->check_and_fwd_getdents64(check, ERROR_RETURN_VALUE, fd, dirp, count);
})

=================================================================== */
