#define WIN32_LEAN_AND_MEAN
#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <dpapi.h>
#include <credentialprovider.h>
#include <ntsecapi.h>
#include <strsafe.h>
#include <shlguid.h>
#include <shlwapi.h>
#include <new>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "credui.lib")
#pragma comment(lib, "secur32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "gdi32.lib")

static HMODULE g_hModule = nullptr;
static LONG g_dllRef = 0;

static const GUID CLSID_UnlockServerProvider =
{ 0xa31e8c27, 0x9b14, 0x4f6d, { 0x8e, 0x52, 0x1c, 0x7a, 0x9d, 0x04, 0xe6, 0xb3 } };

static const wchar_t kProviderKey[] =
    L"Software\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\{A31E8C27-9B14-4F6D-8E52-1C7A9D04E6B3}";
static const wchar_t kEventName[] = L"Global\\UnlockServer.UnlockPulse";
static const wchar_t kCredPath[] = L"C:\\ProgramData\\UnlockServer\\local.cred";
static const wchar_t kReqPath[] = L"C:\\ProgramData\\UnlockServer\\unlock.req";
static const wchar_t kLogPath[] = L"C:\\ProgramData\\UnlockServer\\provider.log";

enum FieldId { FID_TILE = 0, FID_TITLE = 1, FID_SUBTITLE = 2, FID_COUNT = 3 };

static HBITMAP CreateTileBitmap()
{
    const int W = 128;
    const int H = 128;
    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = W;
    bmi.bmiHeader.biHeight = -H;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    HDC screen = GetDC(nullptr);
    void* bits = nullptr;
    HDC dc = CreateCompatibleDC(screen);
    HBITMAP bmp = CreateDIBSection(dc, &bmi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (!bmp)
    {
        DeleteDC(dc);
        ReleaseDC(nullptr, screen);
        return nullptr;
    }

    HGDIOBJ old = SelectObject(dc, bmp);
    RECT rc = { 0, 0, W, H };
    HBRUSH bg = CreateSolidBrush(RGB(20, 92, 160));
    FillRect(dc, &rc, bg);
    DeleteObject(bg);

    HPEN pen = CreatePen(PS_SOLID, 10, RGB(255, 255, 255));
    HGDIOBJ oldPen = SelectObject(dc, pen);
    MoveToEx(dc, 64, 22, nullptr);
    LineTo(dc, 64, 106);
    MoveToEx(dc, 38, 46, nullptr);
    LineTo(dc, 64, 22);
    LineTo(dc, 90, 46);
    LineTo(dc, 64, 64);
    MoveToEx(dc, 38, 82, nullptr);
    LineTo(dc, 64, 106);
    LineTo(dc, 90, 82);
    LineTo(dc, 64, 64);
    SelectObject(dc, oldPen);
    DeleteObject(pen);

    SelectObject(dc, old);
    DeleteDC(dc);
    ReleaseDC(nullptr, screen);
    return bmp;
}

static void ProvLog(const char* msg)
{
    HANDLE h = CreateFileW(kLogPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME st;
    GetLocalTime(&st);
    char line[512];
    int n = sprintf_s(line, "%04d-%02d-%02d %02d:%02d:%02d %s\r\n",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, msg ? msg : "");
    if (n > 0)
    {
        DWORD w = 0;
        WriteFile(h, line, (DWORD)n, &w, nullptr);
    }
    CloseHandle(h);
}

static void SafeRelease(IUnknown** pp)
{
    if (pp && *pp)
    {
        (*pp)->Release();
        *pp = nullptr;
    }
}

static HANDLE CreatePulseEvent()
{
    SECURITY_DESCRIPTOR sd;
    InitializeSecurityDescriptor(&sd, SECURITY_DESCRIPTOR_REVISION);
    SetSecurityDescriptorDacl(&sd, TRUE, nullptr, FALSE);
    SECURITY_ATTRIBUTES sa = { sizeof(sa), &sd, FALSE };
    return CreateEventW(&sa, FALSE, FALSE, kEventName);
}

static bool ReadUtf8File(const wchar_t* path, char* buf, DWORD bufSize)
{
    HANDLE h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD read = 0;
    BOOL ok = ReadFile(h, buf, bufSize - 1, &read, nullptr);
    CloseHandle(h);
    if (!ok) return false;
    buf[read] = 0;
    return read > 0;
}

static bool IsUnlockRequested()
{
    char raw[64] = {};
    if (!ReadUtf8File(kReqPath, raw, sizeof(raw))) return false;
    unsigned long long ticks = 0;
    if (sscanf_s(raw, "%llu", &ticks) != 1 || ticks == 0) return false;

    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    ULARGE_INTEGER now;
    now.LowPart = ft.dwLowDateTime;
    now.HighPart = ft.dwHighDateTime;
    const unsigned long long epochDiff = 504911232000000000ULL;
    unsigned long long nowTicks = now.QuadPart + epochDiff;
    if (nowTicks < ticks) return false;
    return (nowTicks - ticks) < 30ULL * 10000000ULL;
}

static bool DpapiUnprotectFile(const wchar_t* path, wchar_t* domain, wchar_t* user, wchar_t* pass, wchar_t* sid, DWORD cch)
{
    domain[0] = user[0] = pass[0] = 0;
    if (sid) sid[0] = 0;

    HANDLE h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER sz;
    if (!GetFileSizeEx(h, &sz) || sz.QuadPart <= 0 || sz.QuadPart > 64 * 1024)
    {
        CloseHandle(h);
        return false;
    }
    DWORD n = (DWORD)sz.QuadPart;
    BYTE* blob = (BYTE*)LocalAlloc(LMEM_FIXED, n);
    DWORD read = 0;
    BOOL ok = ReadFile(h, blob, n, &read, nullptr);
    CloseHandle(h);
    if (!ok || read != n)
    {
        LocalFree(blob);
        return false;
    }

    DATA_BLOB in = {}, out = {};
    in.pbData = blob;
    in.cbData = read;
    if (!CryptUnprotectData(&in, nullptr, nullptr, nullptr, nullptr, CRYPTPROTECT_UI_FORBIDDEN, &out))
    {
        LocalFree(blob);
        ProvLog("CryptUnprotectData failed");
        return false;
    }
    LocalFree(blob);

    int wlen = MultiByteToWideChar(CP_UTF8, 0, (LPCSTR)out.pbData, (int)out.cbData, nullptr, 0);
    wchar_t* wide = (wchar_t*)LocalAlloc(LMEM_FIXED, (wlen + 1) * sizeof(wchar_t));
    MultiByteToWideChar(CP_UTF8, 0, (LPCSTR)out.pbData, (int)out.cbData, wide, wlen);
    wide[wlen] = 0;
    LocalFree(out.pbData);

    wchar_t* p1 = wcschr(wide, L'\n');
    if (!p1) { LocalFree(wide); return false; }
    *p1 = 0;
    wchar_t* p2 = wcschr(p1 + 1, L'\n');
    if (!p2) { LocalFree(wide); return false; }
    *p2 = 0;
    wchar_t* p3 = wcschr(p2 + 1, L'\n');
    if (p3) *p3 = 0;

    StringCchCopyW(domain, cch, wide[0] ? wide : L".");
    StringCchCopyW(user, cch, p1 + 1);
    StringCchCopyW(pass, cch, p2 + 1);
    if (sid && p3)
        StringCchCopyW(sid, cch, p3 + 1);

    SecureZeroMemory(wide, (wlen + 1) * sizeof(wchar_t));
    LocalFree(wide);
    return user[0] != 0 && pass[0] != 0;
}

static void UnicodeStringInit(UNICODE_STRING* us, wchar_t* s)
{
    size_t len = 0;
    StringCchLengthW(s, 512, &len);
    us->Buffer = s;
    us->Length = (USHORT)(len * sizeof(wchar_t));
    us->MaximumLength = (USHORT)((len + 1) * sizeof(wchar_t));
}

static HRESULT PackUnlockLogon(const wchar_t* domain, const wchar_t* user, const wchar_t* pass,
    CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, BYTE** prgb, DWORD* pcb)
{
    KERB_INTERACTIVE_UNLOCK_LOGON kiul = {};
    kiul.Logon.MessageType = (cpus == CPUS_UNLOCK_WORKSTATION)
        ? KerbWorkstationUnlockLogon
        : KerbInteractiveLogon;

    wchar_t d[256], u[256], p[256];
    StringCchCopyW(d, 256, domain && domain[0] ? domain : L".");
    StringCchCopyW(u, 256, user);
    StringCchCopyW(p, 256, pass);
    UnicodeStringInit(&kiul.Logon.LogonDomainName, d);
    UnicodeStringInit(&kiul.Logon.UserName, u);
    UnicodeStringInit(&kiul.Logon.Password, p);

    const KERB_INTERACTIVE_LOGON* pkilIn = &kiul.Logon;
    DWORD cb = sizeof(kiul)
        + pkilIn->LogonDomainName.Length
        + pkilIn->UserName.Length
        + pkilIn->Password.Length;

    auto* out = (KERB_INTERACTIVE_UNLOCK_LOGON*)CoTaskMemAlloc(cb);
    if (!out) return E_OUTOFMEMORY;
    ZeroMemory(out, cb);
    out->Logon.MessageType = pkilIn->MessageType;
    ZeroMemory(&out->LogonId, sizeof(out->LogonId));

    BYTE* cursor = (BYTE*)out + sizeof(*out);

    auto append = [&](UNICODE_STRING* dest, const UNICODE_STRING* src)
    {
        dest->Length = src->Length;
        dest->MaximumLength = src->Length;
        dest->Buffer = (PWSTR)(cursor - (BYTE*)out);
        if (src->Length)
            CopyMemory(cursor, src->Buffer, src->Length);
        cursor += src->Length;
    };

    append(&out->Logon.LogonDomainName, &kiul.Logon.LogonDomainName);
    append(&out->Logon.UserName, &kiul.Logon.UserName);
    append(&out->Logon.Password, &kiul.Logon.Password);

    *prgb = (BYTE*)out;
    *pcb = cb;
    SecureZeroMemory(p, sizeof(p));
    return S_OK;
}

static HRESULT RetrieveNegotiateAuthPackage(ULONG* pul)
{
    HANDLE hLsa = nullptr;
    NTSTATUS n = LsaConnectUntrusted(&hLsa);
    if (n != 0) return HRESULT_FROM_NT(n);
    LSA_STRING name;
    name.Buffer = (PCHAR)"Negotiate";
    name.Length = 9;
    name.MaximumLength = 10;
    n = LsaLookupAuthenticationPackage(hLsa, &name, pul);
    LsaDeregisterLogonProcess(hLsa);
    return n == 0 ? S_OK : HRESULT_FROM_NT(n);
}

class UnlockCredential : public ICredentialProviderCredential2
{
public:
    UnlockCredential() : _ref(1), _cpus(CPUS_INVALID), _events(nullptr)
    {
        _sid[0] = 0;
        InterlockedIncrement(&g_dllRef);
    }
    ~UnlockCredential()
    {
        SafeRelease((IUnknown**)&_events);
        InterlockedDecrement(&g_dllRef);
    }

    HRESULT RuntimeClassInitialize(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus)
    {
        _cpus = cpus;
        return S_OK;
    }

    void SetSid(const wchar_t* sid)
    {
        StringCchCopyW(_sid, 128, sid ? sid : L"");
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (riid == IID_IUnknown || riid == IID_ICredentialProviderCredential ||
            riid == IID_ICredentialProviderCredential2)
        {
            *ppv = static_cast<ICredentialProviderCredential2*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release()
    {
        LONG c = InterlockedDecrement(&_ref);
        if (!c) delete this;
        return c;
    }

    IFACEMETHODIMP Advise(ICredentialProviderCredentialEvents* ev)
    {
        SafeRelease((IUnknown**)&_events);
        _events = ev;
        if (_events) _events->AddRef();
        return S_OK;
    }
    IFACEMETHODIMP UnAdvise()
    {
        SafeRelease((IUnknown**)&_events);
        return S_OK;
    }
    IFACEMETHODIMP SetSelected(BOOL* autoLogon)
    {
        *autoLogon = IsUnlockRequested() ? TRUE : FALSE;
        return S_OK;
    }
    IFACEMETHODIMP SetDeselected() { return S_OK; }
    IFACEMETHODIMP GetFieldState(DWORD fid, CREDENTIAL_PROVIDER_FIELD_STATE* pfs, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* pfis)
    {
        *pfis = CPFIS_NONE;
        if (fid == FID_TILE || fid == FID_TITLE)
            *pfs = CPFS_DISPLAY_IN_BOTH;
        else
            *pfs = CPFS_DISPLAY_IN_SELECTED_TILE;
        return S_OK;
    }
    IFACEMETHODIMP GetStringValue(DWORD fid, LPWSTR* ppsz)
    {
        const wchar_t* text = L"";
        if (fid == FID_TITLE) text = L"UnlockServer";
        else if (fid == FID_SUBTITLE) text = L"Unlock when a bound device is nearby";
        return SHStrDupW(text, ppsz);
    }
    IFACEMETHODIMP GetBitmapValue(DWORD fid, HBITMAP* phbmp)
    {
        if (!phbmp) return E_INVALIDARG;
        *phbmp = nullptr;
        if (fid != FID_TILE) return E_NOTIMPL;
        *phbmp = CreateTileBitmap();
        return *phbmp ? S_OK : E_OUTOFMEMORY;
    }
    IFACEMETHODIMP GetCheckboxValue(DWORD, BOOL*, LPWSTR*) { return E_NOTIMPL; }
    IFACEMETHODIMP GetSubmitButtonValue(DWORD, DWORD*) { return E_NOTIMPL; }
    IFACEMETHODIMP GetComboBoxValueCount(DWORD, DWORD*, DWORD*) { return E_NOTIMPL; }
    IFACEMETHODIMP GetComboBoxValueAt(DWORD, DWORD, LPWSTR*) { return E_NOTIMPL; }
    IFACEMETHODIMP SetStringValue(DWORD, LPCWSTR) { return E_NOTIMPL; }
    IFACEMETHODIMP SetCheckboxValue(DWORD, BOOL) { return E_NOTIMPL; }
    IFACEMETHODIMP SetComboBoxSelectedValue(DWORD, DWORD) { return E_NOTIMPL; }
    IFACEMETHODIMP CommandLinkClicked(DWORD) { return E_NOTIMPL; }

    IFACEMETHODIMP GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* pcpgsr,
        CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs,
        LPWSTR* ppszOptionalStatusText,
        CREDENTIAL_PROVIDER_STATUS_ICON* pcpsiOptionalStatusIcon)
    {
        *pcpgsr = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
        *ppszOptionalStatusText = nullptr;
        *pcpsiOptionalStatusIcon = CPSI_NONE;
        ZeroMemory(pcpcs, sizeof(*pcpcs));

        wchar_t domain[256] = {}, user[256] = {}, pass[256] = {}, fileSid[128] = {};
        if (!DpapiUnprotectFile(kCredPath, domain, user, pass, fileSid, 256))
        {
            ProvLog("GetSerialization: no saved credential");
            SHStrDupW(L"Windows password is not saved.", ppszOptionalStatusText);
            *pcpsiOptionalStatusIcon = CPSI_ERROR;
            return S_OK;
        }
        if (!_sid[0] && fileSid[0])
            StringCchCopyW(_sid, 128, fileSid);

        BYTE* rgb = nullptr;
        DWORD cb = 0;
        HRESULT hr = PackUnlockLogon(domain, user, pass, _cpus, &rgb, &cb);
        SecureZeroMemory(pass, sizeof(pass));
        if (FAILED(hr))
        {
            ProvLog("GetSerialization: pack failed");
            return hr;
        }

        ULONG authPkg = 0;
        hr = RetrieveNegotiateAuthPackage(&authPkg);
        if (FAILED(hr))
        {
            CoTaskMemFree(rgb);
            ProvLog("GetSerialization: auth package failed");
            return hr;
        }

        pcpcs->ulAuthenticationPackage = authPkg;
        pcpcs->cbSerialization = cb;
        pcpcs->rgbSerialization = rgb;
        pcpcs->clsidCredentialProvider = CLSID_UnlockServerProvider;
        *pcpgsr = CPGSR_RETURN_CREDENTIAL_FINISHED;
        DeleteFileW(kReqPath);
        ProvLog("GetSerialization: submitted");
        return S_OK;
    }

    IFACEMETHODIMP ReportResult(NTSTATUS status, NTSTATUS, LPWSTR* ppsz, CREDENTIAL_PROVIDER_STATUS_ICON* icon)
    {
        *ppsz = nullptr;
        *icon = CPSI_NONE;
        if (status != 0)
            ProvLog("ReportResult: logon failed");
        return S_OK;
    }

    IFACEMETHODIMP GetUserSid(LPWSTR* sid)
    {
        if (!_sid[0])
        {
            *sid = nullptr;
            return S_FALSE;
        }
        return SHStrDupW(_sid, sid);
    }

private:
    LONG _ref;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _cpus;
    ICredentialProviderCredentialEvents* _events;
    wchar_t _sid[128];
};

class UnlockProvider : public ICredentialProvider, public ICredentialProviderSetUserArray
{
public:
    UnlockProvider() : _ref(1), _cpus(CPUS_INVALID), _events(nullptr), _upAdvise(0),
        _cred(nullptr), _stop(nullptr), _thread(nullptr)
    {
        _userSid[0] = 0;
        InterlockedIncrement(&g_dllRef);
    }
    ~UnlockProvider()
    {
        StopWatcher();
        if (_cred) _cred->Release();
        SafeRelease((IUnknown**)&_events);
        InterlockedDecrement(&g_dllRef);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (riid == IID_IUnknown || riid == IID_ICredentialProvider)
            *ppv = static_cast<ICredentialProvider*>(this);
        else if (riid == IID_ICredentialProviderSetUserArray)
            *ppv = static_cast<ICredentialProviderSetUserArray*>(this);
        else
        {
            *ppv = nullptr;
            return E_NOINTERFACE;
        }
        AddRef();
        return S_OK;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release()
    {
        LONG c = InterlockedDecrement(&_ref);
        if (!c) delete this;
        return c;
    }

    IFACEMETHODIMP SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD)
    {
        if (cpus != CPUS_LOGON && cpus != CPUS_UNLOCK_WORKSTATION)
            return E_NOTIMPL;
        _cpus = cpus;
        if (_cred) { _cred->Release(); _cred = nullptr; }
        _cred = new (std::nothrow) UnlockCredential();
        if (!_cred) return E_OUTOFMEMORY;
        if (_userSid[0]) _cred->SetSid(_userSid);
        ProvLog(cpus == CPUS_UNLOCK_WORKSTATION ? "SetUsageScenario unlock" : "SetUsageScenario logon");
        return _cred->RuntimeClassInitialize(cpus);
    }
    IFACEMETHODIMP SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION*) { return E_NOTIMPL; }

    IFACEMETHODIMP Advise(ICredentialProviderEvents* ev, UINT_PTR up)
    {
        SafeRelease((IUnknown**)&_events);
        _events = ev;
        if (_events) _events->AddRef();
        _upAdvise = up;
        StartWatcher();
        return S_OK;
    }
    IFACEMETHODIMP UnAdvise()
    {
        StopWatcher();
        SafeRelease((IUnknown**)&_events);
        return S_OK;
    }

    IFACEMETHODIMP GetFieldDescriptorCount(DWORD* pdw) { *pdw = FID_COUNT; return S_OK; }
    IFACEMETHODIMP GetFieldDescriptorAt(DWORD i, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** ppcpfd)
    {
        if (i >= FID_COUNT || !ppcpfd) return E_INVALIDARG;
        auto* fd = (CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*)CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR));
        if (!fd) return E_OUTOFMEMORY;
        ZeroMemory(fd, sizeof(*fd));
        fd->dwFieldID = i;
        if (i == FID_TILE)
        {
            fd->cpft = CPFT_TILE_IMAGE;
            fd->guidFieldType = CPFG_CREDENTIAL_PROVIDER_LOGO;
            SHStrDupW(L"Image", &fd->pszLabel);
        }
        else if (i == FID_TITLE)
        {
            fd->cpft = CPFT_LARGE_TEXT;
            fd->guidFieldType = CPFG_CREDENTIAL_PROVIDER_LABEL;
            SHStrDupW(L"UnlockServer", &fd->pszLabel);
        }
        else
        {
            fd->cpft = CPFT_SMALL_TEXT;
            SHStrDupW(L"Status", &fd->pszLabel);
        }
        *ppcpfd = fd;
        return S_OK;
    }

    IFACEMETHODIMP GetCredentialCount(DWORD* pdwCount, DWORD* pdwDefault, BOOL* pbAutoLogonWithDefault)
    {
        *pdwCount = 1;
        *pdwDefault = 0;
        BOOL autoLogon = IsUnlockRequested() ? TRUE : FALSE;
        *pbAutoLogonWithDefault = autoLogon;
        if (autoLogon) ProvLog("GetCredentialCount auto-logon");
        return S_OK;
    }
    IFACEMETHODIMP GetCredentialAt(DWORD i, ICredentialProviderCredential** ppcpc)
    {
        if (i != 0 || !_cred) return E_INVALIDARG;
        return _cred->QueryInterface(IID_ICredentialProviderCredential, (void**)ppcpc);
    }

    IFACEMETHODIMP SetUserArray(ICredentialProviderUserArray* users)
    {
        _userSid[0] = 0;
        if (!users) return S_OK;
        DWORD count = 0;
        if (FAILED(users->GetCount(&count)) || count == 0) return S_OK;
        ICredentialProviderUser* user = nullptr;
        if (SUCCEEDED(users->GetAt(0, &user)) && user)
        {
            LPWSTR sid = nullptr;
            if (SUCCEEDED(user->GetSid(&sid)) && sid)
            {
                StringCchCopyW(_userSid, 128, sid);
                CoTaskMemFree(sid);
                ProvLog("SetUserArray got SID");
            }
            user->Release();
        }
        if (_cred && _userSid[0])
            _cred->SetSid(_userSid);
        return S_OK;
    }

private:
    void StartWatcher()
    {
        StopWatcher();
        _stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        _thread = CreateThread(nullptr, 0, WatcherThread, this, 0, nullptr);
    }
    void StopWatcher()
    {
        if (_stop) SetEvent(_stop);
        if (_thread)
        {
            WaitForSingleObject(_thread, 2000);
            CloseHandle(_thread);
            _thread = nullptr;
        }
        if (_stop) { CloseHandle(_stop); _stop = nullptr; }
    }

    static DWORD WINAPI WatcherThread(LPVOID param)
    {
        auto* self = (UnlockProvider*)param;
        HANDLE pulse = CreatePulseEvent();
        HANDLE waits[2] = { self->_stop, pulse };
        while (true)
        {
            DWORD w = WaitForMultipleObjects(pulse ? 2 : 1, waits, FALSE, 400);
            if (w == WAIT_OBJECT_0) break;
            if (self->_events && IsUnlockRequested())
            {
                ProvLog("CredentialsChanged");
                self->_events->CredentialsChanged(self->_upAdvise);
            }
        }
        if (pulse) CloseHandle(pulse);
        return 0;
    }

    LONG _ref;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _cpus;
    ICredentialProviderEvents* _events;
    UINT_PTR _upAdvise;
    UnlockCredential* _cred;
    HANDLE _stop;
    HANDLE _thread;
    wchar_t _userSid[128];
};

class ClassFactory : public IClassFactory
{
public:
    ClassFactory() : _ref(1) { InterlockedIncrement(&g_dllRef); }
    ~ClassFactory() { InterlockedDecrement(&g_dllRef); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release()
    {
        LONG c = InterlockedDecrement(&_ref);
        if (!c) delete this;
        return c;
    }
    IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv)
    {
        if (pUnkOuter) return CLASS_E_NOAGGREGATION;
        auto* p = new (std::nothrow) UnlockProvider();
        if (!p) return E_OUTOFMEMORY;
        HRESULT hr = p->QueryInterface(riid, ppv);
        p->Release();
        return hr;
    }
    IFACEMETHODIMP LockServer(BOOL f) { if (f) InterlockedIncrement(&g_dllRef); else InterlockedDecrement(&g_dllRef); return S_OK; }

private:
    LONG _ref;
};

BOOL APIENTRY DllMain(HMODULE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hModule = h;
        DisableThreadLibraryCalls(h);
        ProvLog("DllMain attach");
    }
    return TRUE;
}

STDAPI DllCanUnloadNow() { return g_dllRef > 0 ? S_FALSE : S_OK; }

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (rclsid != CLSID_UnlockServerProvider) return CLASS_E_CLASSNOTAVAILABLE;
    auto* f = new (std::nothrow) ClassFactory();
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

static HRESULT WriteReg(HKEY root, const wchar_t* sub, const wchar_t* name, const wchar_t* value)
{
    HKEY k = nullptr;
    LONG e = RegCreateKeyExW(root, sub, 0, nullptr, 0, KEY_WRITE, nullptr, &k, nullptr);
    if (e != ERROR_SUCCESS) return HRESULT_FROM_WIN32(e);
    e = RegSetValueExW(k, name, 0, REG_SZ, (const BYTE*)value, (DWORD)((wcslen(value) + 1) * sizeof(wchar_t)));
    RegCloseKey(k);
    return HRESULT_FROM_WIN32(e);
}

STDAPI DllRegisterServer()
{
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(g_hModule, path, MAX_PATH);
    HRESULT hr = WriteReg(HKEY_CLASSES_ROOT,
        L"CLSID\\{A31E8C27-9B14-4F6D-8E52-1C7A9D04E6B3}", nullptr, L"UnlockServer Credential Provider");
    if (SUCCEEDED(hr))
        hr = WriteReg(HKEY_CLASSES_ROOT,
            L"CLSID\\{A31E8C27-9B14-4F6D-8E52-1C7A9D04E6B3}\\InprocServer32", nullptr, path);
    if (SUCCEEDED(hr))
        hr = WriteReg(HKEY_CLASSES_ROOT,
            L"CLSID\\{A31E8C27-9B14-4F6D-8E52-1C7A9D04E6B3}\\InprocServer32", L"ThreadingModel", L"Apartment");
    if (SUCCEEDED(hr))
        hr = WriteReg(HKEY_LOCAL_MACHINE, kProviderKey, nullptr, L"UnlockServer");
    return hr;
}

STDAPI DllUnregisterServer()
{
    RegDeleteTreeW(HKEY_CLASSES_ROOT, L"CLSID\\{A31E8C27-9B14-4F6D-8E52-1C7A9D04E6B3}");
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, kProviderKey);
    return S_OK;
}
