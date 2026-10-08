//! Keyboard shortcuts as the user sees and saves them, such as "Win+Alt+S"
//! ("" turns one off). This module parses, checks and prints them;
//! `tack_windows::hotkey` registers a [`Chord`].

use std::fmt;

/// Shows or hides the board.
pub const DEFAULT_TOGGLE: &str = "Win+Alt+S";
/// Pins the current selection.
pub const DEFAULT_PIN: &str = "Win+Alt+C";

/// A key with its modifiers. `vk` is the Windows virtual-key code.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Hash)]
pub struct Chord {
    pub win: bool,
    pub ctrl: bool,
    pub alt: bool,
    pub shift: bool,
    pub vk: u16,
}

/// The keys a shortcut may end in, besides letters, digits and F1 to F24:
/// names as written in a chord, with their virtual-key codes.
const NAMED: [(&str, u16); 11] = [
    ("Space", 0x20),
    ("PageUp", 0x21),
    ("PageDown", 0x22),
    ("End", 0x23),
    ("Home", 0x24),
    ("Left", 0x25),
    ("Up", 0x26),
    ("Right", 0x27),
    ("Down", 0x28),
    ("Insert", 0x2D),
    ("Delete", 0x2E),
];

/// The name of a key as written in a chord, if Tack accepts it.
fn key_name(vk: u16) -> Option<String> {
    match vk {
        0x30..=0x39 | 0x41..=0x5A => Some(char::from(vk as u8).to_string()),
        0x70..=0x87 => Some(format!("F{}", vk - 0x6F)),
        _ => NAMED.iter().find(|(_, code)| *code == vk).map(|(name, _)| name.to_string()),
    }
}

fn key_code(name: &str) -> Option<u16> {
    let upper = name.to_ascii_uppercase();
    let bytes = upper.as_bytes();
    if bytes.len() == 1 && (bytes[0].is_ascii_uppercase() || bytes[0].is_ascii_digit()) {
        return Some(bytes[0] as u16);
    }
    if let Some(n) = upper.strip_prefix('F').and_then(|n| n.parse::<u16>().ok()) {
        if (1..=24).contains(&n) && !upper.starts_with("F0") {
            return Some(0x6F + n);
        }
    }
    NAMED.iter().find(|(n, _)| n.eq_ignore_ascii_case(name)).map(|(_, code)| *code)
}

impl Chord {
    /// Parses "Win+Alt+S" (modifiers in any order and case, "Windows",
    /// "Control" and "Ctrl" accepted). `Ok(None)` for "" (off). A chord must
    /// hold Win, Ctrl or Alt, unless its key is an F key: Shift and a letter
    /// alone would take that capital letter away from every app.
    pub fn parse(text: &str) -> Result<Option<Chord>, String> {
        let text = text.trim();
        if text.is_empty() {
            return Ok(None);
        }
        let mut chord = Chord { win: false, ctrl: false, alt: false, shift: false, vk: 0 };
        for part in text.split('+').map(str::trim) {
            match part.to_ascii_lowercase().as_str() {
                "win" | "windows" | "super" | "meta" => chord.win = true,
                "ctrl" | "control" => chord.ctrl = true,
                "alt" => chord.alt = true,
                "shift" => chord.shift = true,
                "" => return Err(format!("\"{text}\" has an empty part")),
                _ => {
                    if chord.vk != 0 {
                        return Err(format!("\"{text}\" has more than one key"));
                    }
                    chord.vk = key_code(part).ok_or_else(|| format!("\"{part}\" is not a key Tack can use"))?;
                }
            }
        }
        if chord.vk == 0 {
            return Err(format!("\"{text}\" has no key, only modifiers"));
        }
        let f_key = (0x70..=0x87).contains(&chord.vk);
        if !(chord.win || chord.ctrl || chord.alt || f_key) {
            return Err(format!("\"{text}\" needs Win, Ctrl or Alt"));
        }
        Ok(Some(chord))
    }
}

/// "Win+Ctrl+Alt+Shift+K", modifiers always in that order.
impl fmt::Display for Chord {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        for (on, name) in [(self.win, "Win"), (self.ctrl, "Ctrl"), (self.alt, "Alt"), (self.shift, "Shift")] {
            if on {
                write!(f, "{name}+")?;
            }
        }
        f.write_str(&key_name(self.vk).unwrap_or_else(|| format!("0x{:02X}", self.vk)))
    }
}

/// A shortcut as saved: tidied up ("alt + win + s" becomes "Win+Alt+S"), or
/// left as it was if it does not parse.
pub fn tidy(text: &str) -> String {
    match Chord::parse(text) {
        Ok(Some(chord)) => chord.to_string(),
        Ok(None) => String::new(),
        Err(_) => text.trim().to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn chord(text: &str) -> Chord {
        Chord::parse(text).unwrap().unwrap()
    }

    #[test]
    fn the_defaults_parse() {
        assert_eq!(chord(DEFAULT_TOGGLE), Chord { win: true, ctrl: false, alt: true, shift: false, vk: 0x53 });
        assert_eq!(chord(DEFAULT_PIN).vk, 0x43);
        assert_eq!(chord(DEFAULT_PIN).to_string(), DEFAULT_PIN);
    }

    #[test]
    fn chords_print_in_one_order_whatever_the_input() {
        assert_eq!(chord("shift + alt+ctrl+WINDOWS+k").to_string(), "Win+Ctrl+Alt+Shift+K");
        assert_eq!(chord("control+alt+t").to_string(), "Ctrl+Alt+T");
        assert_eq!(tidy(" alt+win+c "), "Win+Alt+C");
        assert_eq!(tidy("nonsense"), "nonsense");
        assert_eq!(tidy("  "), "");
    }

    #[test]
    fn letters_digits_f_keys_and_a_few_named_keys_are_accepted() {
        assert_eq!(chord("Ctrl+7").vk, 0x37);
        assert_eq!(chord("F9").vk, 0x78);
        assert_eq!(chord("Shift+F24").vk, 0x87);
        assert_eq!(chord("Win+Alt+pageup").to_string(), "Win+Alt+PageUp");
        assert_eq!(chord("Ctrl+Alt+Space").to_string(), "Ctrl+Alt+Space");
        for vk in (0x30..=0x39).chain(0x41..=0x5A).chain(0x70..=0x87) {
            let name = key_name(vk).unwrap();
            assert_eq!(chord(&format!("Alt+{name}")).vk, vk, "{name}");
        }
    }

    #[test]
    fn off_and_bad_chords() {
        assert_eq!(Chord::parse("").unwrap(), None);
        for bad in ["T", "Shift+T", "Win+Alt", "Win+Alt+S+C", "Win++T", "Ctrl+F0", "Ctrl+F25", "Ctrl+Tab", "Alt+é"] {
            assert!(Chord::parse(bad).is_err(), "{bad}");
        }
    }
}
