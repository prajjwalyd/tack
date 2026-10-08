//! The notification area icon and its menu, kept short: Show board and Use
//! on your phone…; Clear board and Open Screenshots folder; the settings
//! (Reveal at top edge, Sound, Start with Windows, Shortcuts…); Quit. A left click on the icon toggles the board. If a
//! shortcut could not be registered (another app has it), the menu says so
//! and offers to change it; the menu is rebuilt whenever that changes.
//!
//! The icon is the app icon in full colour (the brass pin in green felt),
//! hand-drawn for the tray at 16, 20, 24 and 32 px, in two tunings: a darker
//! frame and brass rim for a light taskbar, a lighter felt and frame for a
//! dark one. It follows `SystemUsesLightTheme` (the taskbar's theme, not the
//! apps'), read at startup and again whenever that registry key changes.

use std::sync::Mutex;

use tack_core::RevealReason;
use tack_windows::{autostart, screenshots, shell};
use tauri::image::Image;
use tauri::menu::{CheckMenuItem, Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{include_image, AppHandle, Manager, Wry};
use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::ERROR_SUCCESS;
use windows::Win32::System::Registry::{
    RegGetValueW, RegNotifyChangeKeyValue, RegOpenKeyExW, HKEY, HKEY_CURRENT_USER, KEY_NOTIFY, KEY_QUERY_VALUE,
    REG_NOTIFY_CHANGE_LAST_SET, RRF_RT_REG_DWORD,
};

use crate::ipc::events;
use crate::state::lock;
use crate::{phone, prints, reveal, shortcuts};

/// The check items, kept to read and set their state when clicked.
pub struct TrayItems {
    edge: CheckMenuItem<Wry>,
    sound: CheckMenuItem<Wry>,
    autostart: CheckMenuItem<Wry>,
}

/// The current menu's check items; replaced when the menu is rebuilt.
type Items = Mutex<Option<TrayItems>>;

/// The menu as things stand: the show shortcut beside "Show board", and a
/// line for each shortcut another app has taken.
fn menu(app: &AppHandle) -> tauri::Result<(Menu<Wry>, TrayItems)> {
    let settings = lock(app).settings.clone();

    // The shortcut sits in the menu's accelerator column (after a tab), as
    // text: Tack registers it itself, the menu only shows it.
    let toggle = shortcuts::toggle_label(app);
    let show_label = if toggle.is_empty() { "Show board".to_string() } else { format!("Show board\t{toggle}") };
    let show = MenuItem::with_id(app, "tray:show", show_label, true, None::<&str>)?;
    let clear = MenuItem::with_id(app, "tray:clear", "Clear board", true, None::<&str>)?;
    let folder = MenuItem::with_id(app, "tray:folder", "Open Screenshots folder", true, None::<&str>)?;
    let edge =
        CheckMenuItem::with_id(app, "tray:edge", "Reveal at top edge", true, settings.edge_reveal, None::<&str>)?;
    let sound = CheckMenuItem::with_id(app, "tray:sound", "Sound", true, settings.sound, None::<&str>)?;
    let autostart =
        CheckMenuItem::with_id(app, "tray:autostart", "Start with Windows", true, autostart::enabled(), None::<&str>)?;
    let keys = MenuItem::with_id(app, "tray:shortcuts", "Shortcuts\u{2026}", true, None::<&str>)?;
    let phone = MenuItem::with_id(app, "tray:phone", "Use on your phone\u{2026}", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, "tray:quit", "Quit Tack", true, None::<&str>)?;
    let menu = Menu::with_items(
        app,
        &[
            &show,
            &phone,
            &PredefinedMenuItem::separator(app)?,
            &clear,
            &folder,
            &PredefinedMenuItem::separator(app)?,
            &edge,
            &sound,
            &autostart,
            &keys,
            &PredefinedMenuItem::separator(app)?,
            &quit,
        ],
    )?;
    // Right under "Open Screenshots folder", so it is seen.
    for (n, chord) in shortcuts::in_use(app).into_iter().enumerate() {
        let text = format!("{chord} is in use by another app \u{2014} Change\u{2026}");
        let warning = MenuItem::with_id(app, format!("tray:shortcuts-in-use-{n}"), text, true, None::<&str>)?;
        menu.insert(&warning, 5 + n)?;
    }
    Ok((menu, TrayItems { edge, sound, autostart }))
}

pub fn build(app: &AppHandle) -> tauri::Result<()> {
    let (menu, items) = menu(app)?;

    TrayIconBuilder::with_id("tack")
        .icon(glyph(app, taskbar_is_light()))
        .tooltip("Tack")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click { button: MouseButton::Left, button_state: MouseButtonState::Up, .. } = event {
                reveal::toggle(tray.app_handle(), RevealReason::Tray);
            }
        })
        .build(app)?;

    app.manage::<Items>(Mutex::new(Some(items)));
    follow_taskbar_theme(app.clone());
    Ok(())
}

/// Rebuilds the menu: the shortcuts changed, or one could not be registered.
pub fn refresh(app: &AppHandle) {
    let Some(tray) = app.tray_by_id("tack") else { return };
    match menu(app) {
        Ok((menu, items)) => {
            let _ = tray.set_menu(Some(menu));
            *app.state::<Items>().lock().unwrap_or_else(|p| p.into_inner()) = Some(items);
        }
        Err(e) => eprintln!("tack: cannot rebuild the tray menu: {e}"),
    }
}

const PERSONALIZE: PCWSTR = w!("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");

/// True when the taskbar is light. A missing value (Windows 10 before 1903)
/// means the dark taskbar those versions always had.
fn taskbar_is_light() -> bool {
    let mut value: u32 = 0;
    let mut len = std::mem::size_of::<u32>() as u32;
    let err = unsafe {
        RegGetValueW(
            HKEY_CURRENT_USER,
            PERSONALIZE,
            w!("SystemUsesLightTheme"),
            RRF_RT_REG_DWORD,
            None,
            Some(&mut value as *mut u32 as *mut _),
            Some(&mut len),
        )
    };
    err == ERROR_SUCCESS && value != 0
}

/// The icon for the taskbar's theme, drawn for the tray's size on the
/// primary monitor (16 px at 100%, 20 at 125%, 24 at 150%, 32 at 200%; 64 px
/// above that), so Windows never has to rescale it.
fn glyph(app: &AppHandle, light_taskbar: bool) -> Image<'static> {
    let scale = app.primary_monitor().ok().flatten().map_or(1.0, |m| m.scale_factor());
    let px = (16.0 * scale).round() as u32;
    match (light_taskbar, px) {
        (true, 0..=16) => include_image!("icons/tray-light-16.png"),
        (true, 17..=20) => include_image!("icons/tray-light-20.png"),
        (true, 21..=24) => include_image!("icons/tray-light-24.png"),
        (true, 25..=32) => include_image!("icons/tray-light.png"),
        (true, _) => include_image!("icons/tray-light@2x.png"),
        (false, 0..=16) => include_image!("icons/tray-dark-16.png"),
        (false, 17..=20) => include_image!("icons/tray-dark-20.png"),
        (false, 21..=24) => include_image!("icons/tray-dark-24.png"),
        (false, 25..=32) => include_image!("icons/tray-dark.png"),
        (false, _) => include_image!("icons/tray-dark@2x.png"),
    }
}

/// Swaps the glyph when the taskbar theme changes. The thread sleeps in
/// `RegNotifyChangeKeyValue` until the Personalize key is written: no polling.
fn follow_taskbar_theme(app: AppHandle) {
    let _ = std::thread::Builder::new().name("tray-theme".into()).spawn(move || {
        let mut key = HKEY::default();
        let opened =
            unsafe { RegOpenKeyExW(HKEY_CURRENT_USER, PERSONALIZE, None, KEY_NOTIFY | KEY_QUERY_VALUE, &mut key) };
        if opened != ERROR_SUCCESS {
            return;
        }
        let mut light = taskbar_is_light();
        // The key stays open for the life of the app, like this thread.
        while unsafe { RegNotifyChangeKeyValue(key, false, REG_NOTIFY_CHANGE_LAST_SET, None, false) } == ERROR_SUCCESS {
            let now = taskbar_is_light();
            if now != light {
                light = now;
                if let Some(tray) = app.tray_by_id("tack") {
                    let _ = tray.set_icon(Some(glyph(&app, light)));
                }
            }
        }
    });
}

/// Menu clicks with a `tray:` id. The check state is set explicitly, so it is
/// right whether or not the menu already flipped it.
pub fn handle(app: &AppHandle, id: &str) {
    if id == "tray:phone" {
        let app = app.clone();
        std::thread::spawn(move || phone::window::open(&app));
        return;
    }
    if id == "tray:shortcuts" || id.starts_with("tray:shortcuts-in-use") {
        // Not from this handler: building a window on the event loop's own
        // thread, inside one of its handlers, would wait on itself.
        let app = app.clone();
        std::thread::spawn(move || shortcuts::open_dialog(&app));
        return;
    }
    let state = app.state::<Items>();
    let guard = state.lock().unwrap_or_else(|p| p.into_inner());
    let Some(items) = guard.as_ref() else { return };
    match id {
        "tray:show" => reveal::reveal(app, RevealReason::Tray),
        "tray:clear" => prints::clear(app),
        "tray:folder" => shell::open_folder(&screenshots::folder()),
        "tray:edge" => {
            let on = !lock(app).settings.edge_reveal;
            set_edge_reveal(app, on);
            let _ = items.edge.set_checked(on);
        }
        "tray:sound" => {
            let on = !lock(app).settings.sound;
            set_sound(app, on);
            let _ = items.sound.set_checked(on);
        }
        "tray:autostart" => {
            let on = !autostart::enabled();
            autostart::set(on);
            let _ = items.autostart.set_checked(autostart::enabled());
        }
        "tray:quit" => app.exit(0),
        _ => {}
    }
}

fn set_sound(app: &AppHandle, on: bool) {
    {
        let mut s = lock(app);
        s.settings.sound = on;
        s.save();
    }
    events::settings(app, on);
}

fn set_edge_reveal(app: &AppHandle, on: bool) {
    let mut s = lock(app);
    s.settings.edge_reveal = on;
    s.save();
}
