#include <windows.h>
#include <shellapi.h>

#define APP_RELATIVE_PATH L"\\app\\G-CET-Runtime-Profiler.App.exe"

static const wchar_t* SkipExecutableToken(const wchar_t* commandLine)
{
    const wchar_t* p = commandLine;

    while (*p == L' ' || *p == L'\t')
        ++p;

    if (*p == L'"')
    {
        ++p;
        while (*p && *p != L'"')
            ++p;
        if (*p == L'"')
            ++p;
    }
    else
    {
        while (*p && *p != L' ' && *p != L'\t')
            ++p;
    }

    while (*p == L' ' || *p == L'\t')
        ++p;

    return p;
}

static int Fail(const wchar_t* message)
{
    MessageBoxW(
        NULL,
        message,
        L"G-CET Runtime Profiler",
        MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    return 1;
}

int WINAPI wWinMain(
    HINSTANCE instance,
    HINSTANCE previousInstance,
    PWSTR commandLineUnused,
    int showCommand)
{
    (void)instance;
    (void)previousInstance;
    (void)commandLineUnused;
    (void)showCommand;

    wchar_t packageRoot[32768];
    DWORD rootLength = GetModuleFileNameW(NULL, packageRoot, ARRAYSIZE(packageRoot));
    if (rootLength == 0 || rootLength >= ARRAYSIZE(packageRoot))
        return Fail(L"Could not resolve the profiler package directory.");

    wchar_t* separator = packageRoot + rootLength;
    while (separator > packageRoot &&
           separator[-1] != L'\\' &&
           separator[-1] != L'/')
    {
        --separator;
    }

    if (separator == packageRoot)
        return Fail(L"Could not resolve the profiler package directory.");

    separator[-1] = L'\0';

    const SIZE_T rootChars = (SIZE_T)lstrlenW(packageRoot);
    const SIZE_T relativeChars = (SIZE_T)lstrlenW(APP_RELATIVE_PATH);
    if (rootChars + relativeChars + 1 >= ARRAYSIZE(packageRoot))
        return Fail(L"The profiler package path is too long.");

    wchar_t appPath[32768];
    CopyMemory(appPath, packageRoot, rootChars * sizeof(wchar_t));
    CopyMemory(
        appPath + rootChars,
        APP_RELATIVE_PATH,
        (relativeChars + 1) * sizeof(wchar_t));

    DWORD attributes = GetFileAttributesW(appPath);
    if (attributes == INVALID_FILE_ATTRIBUTES ||
        (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
    {
        return Fail(
            L"The internal application is missing.\r\n\r\n"
            L"Expected: app\\G-CET-Runtime-Profiler.App.exe\r\n\r\n"
            L"Re-extract the complete profiler package.");
    }

    // The managed app already resolves the package root from its parent
    // directory. Keep this launcher deliberately boring: ask the Windows shell
    // to open the real application rather than manually constructing and
    // injecting a CreateProcess command line/environment.
    const wchar_t* forwarded = SkipExecutableToken(GetCommandLineW());
    const BOOL headless = forwarded != NULL && *forwarded != L'\0';

    SHELLEXECUTEINFOW launch;
    ZeroMemory(&launch, sizeof(launch));
    launch.cbSize = sizeof(launch);
    launch.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
    launch.hwnd = NULL;
    launch.lpVerb = L"open";
    launch.lpFile = appPath;
    launch.lpParameters = headless ? forwarded : NULL;
    launch.lpDirectory = packageRoot;
    launch.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&launch) || launch.hProcess == NULL)
        return Fail(L"Windows could not start the internal profiler application.");

    // GUI launch: return immediately. CLI/headless launch: preserve the existing
    // launcher contract by waiting and forwarding the managed app's exit code.
    if (!headless)
    {
        CloseHandle(launch.hProcess);
        return 0;
    }

    WaitForSingleObject(launch.hProcess, INFINITE);

    DWORD exitCode = 1;
    GetExitCodeProcess(launch.hProcess, &exitCode);
    CloseHandle(launch.hProcess);

    return (int)exitCode;
}
