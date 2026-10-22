namespace Tack.Core.Phone;

/// <summary>
/// Who may use the board from another device. A device is known by its NetBird
/// WireGuard key, never its address or name. A device the user allowed gets in;
/// any other is asked about once, with a code shown on both screens; one the
/// user turned down stays out, and may ask again only after a wait that doubles
/// with each refusal. Refusals outlive the server stopping; only a restart of
/// Tack forgets them.
/// </summary>
public sealed class PhoneGate
{
    /// <summary>How many devices may wait for an answer at once; more are turned away, not queued.</summary>
    public const int MaxPending = 3;

    /// <summary>How long a request waits for the user before it lapses.</summary>
    public const long PendingForMs = 10 * 60 * 1000;

    /// <summary>How soon a turned-down device may ask again; doubled after each further refusal, up to <see cref="AskAgainMaxMs"/>.</summary>
    public const long AskAgainAfterMs = 30 * 1000;

    public const long AskAgainMaxMs = 60 * 60 * 1000;

    readonly List<Asking> _pending = [];
    readonly List<Refusal> _refused = [];

    /// <summary>The devices waiting, oldest first.</summary>
    public IReadOnlyList<Asking> Pending => _pending;

    /// <summary>A request from <paramref name="who"/>. <paramref name="code"/> is a fresh code, used if this request asks.</summary>
    public Verdict Check(PhoneSettings settings, Who who, string code, long nowMs)
    {
        Lapse(nowMs);
        if (settings.Allows(who.Key))
        {
            return Verdict.Allowed.Instance;
        }
        if (_refused.Any(r => r.Key == who.Key && !r.Lifted))
        {
            return Verdict.Refused.Instance;
        }
        if (_pending.Find(a => a.Key == who.Key) is { } asking)
        {
            return new Verdict.Waiting(false, asking.Code);
        }
        if (_pending.Count >= MaxPending)
        {
            return Verdict.Busy.Instance;
        }
        _pending.Add(new Asking(who.Key, who.Name, who.Fqdn, who.Ip, code, nowMs));
        return new Verdict.Waiting(true, code);
    }

    /// <summary>A turned-down device asks again, from its page: allowed once its wait is over.</summary>
    public Verdict AskAgain(PhoneSettings settings, Who who, string code, long nowMs)
    {
        if (_refused.Find(r => r.Key == who.Key && !r.Lifted) is { } refusal)
        {
            if (!refusal.MayAskAgain(nowMs))
            {
                return Verdict.Refused.Instance;
            }
            refusal.Lifted = true;
        }
        return Check(settings, who, code, nowMs);
    }

    /// <summary>The user's answer about a waiting device: its name if it was waiting. When allowed, the caller saves it in the settings.</summary>
    public string? Answer(string key, bool allow, long nowMs)
    {
        Lapse(nowMs);
        var index = _pending.FindIndex(a => a.Key == key);
        if (index < 0)
        {
            return null;
        }
        var asking = _pending[index];
        _pending.RemoveAt(index);
        if (!allow)
        {
            if (_refused.Find(r => r.Key == key) is { } refusal)
            {
                refusal.Times++;
                refusal.AtMs = nowMs;
                refusal.Lifted = false;
            }
            else
            {
                _refused.Add(new Refusal(key) { AtMs = nowMs, Times = 1 });
            }
        }
        return asking.Name;
    }

    /// <summary>Forgets the waiting requests: the board stopped being served.</summary>
    public void Clear() => _pending.Clear();

    /// <summary>Lets requests nobody answered in time go.</summary>
    public void Lapse(long nowMs) => _pending.RemoveAll(a => nowMs - a.SinceMs >= PendingForMs);

    /// <summary>A device the user turned down: when, how often, and whether it has since been let ask again.</summary>
    sealed class Refusal(string key)
    {
        public string Key { get; } = key;

        public long AtMs { get; set; }

        public int Times { get; set; }

        public bool Lifted { get; set; }

        public bool MayAskAgain(long nowMs)
        {
            var wait = Math.Min(AskAgainAfterMs << Math.Min(Math.Max(Times - 1, 0), 16), AskAgainMaxMs);
            return Math.Max(nowMs - AtMs, 0) >= wait;
        }
    }
}
