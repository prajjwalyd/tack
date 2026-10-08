//! What Tack needs from `netbird status --json`: this device's address and
//! name on the NetBird network, and the peers it can see. Tack serves the
//! phone board on that address only, and answers only the peers listed here.
//! Only the fields Tack uses are read; NetBird adds fields over time.

use std::net::Ipv4Addr;

use serde::Deserialize;

/// This device on the NetBird network.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Status {
    /// This device's NetBird address, e.g. 100.90.53.74.
    pub ip: Ipv4Addr,
    /// Its NetBird name, e.g. "my-pc.netbird.cloud". Never assumed to end in
    /// netbird.cloud: self-hosted networks have their own domain.
    pub fqdn: String,
    /// The peers NetBird's access policies let this device see.
    pub peers: Vec<Peer>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Peer {
    pub ip: Ipv4Addr,
    pub fqdn: String,
    /// The peer's WireGuard public key: who it is. An address can move to
    /// another peer; the key cannot.
    pub key: String,
}

impl Peer {
    /// The first label of the name, "pixel" for "pixel.netbird.cloud": how
    /// the board names a device.
    pub fn short_name(&self) -> &str {
        self.fqdn.split('.').next().unwrap_or(&self.fqdn)
    }
}

impl Status {
    /// The peer at `ip`, if NetBird lists it.
    pub fn peer(&self, ip: Ipv4Addr) -> Option<&Peer> {
        self.peers.iter().find(|p| p.ip == ip)
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Raw {
    #[serde(default)]
    netbird_ip: String,
    #[serde(default)]
    fqdn: String,
    #[serde(default)]
    peers: RawPeers,
}

#[derive(Deserialize, Default)]
struct RawPeers {
    #[serde(default)]
    details: Vec<RawPeer>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawPeer {
    #[serde(default)]
    netbird_ip: String,
    #[serde(default)]
    fqdn: String,
    #[serde(default)]
    public_key: String,
}

/// Reads `netbird status --json`. None when NetBird is not connected (no
/// address yet) or the output is not what Tack expects.
pub fn parse(json: &str) -> Option<Status> {
    let raw: Raw = serde_json::from_str(json).ok()?;
    let ip = address(&raw.netbird_ip)?;
    let fqdn = raw.fqdn.trim_end_matches('.').to_string();
    if fqdn.is_empty() {
        return None;
    }
    let peers = raw
        .peers
        .details
        .into_iter()
        .filter(|p| !p.public_key.is_empty())
        .filter_map(|p| {
            Some(Peer {
                ip: address(&p.netbird_ip)?,
                fqdn: p.fqdn.trim_end_matches('.').to_string(),
                key: p.public_key,
            })
        })
        .collect();
    Some(Status { ip, fqdn, peers })
}

/// "100.90.53.74/16" or "100.90.53.74" to the address.
fn address(text: &str) -> Option<Ipv4Addr> {
    text.split('/').next()?.trim().parse().ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    const CONNECTED: &str = r#"{
        "peers": { "total": 2, "connected": 0, "details": [
            { "fqdn": "pixel.netbird.cloud", "netbirdIp": "100.90.1.2", "publicKey": "cGl4ZWw=", "status": "Idle" },
            { "fqdn": "jetson.example.org.", "netbirdIp": "100.90.1.3", "publicKey": "amV0c29u", "status": "Connected" }
        ] },
        "netbirdIp": "100.90.53.74/16",
        "fqdn": "my-pc.netbird.cloud",
        "lazyConnectionEnabled": true
    }"#;

    #[test]
    fn reads_this_device_and_its_peers() {
        let s = parse(CONNECTED).unwrap();
        assert_eq!(s.ip, Ipv4Addr::new(100, 90, 53, 74));
        assert_eq!(s.fqdn, "my-pc.netbird.cloud");
        assert_eq!(s.peers.len(), 2);
        assert_eq!(s.peer(Ipv4Addr::new(100, 90, 1, 3)).unwrap().fqdn, "jetson.example.org");
        assert_eq!(s.peers[0].short_name(), "pixel");
        assert_eq!(s.peers[0].key, "cGl4ZWw=");
    }

    #[test]
    fn an_unknown_address_is_no_peer() {
        let s = parse(CONNECTED).unwrap();
        assert!(s.peer(Ipv4Addr::new(192, 168, 1, 20)).is_none());
    }

    #[test]
    fn not_connected_is_none() {
        assert!(parse(r#"{ "netbirdIp": "", "fqdn": "", "peers": { "details": [] } }"#).is_none());
        assert!(parse("Daemon status: NeedsLogin").is_none());
    }

    #[test]
    fn a_peer_without_an_address_or_key_is_skipped() {
        let json = r#"{ "netbirdIp": "100.90.0.1/16", "fqdn": "a.b", "peers": { "details": [
            { "fqdn": "x.b", "netbirdIp": "", "publicKey": "eA==" },
            { "fqdn": "y.b", "netbirdIp": "100.90.0.2", "publicKey": "" }
        ] } }"#;
        assert!(parse(json).unwrap().peers.is_empty());
    }
}
