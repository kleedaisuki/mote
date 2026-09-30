/// @file
/// Self-contained synthetic-window WGC/QPC ordering calibration; no screen pixels persist.

#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif
#ifndef WINVER
#define WINVER 0x0A00
#endif
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <dwmapi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <mutex>
#include <stdexcept>
#include <vector>
#include <string>

using namespace winrt;
namespace capture = winrt::Windows::Graphics::Capture;
namespace direct3d = winrt::Windows::Graphics::DirectX::Direct3D11;

namespace {
/// One deliberately painted color transition on the synthetic test window.
struct toggle {
    int color; ///< 0 means red, 1 means blue.
    int64_t before_qpc; ///< QPC before invalidating the window.
    int64_t after_paint_qpc; ///< QPC after synchronous WM_PAINT completes.
};

/// One WGC frame with its original metadata, arrival and one-pixel color.
struct frame_mark {
    int color; ///< -1 means the center pixel was neither synthetic color.
    int64_t system_100ns; ///< WGC compositor frame metadata.
    int64_t arrival_qpc; ///< QPC immediately after frame dequeue, not callback entry.
    int64_t processed_qpc; ///< QPC after the one-pixel GPU readback.
    int64_t metadata_before{}; ///< Same-thread bracket before reading frame metadata.
    int64_t metadata_after{}; ///< Same-thread bracket after reading frame metadata.
    int64_t timing_before{}; ///< Bracket before optional global DWM timing query.
    int64_t timing_after{}; ///< Bracket after optional global DWM timing query.
    DWM_TIMING_INFO timing{}; ///< Global composition cadence, not source-frame identity.
    HRESULT timing_result{E_NOTIMPL}; ///< Availability of the optional DWM query.
};

std::atomic<int> painted_color{0}; ///< UI thread's current synthetic color.

/// Read the same QPC clock used by WGC's documented frame timestamp.
int64_t qpc()
{
    LARGE_INTEGER value{};
    if (!QueryPerformanceCounter(&value)) throw std::runtime_error("QPC unavailable");
    return value.QuadPart;
}

/// Paint only a solid known color; this window cannot contain user content.
LRESULT CALLBACK window_proc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp)
{
    if (message == WM_PAINT) {
        PAINTSTRUCT paint{};
        auto dc = BeginPaint(hwnd, &paint);
        auto brush = CreateSolidBrush(painted_color.load() ? RGB(0, 0, 255) : RGB(255, 0, 0));
        FillRect(dc, &paint.rcPaint, brush);
        DeleteObject(brush);
        EndPaint(hwnd, &paint);
        return 0;
    }
    return DefWindowProcW(hwnd, message, wp, lp);
}

/// Create a BGRA D3D11 device and identify software-rendering fallback.
com_ptr<ID3D11Device> make_device(bool& warp)
{
    com_ptr<ID3D11Device> device;
    D3D_FEATURE_LEVEL level{};
    auto const flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    auto hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        flags, nullptr, 0, D3D11_SDK_VERSION, device.put(), &level, nullptr);
    if (FAILED(hr)) {
        warp = true;
        check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr,
            flags, nullptr, 0, D3D11_SDK_VERSION, device.put(), &level, nullptr));
    }
    return device;
}

/// Project the DXGI device into a WinRT capture-frame-pool device.
direct3d::IDirect3DDevice project_device(ID3D11Device* device)
{
    com_ptr<IDXGIDevice> dxgi;
    check_hresult(device->QueryInterface(__uuidof(IDXGIDevice), dxgi.put_void()));
    com_ptr<IInspectable> inspectable;
    check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
    return inspectable.as<direct3d::IDirect3DDevice>();
}

/// Construct a WGC item bound to only our newly created HWND.
capture::GraphicsCaptureItem capture_item(HWND hwnd)
{
    auto factory = get_activation_factory<capture::GraphicsCaptureItem>();
    auto interop = factory.as<IGraphicsCaptureItemInterop>();
    capture::GraphicsCaptureItem item{nullptr};
    check_hresult(interop->CreateForWindow(hwnd,
        guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
        reinterpret_cast<void**>(put_abi(item))));
    return item;
}

/// Drain GUI messages while waiting for asynchronous WGC frames.
void pump_for(DWORD milliseconds)
{
    auto const until = GetTickCount64() + milliseconds;
    while (GetTickCount64() < until) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        Sleep(2);
    }
}

/// Capture only one center pixel per frame while retaining the OS timestamps.
class clock_capture {
public:
    /// Start capture on the synthetic window, not the desktop or mote.
    explicit clock_capture(HWND hwnd, bool extended = false, DWORD readback_delay = 0)
        : extended_(extended), readback_delay_(readback_delay)
    {
        if (!capture::GraphicsCaptureSession::IsSupported())
            throw std::runtime_error("WGC unsupported");
        device_ = make_device(warp_);
        device_->GetImmediateContext(context_.put());
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = desc.Height = desc.MipLevels = desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_STAGING;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        check_hresult(device_->CreateTexture2D(&desc, nullptr, pixel_.put()));
        item_ = capture_item(hwnd);
        auto const size = item_.Size();
        pool_ = capture::Direct3D11CaptureFramePool::CreateFreeThreaded(
            project_device(device_.get()),
            winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,
            3, size);
        session_ = pool_.CreateCaptureSession(item_);
        token_ = pool_.FrameArrived([this](auto const& pool, auto const&) {
            this->on_frame(pool);
        });
        session_.StartCapture();
    }

    /// Revoke callback and wait for any in-flight readback before destruction.
    ~clock_capture()
    {
        stop();
    }

    /// Stop capture while its HWND is still alive; safe to call again on destruction.
    void stop()
    {
        if (pool_) {
            if (extended_) std::fprintf(stderr, "STAGE,capture-stop\n");
            stopping_.store(true);
            pool_.FrameArrived(token_);
            // Let already-running readback finish, but never hold this lock
            // across Close: WinRT may wait for an event worker to return.
            { std::lock_guard lock(callback_mutex_); }
            session_.Close();
            pool_.Close();
            pool_ = nullptr;
            if (extended_) std::fprintf(stderr, "STAGE,capture-stopped\n");
        }
    }

    /// Return metadata, never the captured pixel itself.
    std::vector<frame_mark> marks()
    {
        std::lock_guard lock(mutex_);
        return marks_;
    }

    /// Report hardware or WARP driver choice without using it as a latency result.
    bool warp() const { return warp_; }
    /// Count local vector capacity overrun, not undocumented WGC pool drops.
    unsigned overflow() const { return overflow_.load(); }
    /// Count GPU/capture callback errors.
    unsigned errors() const { return errors_.load(); }

private:
    /// Process bounded one-pixel frames on the free-threaded pool worker.
    void on_frame(capture::Direct3D11CaptureFramePool const& pool) noexcept
    {
        std::lock_guard callback_lock(callback_mutex_);
        if (stopping_.load()) return;
        try {
            // A delayed reader can perpetually refill the pool. Bound each callback
            // to its configured capacity rather than starving teardown and the UI.
            for (unsigned drained = 0; drained < 3; ++drained) {
                if (stopping_.load()) break;
                auto frame = pool.TryGetNextFrame();
                if (!frame) break;
                frame_mark mark{};
                mark.color = -1;
                mark.arrival_qpc = qpc();
                mark.metadata_before = qpc();
                mark.system_100ns = frame.SystemRelativeTime().count();
                mark.metadata_after = qpc();
                // A delayed observer must not change an already acquired frame's timestamp.
                if (readback_delay_) Sleep(readback_delay_);
                auto access = frame.Surface().as<
                    ::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
                com_ptr<ID3D11Texture2D> source;
                check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), source.put_void()));
                auto const size = frame.ContentSize();
                if (size.Width < 32 || size.Height < 32)
                    throw std::runtime_error("synthetic WGC frame too small");
                auto const x = static_cast<UINT>(size.Width / 2);
                auto const y = static_cast<UINT>(size.Height / 2);
                D3D11_BOX box{x, y, 0, x + 1, y + 1, 1};
                context_->CopySubresourceRegion(pixel_.get(), 0, 0, 0, 0,
                    source.get(), 0, &box);
                D3D11_MAPPED_SUBRESOURCE mapped{};
                check_hresult(context_->Map(pixel_.get(), 0, D3D11_MAP_READ, 0, &mapped));
                auto const* bgra = static_cast<std::uint8_t const*>(mapped.pData);
                if (bgra[2] > 200 && bgra[0] < 70) mark.color = 0;
                if (bgra[0] > 200 && bgra[2] < 70) mark.color = 1;
                context_->Unmap(pixel_.get(), 0);
                mark.processed_qpc = qpc();
                if (extended_) {
                    mark.timing.cbSize = sizeof(mark.timing);
                    mark.timing_before = qpc();
                    mark.timing_result = DwmGetCompositionTimingInfo(nullptr, &mark.timing);
                    mark.timing_after = qpc();
                }
                std::lock_guard lock(mutex_);
                if (marks_.size() == 128) ++overflow_;
                else marks_.push_back(mark);
            }
        } catch (...) { ++errors_; }
    }

    bool warp_{}; ///< Software D3D fallback, if required.
    bool extended_{}; ///< Include optional numeric DWM metadata for clock diagnosis.
    DWORD readback_delay_{}; ///< Controlled observer delay after metadata acquisition.
    std::atomic<bool> stopping_{false}; ///< Reject new callback work before closing pool resources.
    com_ptr<ID3D11Device> device_; ///< Capture device.
    com_ptr<ID3D11DeviceContext> context_; ///< GPU copy context.
    com_ptr<ID3D11Texture2D> pixel_; ///< Reused one-pixel staging texture.
    capture::GraphicsCaptureItem item_{nullptr}; ///< Synthetic exact HWND.
    capture::Direct3D11CaptureFramePool pool_{nullptr}; ///< Bounded WGC pool.
    capture::GraphicsCaptureSession session_{nullptr}; ///< Active capture.
    event_token token_{}; ///< Callback registration.
    std::mutex callback_mutex_; ///< Guards in-flight callback teardown.
    std::mutex mutex_; ///< Protects metadata vector.
    std::vector<frame_mark> marks_; ///< Bounded in-memory numeric marks.
    std::atomic<unsigned> overflow_{0}; ///< Local vector overflow, not pool loss.
    std::atomic<unsigned> errors_{0}; ///< Capture/copy failures.
};
} // namespace

/// Paint six deterministic color flips and print QPC/WGC ordering diagnostics.
int wmain(int argc, wchar_t** argv)
{
    HWND hwnd{};
    try {
        bool extended{};
        bool same_cpu{};
        DWORD readback_delay{};
        for (int i = 1; i < argc; ++i) {
            std::wstring arg{argv[i]};
            if (arg == L"--timestamp-order") extended = true;
            else if (arg == L"--same-cpu") same_cpu = true;
            else if (arg == L"--delay-readback-20ms") readback_delay = 20;
            else throw std::runtime_error("unknown calibration argument");
        }
        if (same_cpu) {
            DWORD_PTR process_mask{}, system_mask{};
            if (!GetProcessAffinityMask(GetCurrentProcess(), &process_mask, &system_mask) ||
                !SetProcessAffinityMask(GetCurrentProcess(), process_mask & (~process_mask + 1)))
                throw std::runtime_error("same-CPU process affinity unavailable");
        }
        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            throw std::runtime_error("PMv2 DPI context unavailable");
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        WNDCLASSW cls{};
        cls.lpfnWndProc = window_proc;
        cls.hInstance = GetModuleHandleW(nullptr);
        cls.lpszClassName = L"MoteSyntheticWgcClockProbe";
        if (!RegisterClassW(&cls)) throw std::runtime_error("window class registration failed");
        hwnd = CreateWindowExW(0, cls.lpszClassName, L"Synthetic WGC clock probe",
            WS_OVERLAPPEDWINDOW | WS_VISIBLE, 180, 180, 320, 240,
            nullptr, nullptr, cls.hInstance, nullptr);
        if (!hwnd) throw std::runtime_error("synthetic window creation failed");
        UpdateWindow(hwnd);
        LARGE_INTEGER frequency{};
        if (!QueryPerformanceFrequency(&frequency)) throw std::runtime_error("QPC frequency unavailable");
        clock_capture capture(hwnd, extended, readback_delay);
        if (extended) std::fprintf(stderr, "STAGE,capture-started\n");
        pump_for(250);
        std::vector<toggle> toggles;
        for (int i = 0; i < 6; ++i) {
            auto const color = (i + 1) & 1;
            auto const before = qpc();
            painted_color.store(color);
            InvalidateRect(hwnd, nullptr, FALSE);
            UpdateWindow(hwnd);
            auto const after = qpc();
            toggles.push_back({color, before, after});
            pump_for(140);
            if (extended) std::fprintf(stderr, "STAGE,toggle-%d\n", i);
        }
        if (extended) std::fprintf(stderr, "STAGE,read-marks\n");
        auto frames = capture.marks();
        if (extended) std::fprintf(stderr, "STAGE,marks-read\n");
        std::printf("META,%lld,%d,%u,%u,%zu\n",
            static_cast<long long>(frequency.QuadPart), capture.warp() ? 1 : 0,
            capture.overflow(), capture.errors(), frames.size());
        for (size_t i = 0; i < toggles.size(); ++i) {
            auto const& mark = toggles[i];
            std::printf("TOGGLE,%zu,%d,%lld,%lld\n", i, mark.color,
                static_cast<long long>(mark.before_qpc),
                static_cast<long long>(mark.after_paint_qpc));
        }
        for (size_t i = 0; i < frames.size(); ++i) {
            auto const& mark = frames[i];
            std::printf("FRAME,%zu,%d,%lld,%lld,%lld\n", i, mark.color,
                static_cast<long long>(mark.system_100ns),
                static_cast<long long>(mark.arrival_qpc),
                static_cast<long long>(mark.processed_qpc));
            if (extended) {
                std::printf("ORDER,%zu,%lld,%lld,%lld,%lld,%08x,%llu,%llu,%llu,%llu,%llu\n",
                    i, static_cast<long long>(mark.metadata_before),
                    static_cast<long long>(mark.metadata_after),
                    static_cast<long long>(mark.timing_before),
                    static_cast<long long>(mark.timing_after),
                    static_cast<unsigned>(mark.timing_result),
                    mark.timing.qpcCompose, mark.timing.qpcVBlank,
                    mark.timing.qpcFrameComplete, mark.timing.qpcFrameDisplayed,
                    mark.timing.qpcRefreshPeriod);
            }
        }
        std::fflush(stdout);
        capture.stop();
        DestroyWindow(hwnd);
        if (extended) std::fprintf(stderr, "STAGE,window-destroyed\n");
        return capture.errors() || capture.overflow() ? 5 : 0;
    } catch (winrt::hresult_error const& e) {
        std::fprintf(stderr, "WGC HRESULT 0x%08x\n", static_cast<unsigned>(e.code()));
    } catch (std::exception const& e) {
        std::fprintf(stderr, "WGC clock error: %s\n", e.what());
    }
    if (hwnd) DestroyWindow(hwnd);
    return 4;
}
