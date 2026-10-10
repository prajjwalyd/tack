//! Tells when the pointer reaches the top edge, without anyone looking: one
//! thin strip window along the top of each monitor, in the middle third of
//! it, near enough invisible (alpha 1, which Windows still hit-tests). The
//! strip is sent a mouse move only when the pointer is on it, and an OLE drag
//! over it is a drop target's call. A thread of its own sleeps in
//! `GetMessageW` between those: 0 wake-ups per second.
//!
//! - The pointer arrives: the first mouse move on a strip. The strip turns
//!   click-through (`WS_EX_TRANSPARENT`), so a click on the top pixel row goes
//!   to the app below, and a 25 ms timer, which runs only from then, watches
//!   for the pointer leaving.
//! - A button down on arrival may be a drag on its way: the strip then stays
//!   hit-testable, or no drag could enter it, until the button is up.
//! - A drag arrives: the strip is a drop target that accepts nothing and
//!   reports [`EdgeEvent::DragEntered`] and [`EdgeEvent::DragLeft`].
//!
//! Only the middle third: the corners and the far sides of the edge are where
//! window controls and tab strips are, and must never meet a Tack window.
//!
//! The strips are topmost when made. A topmost window raised over the zone
//! afterwards (a full-screen player, say) hides it until the strips are made
//! again, which is at every tuck: the strips exist only while the board is
//! tucked and edge reveal is on ([`EdgeStrips::set_enabled`]).

use std::cell::{Cell, OnceCell};
use std::io;
use std::sync::mpsc::{sync_channel, SyncSender};
use std::thread::JoinHandle;

use tack_core::Rect;
use windows::core::{implement, w, Ref, Result};
use windows::Win32::Foundation::{HINSTANCE, HWND, LPARAM, LRESULT, POINT, POINTL, RECT, WPARAM};
use windows::Win32::Graphics::Gdi::{GetStockObject, PtInRect, BLACK_BRUSH, HBRUSH};
use windows::Win32::System::Com::IDataObject;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Ole::{
    IDropTarget, IDropTarget_Impl, OleInitialize, OleUninitialize, RegisterDragDrop, RevokeDragDrop, DROPEFFECT,
    DROPEFFECT_NONE,
};
use windows::Win32::System::SystemServices::MODIFIERKEYS_FLAGS;
use windows::Win32::System::Threading::GetCurrentThreadId;
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW, GetCursorPos, GetMessageW, GetWindowLongPtrW,
    GetWindowRect, KillTimer, LoadCursorW, PeekMessageW, PostThreadMessageW, RegisterClassW,
    SetLayeredWindowAttributes, SetTimer, SetWindowLongPtrW, SetWindowPos, UnregisterClassW, GWL_EXSTYLE, HWND_TOPMOST,
    IDC_ARROW, LWA_ALPHA, MA_NOACTIVATE, MSG, PM_NOREMOVE, SWP_NOACTIVATE, SWP_SHOWWINDOW, WM_APP, WM_DISPLAYCHANGE,
    WM_DPICHANGED, WM_MOUSEACTIVATE, WM_MOUSEMOVE, WM_QUIT, WM_TIMER, WM_USER, WNDCLASSW, WS_EX_LAYERED,
    WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_EX_TOPMOST, WS_EX_TRANSPARENT, WS_POPUP,
};

use crate::overlay;
use crate::pointer::{self, VK_LBUTTON};

/// The part of a monitor's width, centred, that is the edge zone. The one
/// place to tune it.
const ZONE_FRACTION: f64 = 1.0 / 3.0;
/// How far down the zone reaches, physical px. A pointer cannot go above the
/// screen, so the top row is always met.
const THICKNESS: i32 = 2;
/// How often an arrived pointer is looked at, to see it leave.
const LEAVE_CHECK_MS: u32 = 25;
const LEAVE_TIMER: usize = 1;
/// Between the thread and its handle.
const WM_ENABLE: u32 = WM_APP + 1;
const WM_RELAYOUT: u32 = WM_APP + 2;

/// What the strips saw.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum EdgeEvent {
    /// The pointer reached a zone, and is there until [`EdgeEvent::Left`].
    Arrived,
    Left,
    /// Something carried with a button held came over a zone, until
    /// [`EdgeEvent::DragLeft`].
    DragEntered,
    DragLeft,
}

/// The zone along the top of `monitor` (physical px): its middle third,
/// `thickness` tall. It is each monitor's own, however they sit side by side.
pub(crate) fn zone_rect(monitor: Rect, thickness: i32) -> Rect {
    let w = (monitor.w as f64 * ZONE_FRACTION).round() as i32;
    Rect { x: monitor.x + (monitor.w - w) / 2, y: monitor.y, w, h: thickness }
}

/// The zone of the monitor holding `pt`, or the nearest one.
pub(crate) fn zone_at(pt: POINT) -> Rect {
    zone_rect(rect_of(overlay::monitor_rect_at(pt)), THICKNESS)
}

fn rect_of(r: RECT) -> Rect {
    Rect { x: r.left, y: r.top, w: r.right - r.left, h: r.bottom - r.top }
}

/// The strips, on a thread of their own. Dropping it ends the thread.
pub(crate) struct EdgeStrips {
    thread: Option<JoinHandle<()>>,
    thread_id: u32,
    enabled: bool,
}

impl EdgeStrips {
    /// Starts the thread, with no strip yet ([`EdgeStrips::set_enabled`]).
    /// `on_event` runs on it, and must not block.
    pub(crate) fn start(on_event: impl Fn(EdgeEvent) + Send + 'static) -> io::Result<EdgeStrips> {
        let (ready, started) = sync_channel(1);
        let thread =
            std::thread::Builder::new().name("tack-edge".into()).spawn(move || run(Box::new(on_event), ready))?;
        match started.recv() {
            Ok(Ok(thread_id)) => Ok(EdgeStrips { thread: Some(thread), thread_id, enabled: false }),
            Ok(Err(e)) => Err(e),
            Err(_) => Err(io::Error::other("the edge thread ended at once")),
        }
    }

    /// The strips exist (one per monitor) or do not.
    pub(crate) fn set_enabled(&mut self, on: bool) {
        if on != self.enabled {
            self.enabled = on;
            self.post(WM_ENABLE, on as usize);
        }
    }

    pub(crate) fn enabled(&self) -> bool {
        self.enabled
    }

    fn post(&self, msg: u32, wparam: usize) {
        let _ = unsafe { PostThreadMessageW(self.thread_id, msg, WPARAM(wparam), LPARAM(0)) };
    }
}

impl Drop for EdgeStrips {
    fn drop(&mut self) {
        self.post(WM_QUIT, 0);
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

thread_local! {
    static ON_EVENT: OnceCell<Box<dyn Fn(EdgeEvent)>> = const { OnceCell::new() };
    /// The strip the pointer is on, and whether a button was down on arrival.
    static ARRIVED: Cell<Option<(HWND, bool)>> = const { Cell::new(None) };
}

fn emit(event: EdgeEvent) {
    ON_EVENT.with(|f| f.get().expect("set at start")(event));
}

fn run(on_event: Box<dyn Fn(EdgeEvent) + Send>, ready: SyncSender<io::Result<u32>>) {
    let class = w!("TackEdgeStrip");
    let instance = match unsafe { setup(class) } {
        Ok(instance) => instance,
        Err(e) => {
            let _ = ready.send(Err(io::Error::other(e)));
            return;
        }
    };
    ON_EVENT.with(|f| f.set(on_event).ok());
    unsafe {
        // Makes the thread's queue, so it can be posted to.
        let _ = PeekMessageW(&mut MSG::default(), None, WM_USER, WM_USER, PM_NOREMOVE);
        let _ = ready.send(Ok(GetCurrentThreadId()));

        let (mut strips, mut enabled) = (Vec::new(), false);
        let mut msg = MSG::default();
        while GetMessageW(&mut msg, None, 0, 0).0 > 0 {
            if !msg.hwnd.0.is_null() {
                DispatchMessageW(&msg);
                continue;
            }
            match msg.message {
                WM_ENABLE => enabled = msg.wParam.0 != 0,
                WM_RELAYOUT => {}
                _ => continue,
            }
            relayout(&mut strips, enabled, instance);
        }
        relayout(&mut strips, false, instance);
        let _ = UnregisterClassW(class, Some(instance));
        OleUninitialize();
    }
}

unsafe fn setup(class: windows::core::PCWSTR) -> Result<HINSTANCE> {
    OleInitialize(None)?;
    let instance: HINSTANCE = GetModuleHandleW(None)?.into();
    let wc = WNDCLASSW {
        lpfnWndProc: Some(window_proc),
        hInstance: instance,
        hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
        hbrBackground: HBRUSH(GetStockObject(BLACK_BRUSH).0),
        lpszClassName: class,
        ..Default::default()
    };
    if RegisterClassW(&wc) == 0 {
        let e = windows::core::Error::from_thread();
        OleUninitialize();
        return Err(e);
    }
    Ok(instance)
}

/// Makes the strips match the monitors (none when not `enabled`); leaves them
/// alone if they already do. A monitor added or removed, a resolution or DPI
/// change: the zones move.
unsafe fn relayout(strips: &mut Vec<(HWND, Rect)>, enabled: bool, instance: HINSTANCE) {
    let mut zones: Vec<Rect> = if enabled {
        overlay::monitor_rects().into_iter().map(|m| zone_rect(rect_of(m), THICKNESS)).collect()
    } else {
        Vec::new()
    };
    zones.sort_by_key(|z| (z.x, z.y));
    if strips.iter().map(|&(_, zone)| zone).eq(zones.iter().copied()) {
        return;
    }
    for (hwnd, _) in strips.drain(..) {
        let _ = RevokeDragDrop(hwnd);
        let _ = DestroyWindow(hwnd);
    }
    // Gone with its window, so it is not on a strip any more.
    if ARRIVED.take().is_some() {
        emit(EdgeEvent::Left);
    }
    for zone in zones {
        match create(zone, instance) {
            Ok(hwnd) => strips.push((hwnd, zone)),
            Err(e) => eprintln!("tack: cannot make an edge strip at {zone:?}: {e}"),
        }
    }
}

unsafe fn create(zone: Rect, instance: HINSTANCE) -> Result<HWND> {
    // Never activated, and not in Alt+Tab or the taskbar. Layered with a
    // constant alpha of 1: all but invisible, yet still hit-tested.
    let ex = WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
    let hwnd = CreateWindowExW(
        ex,
        w!("TackEdgeStrip"),
        w!(""),
        WS_POPUP,
        zone.x,
        zone.y,
        zone.w,
        zone.h,
        None,
        None,
        Some(instance),
        None,
    )?;
    let made = (|| {
        SetLayeredWindowAttributes(hwnd, Default::default(), 1, LWA_ALPHA)?;
        RegisterDragDrop(hwnd, &IDropTarget::from(DropTarget))?;
        // Again, in physical px: a window made on a monitor of another DPI
        // may not come out where it was asked.
        SetWindowPos(hwnd, Some(HWND_TOPMOST), zone.x, zone.y, zone.w, zone.h, SWP_NOACTIVATE | SWP_SHOWWINDOW)
    })();
    if let Err(e) = made {
        let _ = DestroyWindow(hwnd);
        return Err(e);
    }
    Ok(hwnd)
}

unsafe extern "system" fn window_proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_MOUSEACTIVATE => return LRESULT(MA_NOACTIVATE as isize),
        WM_MOUSEMOVE => arrive(hwnd),
        WM_TIMER if wparam.0 == LEAVE_TIMER => check_leave(hwnd),
        // Not rebuilt from inside a strip's own message.
        WM_DISPLAYCHANGE | WM_DPICHANGED => {
            let _ = PostThreadMessageW(GetCurrentThreadId(), WM_RELAYOUT, WPARAM(0), LPARAM(0));
            return LRESULT(0);
        }
        _ => {}
    }
    DefWindowProcW(hwnd, msg, wparam, lparam)
}

/// The first mouse move on a strip.
fn arrive(hwnd: HWND) {
    if ARRIVED.get().is_some() {
        return;
    }
    let held = pointer::key_down(VK_LBUTTON.0);
    ARRIVED.set(Some((hwnd, held)));
    if !held {
        set_click_through(hwnd, true);
    }
    unsafe { SetTimer(Some(hwnd), LEAVE_TIMER, LEAVE_CHECK_MS, None) };
    emit(EdgeEvent::Arrived);
}

/// The timer's look at the pointer: gone from the strip, or the held button
/// let go (no drag now, so clicks pass through).
fn check_leave(hwnd: HWND) {
    let Some((on, held)) = ARRIVED.get() else { return };
    if on != hwnd {
        return;
    }
    let mut rect = RECT::default();
    let mut pt = POINT::default();
    let inside = unsafe {
        GetWindowRect(hwnd, &mut rect).is_ok() && GetCursorPos(&mut pt).is_ok() && PtInRect(&rect, pt).as_bool()
    };
    if inside {
        if held && !pointer::key_down(VK_LBUTTON.0) {
            ARRIVED.set(Some((hwnd, false)));
            set_click_through(hwnd, true);
        }
        return;
    }
    unsafe {
        let _ = KillTimer(Some(hwnd), LEAVE_TIMER);
    }
    set_click_through(hwnd, false);
    ARRIVED.set(None);
    emit(EdgeEvent::Left);
}

/// Mouse events pass through the strip, to the window below, or not.
fn set_click_through(hwnd: HWND, on: bool) {
    unsafe {
        let ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        let bit = WS_EX_TRANSPARENT.0 as isize;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, if on { ex | bit } else { ex & !bit });
    }
}

/// Takes no drop: it only says a drag is over the strip.
#[implement(IDropTarget)]
struct DropTarget;

#[allow(non_snake_case)]
impl IDropTarget_Impl for DropTarget_Impl {
    fn DragEnter(
        &self,
        _data: Ref<'_, IDataObject>,
        _keys: MODIFIERKEYS_FLAGS,
        _pt: &POINTL,
        effect: *mut DROPEFFECT,
    ) -> Result<()> {
        refuse(effect);
        emit(EdgeEvent::DragEntered);
        Ok(())
    }

    fn DragOver(&self, _keys: MODIFIERKEYS_FLAGS, _pt: &POINTL, effect: *mut DROPEFFECT) -> Result<()> {
        refuse(effect);
        Ok(())
    }

    fn DragLeave(&self) -> Result<()> {
        emit(EdgeEvent::DragLeft);
        Ok(())
    }

    fn Drop(
        &self,
        _data: Ref<'_, IDataObject>,
        _keys: MODIFIERKEYS_FLAGS,
        _pt: &POINTL,
        effect: *mut DROPEFFECT,
    ) -> Result<()> {
        refuse(effect);
        emit(EdgeEvent::DragLeft);
        Ok(())
    }
}

fn refuse(effect: *mut DROPEFFECT) {
    if !effect.is_null() {
        unsafe { *effect = DROPEFFECT_NONE };
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn monitor(x: i32, y: i32, w: i32, h: i32) -> Rect {
        Rect { x, y, w, h }
    }

    #[test]
    fn the_zone_is_the_middle_third_of_the_top_edge() {
        let zone = zone_rect(monitor(0, 0, 1920, 1080), THICKNESS);
        assert_eq!(zone, Rect { x: 640, y: 0, w: 640, h: THICKNESS });
        // Centred: as far from the left corner as from the right.
        assert_eq!(zone.x, 1920 - (zone.x + zone.w));
    }

    #[test]
    fn it_scales_with_the_screen() {
        // A 1440 px laptop, 4K and an ultrawide, each in its own pixels.
        for (width, third) in [(1440, 480), (3840, 1280), (5120, 1707), (2560, 853)] {
            assert_eq!(zone_rect(monitor(0, 0, width, 1440), THICKNESS).w, third);
        }
    }

    #[test]
    fn odd_widths_round_and_stay_inside_the_monitor() {
        for width in [1366, 1921, 1280, 1, 2, 3, 7] {
            let zone = zone_rect(monitor(0, 0, width, 768), THICKNESS);
            assert!(zone.x >= 0 && zone.x + zone.w <= width, "{width}: {zone:?}");
            // The two sides differ by a pixel at most.
            assert!((zone.x - (width - zone.x - zone.w)).abs() <= 1, "{width}: {zone:?}");
        }
        assert_eq!(zone_rect(monitor(0, 0, 1366, 768), 2), Rect { x: 455, y: 0, w: 455, h: 2 });
    }

    #[test]
    fn each_monitor_has_its_own_zone_on_its_own_top() {
        // Another size to the right, a lower one on the left (negative
        // coordinates), another to the right of that.
        let left = monitor(-2560, 200, 2560, 1440);
        let main = monitor(0, 0, 1920, 1080);
        let right = monitor(1920, -300, 3840, 2160);
        assert_eq!(zone_rect(left, 2), Rect { x: -2560 + 853, y: 200, w: 853, h: 2 });
        assert_eq!(zone_rect(main, 2), Rect { x: 640, y: 0, w: 640, h: 2 });
        assert_eq!(zone_rect(right, 2), Rect { x: 1920 + 1280, y: -300, w: 1280, h: 2 });
        // The corners and the far sides of an edge are outside it.
        for m in [left, main, right] {
            let zone = zone_rect(m, 2);
            assert!(!zone.contains(m.x, m.y) && !zone.contains(m.x + m.w - 1, m.y));
            assert!(zone.contains(m.x + m.w / 2, m.y));
        }
    }

    #[test]
    fn the_strips_start_and_stop_without_a_trace() {
        let mut strips = EdgeStrips::start(|_| {}).expect("a message thread");
        assert!(!strips.enabled());
        strips.set_enabled(true);
        assert!(strips.enabled());
        strips.set_enabled(false);
        drop(strips);
    }
}
