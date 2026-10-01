/// @file
/// Exact-HWND synthetic mote glyph observer. Pixels stay in memory; output is metadata only.

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

#include <array>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cwchar>
#include <exception>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

using namespace winrt;
namespace capture = winrt::Windows::Graphics::Capture;
namespace direct3d = winrt::Windows::Graphics::DirectX::Direct3D11;

namespace {
constexpr int width = 220;
constexpr int height = 24;
constexpr size_t frame_limit = 256;
constexpr UINT timeout_ms = 3000;
constexpr UINT wm_null = 0x0000;
constexpr UINT wm_char = 0x0102;
constexpr UINT em_setsel = 0x00B1;
constexpr unsigned identity_flag = 1;
constexpr unsigned visible_flag = 2;
constexpr unsigned focus_flag = 4;
constexpr unsigned foreground_flag = 8;
constexpr unsigned geometry_flag = 16;
constexpr unsigned required_flags = identity_flag | visible_flag | focus_flag |
    foreground_flag | geometry_flag;

/// Immutable expected target geometry and ownership for one capture session.
struct target_contract {
    HWND main; ///< Exact top-level HWND.
    HWND canvas; ///< Exact native canvas child.
    HWND input; ///< Focused RichEdit input child, or null for oracle snapshots.
    RECT bounds; ///< DWM extended frame bounds in physical pixels.
    RECT canvas_client; ///< Canvas client size at session start.
    POINT canvas_origin; ///< Canvas client origin in physical screen pixels.
};

/// Returns independent target-validity bits without hiding failed predicates.
unsigned inspect_target(target_contract const& contract) noexcept;

/// A captured ROI's operating-system timestamp and non-reversible glyph signature.
struct sample {
    int64_t system_100ns; ///< WGC compositor metadata in 100-ns units.
    int64_t arrival_qpc; ///< External callback entry QPC value.
    int64_t readback_ticks; ///< Entire observer-side ROI processing interval.
    uint64_t hash; ///< Non-reversible signature of synthetic glyph pixels.
    int ink; ///< Pixels contrasted with the modal ROI background color.
    unsigned target_flags; ///< Exact HWND/focus/foreground/geometry state at callback.
    std::array<uint32_t, width * height> pixels; ///< Bounded in-memory oracle data.
};

/// Returns a QPC reading, failing instead of silently substituting wall time.
int64_t qpc()
{
    LARGE_INTEGER value{};
    if (!QueryPerformanceCounter(&value)) throw std::runtime_error("QPC failed");
    return value.QuadPart;
}

/// Creates a hardware BGRA D3D11 device, falling back to WARP only if needed.
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

/// Converts a D3D11 DXGI device into the WinRT WGC device interface.
direct3d::IDirect3DDevice project_device(ID3D11Device* device)
{
    com_ptr<IDXGIDevice> dxgi;
    check_hresult(device->QueryInterface(__uuidof(IDXGIDevice), dxgi.put_void()));
    com_ptr<IInspectable> inspectable;
    check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
    return inspectable.as<direct3d::IDirect3DDevice>();
}

/// Creates a capture item for only the caller-supplied exact target window.
capture::GraphicsCaptureItem capture_item(HWND main)
{
    auto factory = get_activation_factory<capture::GraphicsCaptureItem>();
    auto interop = factory.as<IGraphicsCaptureItemInterop>();
    capture::GraphicsCaptureItem item{nullptr};
    check_hresult(interop->CreateForWindow(main,
        guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
        reinterpret_cast<void**>(put_abi(item))));
    return item;
}

/// Sends a bounded synchronous test input to the exact native input HWND.
void bounded_message(HWND target, UINT message, WPARAM wparam, LPARAM lparam)
{
    DWORD_PTR result{};
    if (!SendMessageTimeoutW(target, message, wparam, lparam,
            SMTO_ABORTIFHUNG | SMTO_BLOCK, timeout_ms, &result)) {
        throw std::runtime_error("bounded SendMessageTimeoutW failed");
    }
}

/// Owns one finite exact-window WGC session and a bounded in-memory ROI trace.
class observer {
public:
    /// Attach to a verified top-level mote HWND and its canvas child.
    observer(target_contract const& target) : target_(target)
    {
        if (!capture::GraphicsCaptureSession::IsSupported())
            throw std::runtime_error("WGC unsupported in this desktop session");
        RECT bounds{};
        if (FAILED(DwmGetWindowAttribute(target.main, DWMWA_EXTENDED_FRAME_BOUNDS,
                &bounds, sizeof(bounds))))
            throw std::runtime_error("DWM frame bounds unavailable");
        POINT origin{0, 0};
        if (!ClientToScreen(target.canvas, &origin))
            throw std::runtime_error("canvas client geometry unavailable");
        // Keep the blinking insertion caret out of the first-row signature;
        // shifted neighboring glyphs still make an X-prefix edit distinctive.
        crop_x_ = origin.x - bounds.left + 62;
        crop_y_ = origin.y - bounds.top + 4;
        bool warp = false;
        device_ = make_device(warp);
        warp_ = warp;
        device_->GetImmediateContext(context_.put());
        item_ = capture_item(target.main);
        auto const size = item_.Size();
        capture_size_ = size;
        if (crop_x_ < 0 || crop_y_ < 0 ||
            crop_x_ + width > size.Width || crop_y_ + height > size.Height)
            throw std::runtime_error("synthetic glyph ROI is outside captured window: crop=" +
                std::to_string(crop_x_) + "," + std::to_string(crop_y_) +
                " capture=" + std::to_string(size.Width) + "x" + std::to_string(size.Height));
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = width;
        desc.Height = height;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_STAGING;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        check_hresult(device_->CreateTexture2D(&desc, nullptr, staging_.put()));
        pool_ = capture::Direct3D11CaptureFramePool::CreateFreeThreaded(
            project_device(device_.get()),
            winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,
            3, size);
        session_ = pool_.CreateCaptureSession(item_);
        token_ = pool_.FrameArrived([this](auto const& sender, auto const&) {
            this->on_frame(sender);
        });
        session_.StartCapture();
    }

    /// Close capture before destroying staging resources used by callbacks.
    ~observer()
    {
        if (pool_) {
            pool_.FrameArrived(token_);
            session_.Close();
            pool_.Close();
            // The frame pool invokes callbacks on its worker thread. Wait for
            // an already-entered callback before releasing D3D resources.
            std::lock_guard lock(callback_mutex_);
        }
    }

    /// Wait for one initial frame, up to a bounded interval.
    bool wait_initial(DWORD milliseconds)
    {
        auto const stop = GetTickCount64() + milliseconds;
        while (GetTickCount64() < stop) {
            if (!snapshot().empty()) return true;
            Sleep(10);
        }
        return !snapshot().empty();
    }

    /// Copy metadata and hashes; never exposes captured pixels.
    std::vector<sample> snapshot()
    {
        std::lock_guard lock(mutex_);
        return samples_;
    }

    /// Count evidence of callback or buffer saturation.
    unsigned overflow() const { return overflow_.load(); }
    /// Count capture/readback errors that invalidate the result.
    unsigned errors() const { return errors_.load(); }
    /// Whether the machine used software WARP rather than its hardware device.
    bool warp() const { return warp_; }
    /// Fixed source-row crop X coordinate inside the captured window.
    int crop_x() const { return crop_x_; }
    /// Fixed source-row crop Y coordinate inside the captured window.
    int crop_y() const { return crop_y_; }

private:
    /// Drain available frames promptly; errors are numeric and do not escape a WinRT callback.
    void on_frame(capture::Direct3D11CaptureFramePool const& sender) noexcept
    {
        std::lock_guard callback_lock(callback_mutex_);
        try {
            while (auto frame = sender.TryGetNextFrame()) {
                auto const arrival = qpc();
                auto const stamp = frame.SystemRelativeTime().count();
                auto flags = inspect_target(target_);
                auto const frame_size = frame.ContentSize();
                if (frame_size.Width != capture_size_.Width ||
                    frame_size.Height != capture_size_.Height)
                    flags &= ~geometry_flag;
                auto texture_access = frame.Surface().as<
                    ::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
                com_ptr<ID3D11Texture2D> texture;
                check_hresult(texture_access->GetInterface(
                    __uuidof(ID3D11Texture2D), texture.put_void()));
                D3D11_BOX box{static_cast<UINT>(crop_x_), static_cast<UINT>(crop_y_), 0,
                    static_cast<UINT>(crop_x_ + width),
                    static_cast<UINT>(crop_y_ + height), 1};
                context_->CopySubresourceRegion(staging_.get(), 0, 0, 0, 0,
                    texture.get(), 0, &box);
                D3D11_MAPPED_SUBRESOURCE mapped{};
                check_hresult(context_->Map(staging_.get(), 0, D3D11_MAP_READ, 0, &mapped));
                sample next{stamp, arrival, 0, 14695981039346656037ull, 0,
                    flags, {}};
                auto const* first = static_cast<std::uint8_t const*>(mapped.pData);
                for (int y = 0; y < height; ++y) {
                    auto const* row = first + static_cast<size_t>(y) * mapped.RowPitch;
                    for (int x = 0; x < width; ++x) {
                        auto const* p = row + x * 4;
                        next.pixels[static_cast<size_t>(y) * width + x] =
                            static_cast<uint32_t>(p[0]) |
                            (static_cast<uint32_t>(p[1]) << 8) |
                            (static_cast<uint32_t>(p[2]) << 16);
                        for (int channel = 0; channel < 3; ++channel) {
                            next.hash ^= p[channel];
                            next.hash *= 1099511628211ull;
                        }
                    }
                }
                context_->Unmap(staging_.get(), 0);
                // The first pixel may itself be a glyph. Estimate the true
                // background from the modal 4-bit-per-channel color bucket.
                std::array<int, 4096> histogram{};
                for (auto const color : next.pixels) {
                    auto const bin = ((color >> 12) & 0xF00) |
                        ((color >> 8) & 0x0F0) | ((color >> 4) & 0x00F);
                    ++histogram[bin];
                }
                int mode = 0;
                for (int bin = 1; bin < 4096; ++bin)
                    if (histogram[bin] > histogram[mode]) mode = bin;
                auto const bg_b = ((mode & 15) << 4) + 8;
                auto const bg_g = (((mode >> 4) & 15) << 4) + 8;
                auto const bg_r = (((mode >> 8) & 15) << 4) + 8;
                for (auto const color : next.pixels) {
                    if (abs(int(color & 255) - bg_b) +
                        abs(int((color >> 8) & 255) - bg_g) +
                        abs(int((color >> 16) & 255) - bg_r) >= 72) ++next.ink;
                }
                next.readback_ticks = qpc() - arrival;
                std::lock_guard lock(mutex_);
                if (samples_.size() == frame_limit) ++overflow_;
                else samples_.push_back(next);
            }
        } catch (...) { ++errors_; }
    }

    int crop_x_{}; ///< Physical-pixel ROI offset in the WGC window texture.
    int crop_y_{}; ///< Physical-pixel ROI offset in the WGC window texture.
    target_contract target_; ///< Exact target identity and fixed geometry contract.
    bool warp_{}; ///< Whether hardware D3D device creation failed.
    com_ptr<ID3D11Device> device_; ///< Capture GPU device.
    com_ptr<ID3D11DeviceContext> context_; ///< ROI copy/readback context.
    com_ptr<ID3D11Texture2D> staging_; ///< Reused tiny CPU-readable ROI texture.
    capture::GraphicsCaptureItem item_{nullptr}; ///< Exact target HWND item.
    winrt::Windows::Graphics::SizeInt32 capture_size_{}; ///< Fixed frame geometry.
    capture::Direct3D11CaptureFramePool pool_{nullptr}; ///< Three-buffer frame queue.
    capture::GraphicsCaptureSession session_{nullptr}; ///< Active exact-HWND session.
    event_token token_{}; ///< Frame callback subscription to revoke at teardown.
    std::mutex callback_mutex_; ///< Prevent resource teardown during callbacks.
    std::mutex mutex_; ///< Protects bounded sample vector snapshots.
    std::vector<sample> samples_; ///< Synthetic ROI metadata and in-memory pixels.
    std::atomic<unsigned> overflow_{0}; ///< Local sample-vector capacity overruns.
    std::atomic<unsigned> errors_{0}; ///< Callback D3D/WinRT processing failures.
};

/// Count materially changed source-row pixels without exporting their values.
int different_pixels(sample const& reference, sample const& frame)
{
    int changed = 0;
    for (size_t i = 0; i < frame.pixels.size(); ++i) {
        auto const a = reference.pixels[i];
        auto const b = frame.pixels[i];
        auto const difference = abs(int(a & 255) - int(b & 255)) +
            abs(int((a >> 8) & 255) - int((b >> 8) & 255)) +
            abs(int((a >> 16) & 255) - int((b >> 16) & 255));
        if (difference >= 24) ++changed;
    }
    return changed;
}

/// Print one metadata-only record, comparing pixels in memory to the baseline.
void print_frame(char const* phase, sample const& frame, sample const& baseline)
{
    std::printf("FRAME,%s,%lld,%lld,%lld,%016llx,%d,%d,%u\n", phase,
        static_cast<long long>(frame.system_100ns),
        static_cast<long long>(frame.arrival_qpc),
        static_cast<long long>(frame.readback_ticks),
        static_cast<unsigned long long>(frame.hash), frame.ink,
        different_pixels(baseline, frame), frame.target_flags);
}

/// Parse a decimal HWND; accepting hexadecimal would make logs ambiguous.
HWND parse_hwnd(wchar_t const* arg)
{
    wchar_t* end{};
    auto const value = std::wcstoull(arg, &end, 10);
    if (!value || *end) throw std::runtime_error("invalid decimal HWND");
    auto hwnd = reinterpret_cast<HWND>(static_cast<UINT_PTR>(value));
    if (!IsWindow(hwnd)) throw std::runtime_error("HWND no longer exists");
    return hwnd;
}

/// Refuse capture of arbitrary desktop windows or mismatched child processes.
void verify_target(HWND main, HWND canvas, HWND input)
{
    wchar_t main_class[64]{};
    wchar_t canvas_class[64]{};
    wchar_t title[512]{};
    if (!GetClassNameW(main, main_class, 64) ||
        wcscmp(main_class, L"MoteNativeEditorWindow") != 0 ||
        !GetClassNameW(canvas, canvas_class, 64) ||
        wcscmp(canvas_class, L"MoteInteractiveCanvas") != 0)
        throw std::runtime_error("exact mote HWND classes required");
    if (!GetWindowTextW(main, title, 512) || !wcsstr(title, L"synthetic.txt"))
        throw std::runtime_error("synthetic.txt test window title required");
    DWORD main_pid{}, canvas_pid{}, input_pid{};
    GetWindowThreadProcessId(main, &main_pid);
    GetWindowThreadProcessId(canvas, &canvas_pid);
    if (!main_pid || main_pid != canvas_pid || !IsChild(main, canvas))
        throw std::runtime_error("mote HWND ownership invalid");
    if (input) {
        GetWindowThreadProcessId(input, &input_pid);
        if (input_pid != main_pid || !IsChild(canvas, input))
            throw std::runtime_error("input HWND ownership invalid");
    }
}

/// Snapshot exact target geometry once before capture begins.
target_contract make_contract(HWND main, HWND canvas, HWND input)
{
    verify_target(main, canvas, input);
    target_contract target{main, canvas, input, {}, {}, {0, 0}};
    if (FAILED(DwmGetWindowAttribute(main, DWMWA_EXTENDED_FRAME_BOUNDS,
            &target.bounds, sizeof(target.bounds))) ||
        !GetClientRect(canvas, &target.canvas_client) ||
        !ClientToScreen(canvas, &target.canvas_origin))
        throw std::runtime_error("initial target geometry unavailable");
    return target;
}

/// Compare Win32 rectangles without depending on padding in RECT.
bool same_rect(RECT const& a, RECT const& b)
{
    return a.left == b.left && a.top == b.top &&
        a.right == b.right && a.bottom == b.bottom;
}

/// Revalidate PID, focus, foreground, visibility and physical geometry.
unsigned inspect_target(target_contract const& target) noexcept
{
    try { verify_target(target.main, target.canvas, target.input); }
    catch (...) { return 0; }
    unsigned flags = identity_flag;
    if (IsWindowVisible(target.main) && IsWindowVisible(target.canvas) &&
        (!target.input || IsWindowVisible(target.input)) && !IsIconic(target.main))
        flags |= visible_flag;
    if (GetForegroundWindow() == target.main) flags |= foreground_flag;
    if (!target.input) flags |= focus_flag;
    else {
        auto const thread = GetWindowThreadProcessId(target.main, nullptr);
        GUITHREADINFO gui{sizeof(GUITHREADINFO)};
        if (thread && GetGUIThreadInfo(thread, &gui) && gui.hwndFocus == target.input)
            flags |= focus_flag;
    }
    RECT bounds{}, canvas_client{};
    POINT origin{0, 0};
    if (SUCCEEDED(DwmGetWindowAttribute(target.main, DWMWA_EXTENDED_FRAME_BOUNDS,
            &bounds, sizeof(bounds))) &&
        GetClientRect(target.canvas, &canvas_client) &&
        ClientToScreen(target.canvas, &origin) &&
        same_rect(bounds, target.bounds) &&
        same_rect(canvas_client, target.canvas_client) &&
        origin.x == target.canvas_origin.x && origin.y == target.canvas_origin.y)
        flags |= geometry_flag;
    return flags;
}
} // namespace

/// Capture the ordinary Continuous first-row glyph before/after one synthetic edit.
int wmain(int argc, wchar_t** argv)
{
    if (argc < 4 || argc > 5) {
        std::fprintf(stderr, "usage: WgcEditObserver.exe edit|snapshot <main-hwnd> <canvas-hwnd> [input-hwnd]\n");
        return 2;
    }
    try {
        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            throw std::runtime_error("per-monitor-v2 DPI awareness unavailable");
        auto const mode = std::wstring(argv[1]);
        if ((mode == L"edit" && argc != 5) ||
            (mode == L"snapshot" && argc != 4) ||
            (mode != L"edit" && mode != L"snapshot"))
            throw std::runtime_error("edit requires input HWND; snapshot does not");
        auto main = parse_hwnd(argv[2]);
        auto canvas = parse_hwnd(argv[3]);
        HWND input{};
        if (mode == L"edit") {
            input = parse_hwnd(argv[4]);
        }
        verify_target(main, canvas, input);
        if (mode == L"edit") {
            bounded_message(input, em_setsel, 0, 0);
            Sleep(350);
        }
        LARGE_INTEGER frequency{};
        if (!QueryPerformanceFrequency(&frequency))
            throw std::runtime_error("QPC frequency unavailable");
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        auto const target = make_contract(main, canvas, input);
        observer capture(target);
        if (!capture.wait_initial(3000)) throw std::runtime_error("no initial WGC frame");
        Sleep(180);
        auto before = capture.snapshot();
        auto const baseline = before.back();
        std::printf("META,%s,%lld,%d,%d,%d,%u,%u,%u\n", mode == L"edit" ? "edit" : "snapshot",
            static_cast<long long>(frequency.QuadPart), capture.crop_x(), capture.crop_y(),
            capture.warp() ? 1 : 0, capture.overflow(), capture.errors(), required_flags);
        print_frame("baseline", baseline, baseline);
        if (mode == L"edit") {
            bounded_message(input, wm_null, 0, 0);
            Sleep(300);
            auto const controlled = capture.snapshot();
            for (size_t i = before.size(); i < controlled.size(); ++i)
                print_frame("control", controlled[i], baseline);
            auto const pre_flags = inspect_target(target);
            auto const dispatch = qpc();
            bounded_message(input, wm_char, L'X', 0);
            auto const ack = qpc();
            Sleep(1800);
            auto const timed = capture.snapshot();
            auto const post_flags = inspect_target(target);
            auto const post_qpc = qpc();
            std::printf("STATE,pre,%u,%lld\n", pre_flags,
                static_cast<long long>(dispatch));
            std::printf("STATE,post,%u,%lld\n", post_flags,
                static_cast<long long>(post_qpc));
            std::printf("EDIT,%lld,%lld,%zu,%u,%u\n",
                static_cast<long long>(dispatch), static_cast<long long>(ack),
                controlled.size(), capture.overflow(), capture.errors());
            for (size_t i = controlled.size(); i < timed.size(); ++i)
                print_frame("timed", timed[i], baseline);
        }
        std::fflush(stdout);
        return capture.errors() || capture.overflow() ? 5 : 0;
    } catch (winrt::hresult_error const& e) {
        std::fprintf(stderr, "WGC HRESULT 0x%08x\n", static_cast<unsigned>(e.code()));
        return 4;
    } catch (std::exception const& e) {
        std::fprintf(stderr, "WGC error: %s\n", e.what());
        return 4;
    }
}
