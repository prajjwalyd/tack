//! Tests that keep the update settings in `tauri.conf.json` and the web
//! views' capabilities as they must be. Nothing here runs in the app: a
//! change that loosens an update safeguard, or lets a web view near the
//! network or the updater, fails the build instead of shipping.

use serde_json::Value;

const CONFIG: &str = include_str!("../../tauri.conf.json");
const CAPABILITIES: &str = include_str!("../../capabilities/default.json");

/// Tack's update-signing public key (minisign; public by design). The
/// matching private key signs the releases in CI.
const PUBKEY: &str = "dW50cnVzdGVkIGNvbW1lbnQ6IG1pbmlzaWduIHB1YmxpYyBrZXk6IDlEMjBGNUEwRjIzMUQ5MkYKUldRdjJUSHlvUFVnblhFbXRrbVRyT1BiQzNabis2Zks0Q0NQN0lrV0ptam9hUnhCUk5LTkNyNEcK";
const ENDPOINT: &str = "https://github.com/prajjwalyd/tack/releases/latest/download/latest.json";

fn config() -> Value {
    serde_json::from_str(CONFIG).expect("tauri.conf.json is JSON")
}

fn updater() -> Value {
    config()["plugins"]["updater"].clone()
}

#[test]
fn updates_come_from_one_https_endpoint() {
    assert_eq!(updater()["endpoints"], serde_json::json!([ENDPOINT]));
}

#[test]
fn updates_are_verified_against_the_built_in_key() {
    use base64::Engine;
    let updater = updater();
    assert_eq!(updater["pubkey"], PUBKEY);
    let key = base64::engine::general_purpose::STANDARD.decode(PUBKEY).unwrap();
    let key = String::from_utf8(key).unwrap();
    assert!(key.starts_with("untrusted comment: minisign public key"), "not a minisign public key");
}

#[test]
fn no_safeguard_is_switched_off() {
    let updater = updater();
    let keys = updater.as_object().unwrap();
    for key in keys.keys() {
        assert!(!key.to_lowercase().starts_with("dangerous"), "{key} weakens the updater");
    }
    // An old release has a valid signature too: it must be the signed version.
    assert_eq!(updater["requireSignedVersion"], true);
    assert_ne!(updater["allowDowngrades"], true);
    assert_eq!(updater["windows"]["installMode"], "passive");
}

#[test]
fn releases_are_built_with_signatures() {
    assert_eq!(config()["bundle"]["createUpdaterArtifacts"], true);
}

#[test]
fn the_version_is_the_one_the_signatures_name() {
    // The CLI signs the version from the config; the app compares against
    // its own. They must be one number.
    assert_eq!(config()["version"], env!("CARGO_PKG_VERSION"));
}

#[test]
fn web_views_are_granted_nothing_of_the_updater() {
    let capabilities: Value = serde_json::from_str(CAPABILITIES).unwrap();
    let permissions = capabilities["permissions"].as_array().unwrap();
    for permission in permissions {
        let permission = permission.as_str().expect("a plain permission");
        assert!(!permission.starts_with("updater"), "{permission}");
        assert!(permission.starts_with("core:"), "{permission}: only core is granted");
    }
}

#[test]
fn the_web_views_stay_off_the_network() {
    let config = config();
    let csp = config["app"]["security"]["csp"].as_str().unwrap();
    assert!(csp.starts_with("default-src 'self';"));
    assert!(csp.contains("connect-src ipc: http://ipc.localhost;"));
    assert!(!csp.contains("https:") && !csp.contains("unsafe-eval"));
    let args = config["app"]["windows"][0]["additionalBrowserArgs"].as_str().unwrap();
    for arg in ["--disable-background-networking", "--disable-component-update", "--no-proxy-server"] {
        assert!(args.contains(arg), "{arg}");
    }
}
