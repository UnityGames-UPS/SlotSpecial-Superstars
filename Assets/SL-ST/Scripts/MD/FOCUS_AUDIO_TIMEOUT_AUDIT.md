# Focus / Visibility / Audio-Mute / Timeout / OnError / Orientation / Balance-Sync — Audit Runbook

> **Who this is for:** an LLM auditing ONE of our ~70 slot games. Every game shares this
> lifecycle logic but with different class/method names. Your job: verify each of the 11 checks
> below against the reference contract, report **PASS / FAIL / MISSING** with a `file:line`, and
> where it's FAIL or MISSING, paste the corrected implementation.
>
> The reference game is **Diamond Riches**. All reference code below is copied verbatim from it.
> (Exceptions: Check 7 — Orientation change — uses **Age of Gods** as its reference, since that game
> carries the current responsive-scaling implementation. Check 10 — Balance sync — uses
> **SlotBase-RockClimber**, the first game in the line to implement it.)

---

## How to use this doc

1. Read all 9 checks first so you understand how the pieces interlock (they cooperate — a
   correct `OnFocusChanged` is useless if the visibility listener was never registered).
2. For each check: locate the equivalent code in the target game, compare against **What to look
   for** and the **Reference implementation**, then run through **Common failure modes**.
3. Emit a verdict per check using the template, then fill in the **Final report** table at the end.
4. If a piece is MISSING or FAIL, output the exact code to add/fix, adapted to the target game's
   names (see the naming map).

### Guardrail: fix what's in the reference, don't invent what isn't

When adapting the **Reference implementation** to the target game, port it faithfully (renaming
per the naming map) — don't add extra behavior the reference doesn't specify, even if it looks
like a reasonable improvement. Two concrete traps hit during a real audit pass:

- **Check 8 (`resizeCanvas`)**: the reference resizes `documentElement`/`body`/`canvas` directly
  off `visualViewport`. A target game's *existing* `resizeCanvas` may do something different (e.g.
  a hardcoded-aspect-ratio letterbox) — replace it with the reference version, don't layer a
  debounce or extra helper functions on top that the reference doesn't have.
- **Check 7 (`SwitchDisplay`) — CONFIRMED convention, but not the WebGL template's job**: across
  this line of ~70 games, the orientation/scaling handler's GameObject is named **`OC`** and its
  entry point is **`SwitchDisplay(string dimensions)`**. This is a real, standing contract — the
  **host platform** (the React Native wrapper) calls `SendMessage('OC', 'SwitchDisplay', "<w>,<h>")`
  directly on resize/rotation. Do **not** add a relay call for this inside `index.html` —
  the host already does it outside the Unity build, and adding a second call site from the
  template would be redundant/conflicting, not a fix. If `SwitchDisplay` looks orphaned (no
  in-repo caller besides the editor hotkey), that's expected and correct — verify the GameObject is
  named `OC` and the method signature matches, and leave it there. Nothing to wire.
- **Check 8 (`createUnityInstance(...).then((unityInstance) => { ... })`)**: the reference's
  `.then()` callback body is **empty** — no lines inside it. Editing this file to fix `resizeCanvas`
  (Check 8) or remove console suppression (Check 9) does not license adding anything to that
  callback: no `window.unityInstance = unityInstance`, no
  `window.SendMessage = unityInstance.SendMessage.bind(unityInstance)`, no
  `window.ReactNativeWebView.postMessage("UnityReady")` or similar ready-signal, even though it
  looks like a natural, low-risk addition that would help the host know the build finished loading.
  None of the 9 checks call for an instance-ready signal. If a target game's `index.html` already
  has one, leave it — but do not add one while working a different check, and do not assume it
  needs to exist just because the jslib's `sendFocusToUnity` mentions a `SendMessage` global as one
  of its lookup fallbacks (Check 1/6's reference already handles that with its own
  `typeof SendMessage === 'function'` / `typeof unityInstance !== 'undefined'` guards — it does not
  require `index.html` to publish either global).

If you're tempted to add code to satisfy a check that isn't shown in that check's **Reference
implementation** block AND isn't a confirmed cross-game convention like the one above, stop and
mark it UNVERIFIED with a note instead — that is the correct audit output, not silent invention.

### Naming map — match by ROLE, not exact name

Names vary across the 70+ games. Map by responsibility, not spelling:

| Role | Diamond Riches name | Other games may call it |
|---|---|---|
| Socket lifecycle manager | `SocketController` | `SocketIOManager`, `NetworkManager` |
| UI / popup manager | `UIManager` | `UiManager`, `UIController` |
| Audio manager | `AudioController` | `AudioManager`, `SoundManager` |
| JS interop wrapper | `JSFunctCalls` | `JSManager`, `WebGLBridge` |
| Disconnect popup call | `DisconnectionPopup()` | `OpenDisconnectPopup()`, `ShowDisconnect()` |
| User sound flag | `isSound` | `soundOn`, `audioEnabled` |
| Mute-all method | `SetMuteAll(bool)` | `SetMute(bool)`. **Not** `PauseAllAudio()`/`ResumeAudio()` — see Check 3, Pause/UnPause is retired |
| Orientation/scaling handler | `OrientationChange` | `ResolutionManager`, `AspectController`, `ScreenOrientationHandler` |
| Orientation entry point | `SwitchDisplay(string)` | `OnResize(string)`, `SetOrientation(string)` |
| WebGL template | `Assets/WebGLTemplates/custom/index.html` | `Assets/WebGLTemplates/<template>/index.html` |
| Balance-sync payload DTO | `BalanceSyncPayload` (RockClimber) | any `[Serializable]` class with a single `balance` field |
| Balance field read for spin-gating | `SocketIOManager.playerdata.balance` | whatever field the game's spin gate already compares against |

If a game splits mute into `PauseAllAudio()` / `ResumeAudio()` (engine `Pause()`/`UnPause()`) instead
of a single mute-flag `SetMuteAll(bool)`, that is a **FAIL, not an acceptable variant** — see Check 3.
Engine pause/resume is a second, independent thing controlling audibility on top of the `.mute` flag,
and production games in this line have hit real "audio stuck silent after refocus" bugs from the two
desyncing. Convert it to mute-flag toggling as part of the fix.

### The 11 checks at a glance

1. **Visibility listener registration** — `.jslib` + `RegisterVisibilityListener` wrapper, called from `Awake`.
2. **`OnFocusChanged` callback** — `public`, routes to audio + socket.
3. **Audio mute/unmute honoring the runtime sound setting** — blur mutes, focus restores to user's choice, the user's own mute/unmute button always wins over a stuck forced-mute flag, and BOTH the JS-driven `OnFocusChanged` and Unity's native `OnApplicationFocus` call the *same* mute-all method (mute-flag toggling only — never `Pause()`/`UnPause()`), guarded so a duplicate call from the other path can't clobber the stored "restore to" state.
4. **60-second background timeout** — closes the socket if the player stays away too long.
5. **`OnError(Error err)`** — session-expired vs generic error handling.
6. **Instant JS-side mute** — `.jslib` suspends the WebAudio context on blur so audio stops immediately.
7. **Orientation change / responsive scaling** — `SwitchDisplay` rotates the UI and retunes the CanvasScaler match on resize.
8. **WebGL template canvas fit** — `custom/index.html` has a clean `resizeCanvas` + resize/orientationchange listeners; template macros intact.
9. **No blanket console suppression in the WebGL template** — `custom/index.html` must not override `console.log`/`warn`/`error` to no-ops.
10. **Balance sync socket event (`balance:sync`)** — a backend-pushed balance update mutates the same field the spin gate reads, and refreshes the balance UI immediately.
11. **No high-frequency/heartbeat debug logging** — ping/pong (and similarly-timed) `Debug.Log` calls must not fire on every tick of a short interval; they drown out real diagnostics in a live console.

---

## Check 1 — Visibility listener registration (JS `.jslib` + wrapper + Awake call)

### What to look for
Three linked pieces must all exist:
- A `RegisterVisibilityChangeListener` function inside the game's `CustomJsLib.jslib`
  (`mergeInto(LibraryManager.library, { ... })` block). *Note: `.jslib` is not a `.cs` file — if
  you cannot open it, report it as "unverified, ask a human to confirm the .jslib function exists".*
- A `[DllImport("__Internal")]` + `internal void RegisterVisibilityListener(string)` wrapper in
  the JS-interop script, guarded by `#if UNITY_WEBGL && !UNITY_EDITOR`.
- Exactly one call `jsFunctCalls.RegisterVisibilityListener(gameObject.name)` in the UI manager's
  `Awake()`. **The `OnFocusChanged` receiver (Check 2) MUST be a component on that same
  GameObject** — the JS layer calls `SendMessage(gameObjectName, 'OnFocusChanged', value)`.

### Reference implementation

`JSFunctCalls.cs`:
```csharp
[DllImport("__Internal")] private static extern void RegisterVisibilityChangeListener(string gameObjectName);

internal void RegisterVisibilityListener(string gameObjectName)
{
#if UNITY_WEBGL && !UNITY_EDITOR
    Debug.Log($"[JS] Registering visibility change listener on '{gameObjectName}'");
    RegisterVisibilityChangeListener(gameObjectName);
#else
    Debug.Log("[JS] Visibility listener not registered (editor mode)");
#endif
}
```

`UIManager.Awake()`:
```csharp
if (jsFunctCalls != null)
    jsFunctCalls.RegisterVisibilityListener(gameObject.name);
```

The `.jslib` function (contract is fixed — callback name `OnFocusChanged`, values `'1'`=focused /
`'0'`=blurred; do not change them):
```js
RegisterVisibilityChangeListener: function(gameObjectNamePtr) {
  var gameObjectName = UTF8ToString(gameObjectNamePtr);

  // See Check 6 — instant JS-side mute. Kills audio before Unity's throttled loop.
  function setUnityAudioSuspended(suspended) {
      try {
          var wa = (typeof WEBAudio !== 'undefined') ? WEBAudio
                 : (typeof Module !== 'undefined' && Module.WEBAudio) ? Module.WEBAudio
                 : null;
          if (!wa || !wa.audioContext) return;
          if (suspended) {
              if (wa.audioContext.state === 'running') wa.audioContext.suspend();
          } else {
              if (wa.audioContext.state === 'suspended') wa.audioContext.resume();
          }
      } catch (err) { console.warn('[JS] Unity audio suspend/resume failed:', err); }
  }

  function sendFocusToUnity(focused) {
      setUnityAudioSuspended(!focused);
      try {
          var value = focused ? '1' : '0';
          if (typeof SendMessage === 'function') {
              SendMessage(gameObjectName, 'OnFocusChanged', value);
          } else if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
              unityInstance.SendMessage(gameObjectName, 'OnFocusChanged', value);
          }
      } catch (err) {
          console.error('[JS] Error sending focus message to Unity:', err);
      }
  }

  window._unityVisibilityCallback = function() {
      var hidden = document.hidden || document.webkitHidden;
      sendFocusToUnity(!hidden);
  };
  window._unityWindowBlurCallback  = function() { sendFocusToUnity(false); };
  window._unityWindowFocusCallback = function() { sendFocusToUnity(true); };

  // Remove before re-adding to avoid duplicates
  document.removeEventListener('visibilitychange',       window._unityVisibilityCallback);
  document.removeEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
  window.removeEventListener('blur',  window._unityWindowBlurCallback);
  window.removeEventListener('focus', window._unityWindowFocusCallback);

  document.addEventListener('visibilitychange',       window._unityVisibilityCallback);
  document.addEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
  window.addEventListener('blur',  window._unityWindowBlurCallback);
  window.addEventListener('focus', window._unityWindowFocusCallback);
},
```

### Common failure modes
- `RegisterVisibilityListener` never called from `Awake()` → listener never wired; focus changes never reach Unity.
- Registered on a GameObject whose name differs from where `OnFocusChanged` lives → `SendMessage` silently no-ops.
- DllImport / call not guarded by `#if UNITY_WEBGL && !UNITY_EDITOR` → editor/native build fails to compile or link.
- `.jslib` function missing entirely while the C# wrapper exists → runtime "function not found".

---

## Check 2 — `OnFocusChanged(string value)` callback

### What to look for
A **`public`** method named exactly `OnFocusChanged(string value)` on the same GameObject passed
in Check 1. It must (a) parse `"1"` → focused, (b) drive audio, (c) notify the socket manager.

**(b) must call the same audio-mute entry point that the native `OnApplicationFocus` path uses (Check
3)** — both focus sources feed one shared mute-all method, never two separate mechanisms. **(c)** is
exclusive to this path — see Check 4's note on why the socket timeout must not also be wired from
`OnApplicationFocus`.

### Reference implementation
`UIManager.cs`:
```csharp
public void OnFocusChanged(string value)
{
    bool focused = value == "1";
    Debug.Log("UNITY FOCUS CHANGED: " + value + " (focused: " + focused + ")");
    audioController?.SetMuteAll(focused ? !isSound : true);
    socketController?.HandleFocusChange(focused);
}
```

### Common failure modes
- Declared `internal`/`private` instead of **`public`** → Unity's `SendMessage` cannot invoke it (this is the single most common bug). Reserve `internal` for everything else per project convention, but this method must be `public`.
- Method renamed → JS contract expects the literal name `OnFocusChanged`.
- Audio muted unconditionally on focus (`SetMuteAll(false)` on focus) instead of `!isSound` → un-mutes a game the user chose to keep silent (see Check 3 invariant).
- Missing the `socketController?.HandleFocusChange(focused)` call → audio toggles but the 60s timeout (Check 4) never arms.

---

## Check 3 — Audio mute/unmute honoring the runtime sound setting, driven from BOTH focus sources

### What to look for
Two independent triggers report a focus change, and — learned from a real production bug — **in a
WebGL build either or both may fire for the same blur/focus event**, not reliably just one:
- **The JS-driven path** — `OnFocusChanged` (Check 2), fed by the `.jslib` visibility listener. The
  only signal guaranteed to fire inside an embedded ReactNativeWebView.
- **Unity's native path** — `OnApplicationFocus(bool)`, wherever the game places it (often on the
  same component that owns `AudioController`, or on `AudioController` itself). Unreliable inside a
  WebView, but commonly still fires for ordinary browser tab-switch/window-blur, and always fires in
  the Editor — so it must not be skipped just because the game only ships WebGL.

**Both paths must call the exact same mute-all method** — never two different mechanisms (e.g. a
mute-flag on one path, `AudioSource.Pause()`/`UnPause()` on the other). That single method must
**never force-unmute** — regaining focus restores exactly the user's last chosen setting, whatever
form that setting takes in this game (single flag vs. per-category sliders — see "Adapting to the
game's mute model" below).

### Do not use `AudioSource.Pause()` / `UnPause()` for focus muting — retired pattern
An older revision of this doc allowed `Pause()`-on-blur / `UnPause()`-on-focus as an "acceptable"
native-path alternative to mute-flag toggling. **That guidance is retired — treat it as a FAIL now.**
Engine pause/resume is a second, independent thing controlling audibility on top of the `.mute` flag;
the two are trivial to desync (see the retained trap write-up further down for what that looked like
in practice). Always mute via the `.mute` flag, from both focus paths, through one shared method.

### The invariant (verify this explicitly)
> **Regaining focus must never un-mute a game the user muted.**
> Whichever focus source fires, restoring on focus must reproduce the user's last chosen setting
> exactly — never a hardcoded `mute = false`. On blur, both sources force `mute = true` regardless of
> setting.

### The reentrancy trap (hit in a real audit pass — verify this explicitly)
> **If both focus sources can fire for the same blur/focus event, the shared mute-all method must be
> idempotent, or the second call clobbers the first call's captured "restore to" state.**
>
> Concretely: blur fires from source A → `SetMuteAll(true)` captures each source's current `.mute`
> (the user's real setting) into a "restore to" slot, then forces `.mute = true`. Blur *also* fires
> from source B for the same event (the JS bridge and Unity's own `OnApplicationFocus` both firing
> for one tab switch/backgrounding is the common case now that both are wired, not an edge case) — a
> naive `SetMuteAll(true)` runs again, but `.mute` is already `true`, so it captures `true` as the
> "restore to" value, permanently overwriting the user's real setting. On focus regain, both sources
> fire `SetMuteAll(false)`, and every call restores `.mute` to the clobbered `true`. **The audio is
> now stuck muted (or paused, under the retired Pause/UnPause pattern) after every focus cycle**, and
> the only way to hear it again is to manually move the sound/music slider — which sets `.mute`
> directly, bypassing the corrupted restore value — until the next blur/focus cycle clobbers it again.
>
> **Fix:** guard the mute-all method with a single re-entrancy flag so a second call for the same
> direction is a no-op:
> ```csharp
> private bool isForceMuted = false;
>
> internal void SetMuteAll(bool forceMute)
> {
>     if (forceMute == isForceMuted) return;   // already in that state — don't re-capture/re-restore
>     isForceMuted = forceMute;
>     // ... capture-and-force or restore, as below ...
> }
> ```
> **Verify:** confirm the game's mute-all method has an equivalent guard. Now that wiring both focus
> sources to the same method is the recommended pattern (not optional), this bug is latent in any
> game that wires both without the guard, even if it hasn't been reported yet.

### Adapting to the game's mute model — single flag vs. per-category sliders
Games in this line differ in how the *user's chosen setting* is represented:
- **Single flag** (reference: `isSound` / `userMuted`) — one bool for the whole game's audio.
- **Per-category sliders** (e.g. separate Music and SFX sliders, each writing volume + `.mute`
  straight onto its own `AudioSource`(s), with no separate boolean flag at all).

Don't force a slider-based game into the single-flag reference shape. Instead, capture and restore
**per `AudioSource`**, using whatever that source's `.mute` flag currently is as the source of truth
for "the user's setting" — this covers both models, since a slider-based game already keeps its truth
on `.mute`/volume and never needed a separate `isSound` bool:
```csharp
private List<AudioSource> allSources;
private readonly Dictionary<AudioSource, bool> preFocusMuteState = new Dictionary<AudioSource, bool>();
private bool isForceMuted = false;

internal void SetMuteAll(bool forceMute)
{
    if (forceMute == isForceMuted) return;
    isForceMuted = forceMute;

    foreach (var source in allSources)
    {
        if (source == null) continue;
        if (forceMute)
        {
            preFocusMuteState[source] = source.mute;
            source.mute = true;
        }
        else
        {
            source.mute = preFocusMuteState.TryGetValue(source, out bool prevMuted) ? prevMuted : source.mute;
        }
    }
}
```
For a single-flag game, the reference's `userMuted`-overwrite shape (below) is simpler and still
correct — use whichever shape already matches the game, as long as **both focus paths call the same
method** and it carries the reentrancy guard above.

### The reverse invariant (verify this too — hit in a real audit pass)
> **A stuck/stale forced-mute must never block the user's own mute/unmute button.**
> Games that implement muting as a *layered* flag (e.g. `source.mute = focusForceMuted ||
> categoryMuted`, rather than the reference's single `userMuted` overwrite) are exposed to this:
> if a spurious/unpaired blur signal sets `focusForceMuted = true` and the matching focus-regain
> signal is ever missed or delayed — routine in WebView embeds, where a native overlay or bridge
> event can fire a DOM `blur` without the player actually leaving the game — then
> `focusForceMuted` sticks `true` forever. From that point, toggling the in-game sound/music
> button changes `categoryMuted` but the OR'd expression keeps evaluating muted, so **the button
> visibly does nothing** until an unrelated, later focus/blur cycle happens to clear the stuck
> flag. This reads to a tester exactly like "toggling music back on doesn't work until I lose
> focus and regain focus."
>
> **Verify:** find every place a UI mute/unmute control is wired (button `onClick`, `SetSound`,
> `ToggleMute`, etc.) and confirm it clears any forced-mute/focus flag before or while applying the
> category state — an explicit user interaction is proof the game currently has real interactive
> focus, so it must always win immediately, regardless of what a stale blur/focus signal claims.
> The reference avoids this class of bug entirely by not layering a separate forced-mute flag —
> `SetMuteAll` is the single source of truth for both the user toggle and the focus path. If a
> target game instead layers a forced-mute on top of per-category mute (as is reasonable when a
> game splits mute into more than the reference's one `isSound` flag), that layered flag **must**
> be reset by the user-toggle code path, not only by a matching focus-regain event.

### The Pause/UnPause vs. mute-flag desync trap (why Pause/UnPause is retired — historical context)
> **If any code path calls `AudioSource.Pause()` on blur, every mute-toggling code path must be
> able to `UnPause()` it too — not just the code path that originally paused it.**
>
> This is the concrete failure that led to retiring `Pause()`-on-blur / `UnPause()`-on-focus as an
> allowed native/editor-path pattern (see "Do not use `AudioSource.Pause()`/`UnPause()`" above — it's
> a FAIL now, not an acceptable alternative). It creates a second, *independent* thing controlling
> whether audio is actually audible: the engine playback state (`isPlaying`/paused), separate from
> the `.mute` flag. If the game's user-facing mute/unmute toggle (`ToggleMute`, `SetMuteAll`, etc.)
> only ever sets `.mute` and never calls `UnPause()`, then a source that got `Pause()`d by a
> focus-loss event stays **silently paused** even after the user turns their sound/music setting back
> on — because flipping `.mute = false` does not resume a paused `AudioSource`. Only the *next*
> actual focus-regain event (whose handler calls `UnPause()`) makes it audible again. This produces
> the exact same "toggling sound/music back on doesn't work until I lose focus and regain focus"
> symptom as the reverse-invariant bug above, and the exact same "stuck silent after refocus" symptom
> as the reentrancy trap above, but from **three distinct causes** — check all three before concluding
> which one it is.
>
> **If you find this pattern in a target game:** don't patch it by adding `UnPause()` calls to every
> mute-toggling path (that was last revision's guidance) — convert the whole native/editor path to
> mute-flag toggling through the shared `SetMuteAll`-equivalent instead, per "What to look for" above.
> That removes the second state entirely rather than keeping it in sync by hand.

### Reference implementation
`AudioController.cs` (single-flag shape — add the reentrancy guard from above regardless of shape).
Note the split: `SetMuteAll` is the **focus-driven** method — both focus sources call it, and it never
touches `userMuted`. The **user-toggle** entry point (slider/button, wired via `ToggleAudio`/`SetSound`
in the reference) is a separate method that *does* write `userMuted`, and defers to it while a focus
mute is in effect:
```csharp
private bool isForceMuted = false;
private bool preFocusUserMuted;

// Focus-driven — called from BOTH OnFocusChanged (Check 2) and OnApplicationFocus below.
internal void SetMuteAll(bool forceMute)
{
    if (forceMute == isForceMuted) return;
    isForceMuted = forceMute;

    if (forceMute)
    {
        preFocusUserMuted = userMuted;
        foreach (var entry in entries) entry.source.mute = true;
    }
    else
    {
        foreach (var entry in entries) entry.source.mute = preFocusUserMuted;
    }
}

// User-toggle-driven — the sound/music button or slider callback.
internal void SetUserMute(bool mute)
{
    userMuted = mute;
    if (!isForceMuted)
        foreach (var entry in entries) entry.source.mute = mute;
}

// Native/editor focus path — calls the SAME method the WebGL OnFocusChanged path calls.
private void OnApplicationFocus(bool focus)
{
    SetMuteAll(!focus);
}
```

> `HandleFocusChange` (the socket timeout, Check 4) stays exclusive to the WebGL/JS `OnFocusChanged`
> path. Do **not** also call it from `OnApplicationFocus` — it isn't reliable enough inside a WebView
> to gate a network-affecting timer, so a game that only ships WebGL should treat the JS bridge as the
> single source of truth for the timeout, while treating audio muting as fed by both sources (audio
> muting is safe from either source firing spuriously; closing a socket on a false signal is not).

`UIManager.cs` (user toggle):
```csharp
private void SetSound(bool soundOn)
{
    isSound = soundOn;
    // SetUserMute(true) mutes; isSound==true means audio plays, so invoke with !isSound.
    ToggleAudio?.Invoke(!isSound);
    ApplySoundButtonVisibility();
}
```

`GameManager.cs` (wiring — note this wires the **user-toggle** method, not the focus-driven one):
```csharp
uIManager.ToggleAudio = audioController.SetUserMute;
```

### Common failure modes
- `OnApplicationFocus` restores with `false` (hard un-mute) instead of the user's stored setting → breaks the invariant.
- `SetMuteAll`/equivalent lacks the `isForceMuted` reentrancy guard while both focus paths are wired → the reentrancy trap above; audio gets stuck muted after the first refocus that receives duplicate blur/focus signals.
- `OnApplicationFocus` and `OnFocusChanged` call two different mechanisms (e.g. one mutes, the other `Pause()`s) instead of the same shared method → the two silencing states can desync; treat this as a FAIL and consolidate onto one mute-flag method.
- `ToggleAudio`/slider callbacks never wired to actually reach the `AudioSource`(s) → the sound button does nothing.
- Polarity inversion: passing `isSound` instead of `!isSound` (or the slider-equivalent) to a *mute* method → button is backwards.
- Game has no `OnApplicationFocus` wired to the shared mute method at all → editor/native testing and ordinary browser tab-switches (not just the WebView-embedded case) don't mute on blur. This is now a **FAIL**, not a partial/optional gap — wire it, calling the same method `OnFocusChanged` calls (see Check 2).
- A layered forced-mute flag (`focusForceMuted || categoryMuted`) that only gets cleared by a focus-regain event, never by the user-toggle code path → sound/music button silently stops working after any unpaired/stray blur signal, and only a later focus cycle fixes it. See "The reverse invariant" above.

---

## Check 4 — 60-second background timeout

### What to look for
On the socket manager: fields, a `HandleFocusChange(bool)` entry point (called from Check 2), and
a `FocusTimeoutCheck` coroutine that closes the socket after `maxBackgroundTime` (60s) away.

**`HandleFocusChange` must be called from the WebGL/JS `OnFocusChanged` path only — never from
Unity's native `OnApplicationFocus`.** This is asymmetric with Check 3's audio wiring on purpose:
`OnApplicationFocus` isn't reliable enough inside a WebView to gate a network-affecting timer, and
since these games ship WebGL-only, the JS bridge is the one signal actually trustworthy in
production. A spurious `OnApplicationFocus` firing (or not firing) should never start or clear the
background-close timer. Audio muting is safe to drive from both sources because a wrong mute call is
just a wrong mute call; a wrong socket close is a dropped session.

### Reference implementation
`SocketController.cs` — fields:
```csharp
private bool hasFocus = true;
private float focusLostTime = 0f;
private Coroutine focusCheckRoutine;
private float maxBackgroundTime = 60f;
private bool isExiting = false;
private bool isBeingDestroyed = false;
```

`HandleFocusChange`:
```csharp
internal void HandleFocusChange(bool focus)
{
    hasFocus = focus;

    if (!focus)
    {
        focusLostTime = Time.time;
        if (focusCheckRoutine == null && !isExiting && !isBeingDestroyed)
            focusCheckRoutine = StartCoroutine(FocusTimeoutCheck());
    }
    else
    {
        if (focusCheckRoutine != null)
        {
            StopCoroutine(focusCheckRoutine);
            focusCheckRoutine = null;
        }
    }
}
```

`FocusTimeoutCheck`:
```csharp
private IEnumerator FocusTimeoutCheck()
{
    while (!hasFocus && !isExiting && !isBeingDestroyed)
    {
        if (Time.time - focusLostTime >= maxBackgroundTime)
        {
            Debug.LogWarning("[SOCKET] Background timeout — closing connection");
            isConnected = false;
            ResetPingRoutine();

            if (manager != null)
            {
                try { manager.Close(); }
                catch (Exception e) { Debug.LogWarning($"[SOCKET] Focus close error: {e.Message}"); }
            }

            UiManager.DisconnectionPopup();
            focusCheckRoutine = null;
            yield break;
        }

        yield return new WaitForSecondsRealtime(1f);
    }

    focusCheckRoutine = null;
}
```

> `ResetPingRoutine()` / `isBeingDestroyed` (set in `OnDestroy`) / `isExiting` (set when closing)
> are Diamond Riches names — map to the target game's ping-stop and lifecycle guards. If the game
> has no ping routine, drop that line.

### Common failure modes
- `WaitForSecondsRealtime` replaced with `WaitForSeconds` → timer stalls when the tab is backgrounded (`Time.timeScale`/frame ticks may pause), so the 60s never elapses. **Must be Realtime.**
- No guard `focusCheckRoutine == null` before `StartCoroutine` → duplicate coroutines on rapid blur/focus.
- Routine not stopped on regained focus → socket closes even though the player came back in time.
- `manager.Close()` not wrapped in try/catch → an exception during teardown leaks the coroutine handle.
- Ping/heartbeat routine not stopped on timeout → phantom reconnection attempts after disconnect.
- Missing `isExiting`/`isBeingDestroyed` guards → coroutine runs during scene teardown and touches destroyed objects.

---

## Check 5 — `OnError(Error err)`

### What to look for
An error handler registered on the socket and differentiating **session-expired** from generic
errors, with WebGL-guarded messages to the JS host.

### Reference implementation
`SocketController.cs` — registration (in socket setup):
```csharp
GameSocket.On<Error>(SocketIOEventTypes.Error, OnError);
```

Handler:
```csharp
private void OnError(Error err)
{
    Debug.LogError("[ERROR] Socket error: " + err);
    if (!string.IsNullOrEmpty(err.message) && err.message.Contains("Session expired"))
    {
        Debug.LogWarning("Session expired detected");
        OnDisconnected();
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("session_expired");
#endif
    }
    else
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("error");
#endif
    }
}
```

### Common failure modes
- `OnError` never registered via `GameSocket.On<Error>(SocketIOEventTypes.Error, OnError)` → server errors silently ignored.
- No `"Session expired"` branch → expired sessions don't notify the host (`session_expired`) and the player sees a generic error.
- `SendCustomMessage` calls not wrapped in `#if UNITY_WEBGL && !UNITY_EDITOR` → editor/native build breaks.
- Session-expired branch omits `OnDisconnected()` → UI never shows the disconnect state.
- Null `err.message` not guarded → `NullReferenceException` on `.Contains`.

---

## Check 6 — Instant JS-side mute (WebAudio suspend in `.jslib`)

### What to look for
Inside `RegisterVisibilityChangeListener` in the game's `CustomJsLib.jslib`, the focus dispatcher
(`sendFocusToUnity`) must suspend/resume Unity's WebAudio context **directly in JS**, as its first
action, before the `SendMessage` handoff.

> **Why this exists:** a hidden browser tab or a backgrounded ReactNativeWebView throttles Unity's
> main loop (rAF pauses; timers clamp to ~1s+). If muting only travels through
> `SendMessage → C# OnFocusChanged → SetMuteAll`, the audio keeps playing for ~3s until the
> throttled loop processes it. Also, Unity's native `OnApplicationFocus` does **not** fire inside a
> WebView, so the C# native path can't cover APK. Suspending the AudioContext in the JS event
> handler stops sound instantly on every platform. This is not a `.cs` file — if you can't open it,
> mark UNVERIFIED and ask a human to confirm.

### Reference implementation
`CustomJsLib.jslib` — helper + first line of the dispatcher:
```js
function setUnityAudioSuspended(suspended) {
    try {
        var wa = (typeof WEBAudio !== 'undefined') ? WEBAudio
               : (typeof Module !== 'undefined' && Module.WEBAudio) ? Module.WEBAudio
               : null;
        if (!wa || !wa.audioContext) return;
        if (suspended) {
            if (wa.audioContext.state === 'running') wa.audioContext.suspend();
        } else {
            if (wa.audioContext.state === 'suspended') wa.audioContext.resume();
        }
    } catch (err) { console.warn('[JS] Unity audio suspend/resume failed:', err); }
}

function sendFocusToUnity(focused) {
    setUnityAudioSuspended(!focused);   // <-- instant; runs before Unity's throttled loop
    // ... existing SendMessage(gameObjectName, 'OnFocusChanged', focused ? '1' : '0') ...
}
```

### Why it's safe with the user's sound setting
`suspend()`/`resume()` only pause/unpause the whole context — they never touch the per-source
`.mute` flags Unity manages (Check 3). A game the user muted stays muted after `resume()`. The late
C# `OnFocusChanged` then reasserts the correct mute state; consistent, just no longer audible during
the gap. **No C# change is required for this check** — it is purely additive in the `.jslib`.

### Common failure modes
- `sendFocusToUnity` only calls `SendMessage` (no `setUnityAudioSuspended`) → 3s of audio on tab switch / APK background (the original bug this check exists to catch).
- `suspend`/`resume` polarity swapped (`setUnityAudioSuspended(focused)`) → mutes on return, plays on leave.
- Missing the `Module.WEBAudio` fallback and `WEBAudio` isn't global in that build → helper silently no-ops; confirm the audio actually stops in a real build.
- No `state === 'running'` / `'suspended'` guard → redundant suspend/resume calls (harmless, but noisy; guards preferred).

---

## Check 7 — Orientation change / responsive scaling (`SwitchDisplay`)

### What to look for
A component (role: **Orientation/scaling handler**, `OrientationChange` in the reference) that owns
the `RectTransform UIWrapper` + `CanvasScaler` and exposes a **`SwitchDisplay(string dimensions)`**
entry point. The host (React Native / browser) calls it via
`SendMessage(gameObjectName, 'SwitchDisplay', "<width>,<height>")` whenever the viewport
rotates or resizes; the editor simulates it with the Space key. It must:

1. **Debounce** the incoming call through a coroutine that waits `waitForRotation` seconds
   (`WaitForSecondsRealtime`, so a rotation while the tab is backgrounded still resolves), stopping
   any in-flight rotation coroutine first.
2. **Validate** the `"w,h"` payload (`Split(',')` → exactly 2 parts, `int.TryParse`, both `> 0`).
3. **Rotate** `UIWrapper` — `Quaternion.identity` for landscape, `Quaternion.Euler(0,0,-90)` for
   portrait — via a DOTween tween, killing the previous rotation tween first.
4. **Retune** `CanvasScaler.matchWidthOrHeight` by continuous interpolation between the width-scale
   and height-scale (log-space), clamped to `[0,1]`, tweened (not snapped), killing the previous
   match tween first. **Guard the `Log(heightScale/widthScale)` against the equal-scale case**
   (division by zero when `widthScale == heightScale`).
5. **Apply an initial orientation on boot** — a `Start()` (or equivalent) that calls the same
   `ApplyMatch` path with `Screen.width/height`, so the very first frame is already correct instead
   of waiting for the first host resize event.

> **Why the continuous match matters:** the older approach hard-coded `matchWidthOrHeight` into
> discrete aspect-ratio buckets (`>= 1.3 && < 1.4 → 0.27f`, …). Any device whose aspect fell between
> buckets, or outside the top bucket, got a visibly wrong scale. The log-interpolated formula covers
> every aspect ratio continuously, so no per-device tuning table is needed.

### Reference implementation
`OrientationChange.cs` (Age of Gods):
```csharp
private void Start()
{
  ApplyMatch(Screen.width, Screen.height);
}

private void SwitchDisplay(string dimensions)
{
  if (rotationRoutine != null) StopCoroutine(rotationRoutine);
  rotationRoutine = StartCoroutine(RotationCoroutine(dimensions));
}

private IEnumerator RotationCoroutine(string dimensions)
{
  yield return new WaitForSecondsRealtime(waitForRotation);
  string[] parts = dimensions.Split(',');
  if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height) && width > 0 && height > 0)
  {
    ApplyMatch(width, height);
  }
  else
  {
    Debug.LogWarning("Unity: Invalid format received in SwitchDisplay");
  }
}

private void ApplyMatch(int width, int height)
{
  isLandscape = width > height;

  Quaternion targetRotation = isLandscape ? Quaternion.identity : Quaternion.Euler(0, 0, -90);
  if (rotationTween != null && rotationTween.IsActive()) rotationTween.Kill();
  rotationTween = UIWrapper.DOLocalRotateQuaternion(targetRotation, transitionDuration).SetEase(Ease.OutCubic);

  float refW = ReferenceAspect.x;
  float refH = ReferenceAspect.y;

  float widthScale = (float)width / refW;
  float heightScale = (float)height / refH;

  float targetScale;
  if (isLandscape)
  {
    targetScale = Mathf.Min(widthScale, heightScale);
  }
  else
  {
    float portraitWidthScale = (float)height / refW;
    float portraitHeightScale = (float)width / refH;
    targetScale = Mathf.Min(portraitWidthScale, portraitHeightScale);
  }

  float targetMatch;
  if (Mathf.Abs(heightScale - widthScale) < 0.0001f)
  {
    targetMatch = 0.5f;
  }
  else
  {
    float logRatio = Mathf.Log(heightScale / widthScale);
    targetMatch = Mathf.Log(targetScale / widthScale) / logRatio;
    targetMatch = Mathf.Clamp01(targetMatch);
  }

  if (matchTween != null && matchTween.IsActive()) matchTween.Kill();
  matchTween = DOTween.To(() => CanvasScaler.matchWidthOrHeight, x => CanvasScaler.matchWidthOrHeight = x, targetMatch, transitionDuration).SetEase(Ease.InOutQuad);
}
```

The editor simulation hook (keep it guarded):
```csharp
#if UNITY_EDITOR
private void Update()
{
  if (Input.GetKeyDown(KeyCode.Space))
    SwitchDisplay(Screen.width + "," + Screen.height);
}
#endif
```

### Common failure modes
- `SwitchDisplay` renamed, or the component sits on a GameObject whose name differs from what the
  host targets → `SendMessage(gameObjectName, 'SwitchDisplay', …)` silently no-ops and the game never
  rotates. (Unity's `SendMessage` **can** reach a `private` method, so private is fine — the *name*
  and the *GameObject* are what must match.)
- No `Start()`/initial `ApplyMatch` → the game boots in the wrong orientation until the first resize.
- Still using the old discrete aspect-ratio buckets instead of the continuous log formula → wrong
  scale on aspect ratios that fall between/outside the buckets. **Replace with the formula above.**
- Missing the `Mathf.Abs(heightScale - widthScale) < 0.0001f` guard → `Log(1)=0` denominator →
  `NaN`/`Infinity` fed into the match tween on a square-ish viewport.
- `WaitForSeconds` instead of `WaitForSecondsRealtime` → a rotation that arrives while the tab is
  backgrounded (`Time.timeScale`/frame ticks paused) never resolves.
- Previous rotation coroutine / rotation tween / match tween not stopped/killed before starting a new
  one → overlapping tweens fight and the UI jitters or lands on a stale value during rapid rotations.
- Match value snapped (`CanvasScaler.matchWidthOrHeight = targetMatch` directly) instead of tweened →
  visible pop instead of a smooth transition (acceptable functionally, but off-spec).
- No `Mathf.Clamp01` on `targetMatch` → out-of-range match value from an extreme aspect ratio.

---

## Check 8 — WebGL template canvas fit (`resizeCanvas` + resize/orientationchange listeners)

### What to look for
The game's WebGL template (`Assets/WebGLTemplates/<template>/index.html`, `custom` in the reference)
must have a `resizeCanvas()` that sizes the canvas to the **visible** viewport and re-runs on resize
and rotation. Specifically:

1. A `resizeCanvas()` that reads `window.visualViewport` (the accurate visible area on iOS) with a
   `window.innerWidth/innerHeight` fallback, and applies the size to `documentElement` / `body` /
   the Unity canvas.
2. It is registered — as a **function reference**, not called — on `window` `resize` and
   `orientationchange`, plus the `visualViewport` `resize`/`scroll` block and the scroll-lock/gesture
   handlers (`window` `scroll`, `document` `touchmove` / `gesturestart` / `gesturechange`), and run
   once via `window.addEventListener('load', resizeCanvas)`.
3. The Unity template macros are **intact** — `{{{ LOADER_FILENAME }}}`, `{{{ JSON.stringify(PRODUCT_NAME) }}}`,
   etc. — and the `#if …/#endif` platform blocks sit at **column 0**.

> *Note: this is an `.html` template asset, not a `.cs` file — safe to read and edit. If the template
> doesn't exist yet, create it from the reference below.*

### Reference implementation
`Assets/WebGLTemplates/custom/index.html` (Age of Gods):
```html
<!DOCTYPE html>
<html lang="en-us">

<head>
  <meta charset="utf-8">
  <meta http-equiv="Content-Type" content="text/html; charset=utf-8">
  <title>{{{ PRODUCT_NAME }}}</title>
  <style>
    body,
    html {
      margin: 0;
      padding: 0;
      overflow: hidden;
      height: 100%;
      width: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      background-color: black;
    }

    #unity-canvas {
      height: 100%;
      width: 100%;
    }

    #loading-screen {
      position: absolute;
      width: 100%;
      height: 100%;
    }
  </style>
</head>

<body>

  <canvas id="unity-canvas" tabindex="-1"></canvas>
  <script>
    const canvas = document.querySelector("#unity-canvas");

    function resizeCanvas() {
      // visualViewport is the accurate visible area on iOS; fall back to innerWidth/Height.
      var vv = window.visualViewport;
      var w = Math.round(vv ? vv.width : window.innerWidth);
      var h = Math.round(vv ? vv.height : window.innerHeight);
      window.scrollTo(0, 0);
      document.documentElement.style.width = w + "px";
      document.documentElement.style.height = h + "px";
      document.body.style.width = w + "px";
      document.body.style.height = h + "px";
      canvas.style.width = w + "px";
      canvas.style.height = h + "px";
    }

    window.addEventListener('resize', resizeCanvas);
    window.addEventListener('orientationchange', resizeCanvas);
    if (window.visualViewport) {
      window.visualViewport.addEventListener('resize', resizeCanvas);
      window.visualViewport.addEventListener('scroll', function () { window.scrollTo(0, 0); });
    }
    // Scroll-lock: CSS touch-action alone won't stop iOS pinch-pan; non-passive touchmove does.
    // Unity still receives the touch events (preventDefault only cancels browser scroll/zoom).
    window.addEventListener('scroll', function () { window.scrollTo(0, 0); }, { passive: true });
    document.addEventListener('touchmove', function (e) { e.preventDefault(); }, { passive: false });
    document.addEventListener('gesturestart', function (e) { e.preventDefault(); });
    document.addEventListener('gesturechange', function (e) { e.preventDefault(); });

    var buildUrl = "Build";
    var loaderUrl = buildUrl + "/{{{ LOADER_FILENAME }}}";
    var config = {
      dataUrl: buildUrl + "/{{{ DATA_FILENAME }}}",
      frameworkUrl: buildUrl + "/{{{ FRAMEWORK_FILENAME }}}",
#if USE_THREADS
      workerUrl: buildUrl + "/{{{ WORKER_FILENAME }}}",
#endif
#if USE_WASM
      codeUrl: buildUrl + "/{{{ CODE_FILENAME }}}",
#endif
#if MEMORY_FILENAME
      memoryUrl: buildUrl + "/{{{ MEMORY_FILENAME }}}",
#endif
#if SYMBOLS_FILENAME
      symbolsUrl: buildUrl + "/{{{ SYMBOLS_FILENAME }}}",
#endif
      streamingAssetsUrl: "StreamingAssets",
      companyName: {{{ JSON.stringify(COMPANY_NAME) }}},
      productName: {{{ JSON.stringify(PRODUCT_NAME) }}},
      productVersion: {{{ JSON.stringify(PRODUCT_VERSION) }}},
    };


    var script = document.createElement("script");
    script.src = loaderUrl;
    script.onload = () => {
      createUnityInstance(canvas, config, (progress) => {
      }).then((unityInstance) => {
      }).catch((message) => {
        alert(message);
      });
    };

    document.body.appendChild(script);
    window.addEventListener('load', resizeCanvas);
  </script>
  <script>
    window.focus();
  </script>

</body>

</html>
```

### Common failure modes
- **Mangled template macros** — an HTML/JS auto-formatter inserts spaces into `{{{ … }}}`, turning
  `{{{ JSON.stringify(PRODUCT_NAME) }}}` into `{ { { JSON.stringify(PRODUCT_NAME) } } }`. Unity's
  template substitution then fails and the build ships broken `config` values. Grep for `{ { {` — it
  must return nothing.
- **`load` handler called instead of registered** — `window.addEventListener('load', resizeCanvas())`
  runs `resizeCanvas` immediately and registers its `undefined` return as the listener. Must be
  `resizeCanvas` (a reference), not `resizeCanvas()`.
- **Indented preprocessor** — a formatter indents the `#if`/`#endif` platform blocks. Restore them to
  column 0 so Unity's template preprocessor sees them.
- **`resizeCanvas` / listeners missing** — the canvas never refits after a rotation or host resize, so
  the game renders letterboxed or clipped.

---

## Check 9 — No blanket console suppression in the WebGL template

### What to look for
The game's WebGL template (`Assets/WebGLTemplates/<template>/index.html`) must **not** contain a
script block that overrides `console.log` / `console.warn` / `console.error` with no-op functions.
This pattern is sometimes pasted in to "quiet down" the browser console for a build, but it
silently swallows every Unity/JS diagnostic — including the `[JS]` logs this very runbook's checks
(1, 2, 6) rely on to verify focus/audio behavior in a live build, and any `Debug.LogError`/`LogWarning`
routed through `SendLogToReactNative` (see `JSFunctCalls.HandleLog`).

### Reference implementation
There is no reference implementation — the correct state is **absence** of this block. Diamond
Riches' `custom/index.html` has no console-override script at all; `console.log`/`warn`/`error`
are left as the browser's native implementations.

### Common failure modes
- A block like this anywhere in the template:
  ```html
  <script type="text/javascript">
      console.log = function() {};
      console.warn = function() {};
      console.error = function() {};
  </script>
  ```
  silently discards every subsequent console message for the life of the page — including errors
  unrelated to whatever the suppression was originally added for.
- Partial suppression (only `console.log` overridden, `warn`/`error` left alone, or vice versa) is
  still a FAIL — any override of these globals defeats debugging in a live/shared build.
- Grep the template for `console.log = function` / `console.warn = function` / `console.error = function`
  — any match is a FAIL regardless of where in the file it sits.

---

## Check 10 — Balance sync socket event (`balance:sync`)

### What to look for
The backend can push an out-of-band balance update at any time (not just as part of a spin/gamble/
bonus result). Payload shape from the backend:
```
name: "balance:sync",
payload: {
    userId: string;
    gameId: string;
    balance: number;
}
```
`userId`/`gameId` identify the player/game to the backend and are **not used client-side** — ignore
them. Only `balance` matters. Three linked pieces must exist:
- A `[Serializable]` DTO with a single `balance` field (reference: `BalanceSyncPayload`).
- A registration + handler on the socket manager that deserializes the raw payload and writes the new
  value into **whichever field the game's spin gate already reads** — not a parallel/duplicate field.
  Find that field first (grep for what the low-balance check / spin-start gate compares against)
  before wiring this in; every game in this line names it differently.
- A call into the UI layer that snaps the balance display to the new value immediately (not tweened —
  this isn't a spin-result animation) and re-runs the low-balance check, since an external balance
  push can move the player from "can spin" to "can't spin" or vice versa outside of any spin flow.

### Reference implementation
`SocketIOManager.cs` (RockClimber) — registration, next to the other `gameSocket.On<string>(...)` calls:
```csharp
gameSocket.On<string>("balance:sync", OnBalanceSync);
```
DTO, next to the existing `Player` class:
```csharp
[Serializable]
public class BalanceSyncPayload
{
  public double balance;
}
```
Handler — deserializes the raw string via `JsonConvert.DeserializeObject`, the same safe path already
used for every other complex payload in this file (**not** a typed `On<BalanceSyncPayload>(...)`
registration — that would depend on the Socket.IO plugin's own decoder supporting arbitrary
game-defined classes, which isn't confirmed):
```csharp
private void OnBalanceSync(string data)
{
  BalanceSyncPayload syncPayload = JsonConvert.DeserializeObject<BalanceSyncPayload>(data);
  if (syncPayload == null) return;

  if (playerdata == null) playerdata = new Player();
  playerdata.balance = syncPayload.balance;

  slotManager.UpdateBalanceDisplay(syncPayload.balance);
}
```
`SlotBehaviour.cs` (RockClimber) — snaps the UI and re-checks the low-balance gate:
```csharp
internal void UpdateBalanceDisplay(double newBalance)
{
  currentBalance = newBalance;
  if (Balance_text) Balance_text.text = newBalance.ToString("F3");
  CompareBalance();
}
```

### Common failure modes
- Writing the new balance into a field the spin gate doesn't actually read (e.g. a display-only cache)
  → the balance text updates but the next spin still checks the stale value.
- Tweening the balance display like a spin-result win → looks like the player just won/lost money
  instead of an external correction.
- Not re-running the low-balance check after the sync → a balance pushed below the current bet
  doesn't surface the low-balance popup until the next spin attempt.
- Deserializing `userId`/`gameId` into the DTO and treating a missing/mismatched one as an error →
  the spec says ignore them; the DTO should only declare `balance`.

---

## Check 11 — No high-frequency/heartbeat debug logging

### What to look for
The socket manager's ping/pong heartbeat (`SendPing`/`PingCheck`/`OnPongReceived` in the reference,
or whatever the game calls its keepalive loop) runs on a short fixed interval — `pingInterval`, ~2s
in the reference — for as long as the socket is connected, i.e. the entire play session. Any
`Debug.Log` (not `LogWarning`/`LogError`, which are for genuine problems) inside that loop or its
response handler fires every single tick and floods the console within minutes, burying real
diagnostics — including the `[JS]`/`[ERROR]`/`[SOCKET]` logs the other checks in this doc rely on to
verify behavior in a live build. This was caught in a real audit pass on `SocketIOManager.cs`
(`PingCheck`, `OnPongReceived`) and had to be commented out.

**Verify:** find the ping/heartbeat loop and its response handler; confirm neither logs on the
success path. `Debug.LogWarning`/`Debug.LogError` on the *failure* path (missed pong, disconnect) are
fine and expected — those are genuine, low-frequency diagnostics, not noise.

### Reference implementation
`SocketIOManager.cs` (RockClimber) — the per-tick and per-pong logs are removed/commented, the
failure-path warning/error logs are kept:
```csharp
private void OnPongReceived(string data)
{
  waitingForPong = false;
  missedPongs = 0;
  lastPongTime = Time.time;
}

private IEnumerator PingCheck()
{
  while (true)
  {
    if (missedPongs == 0)
    {
      uiManager.CheckAndClosePopups();
    }

    if (waitingForPong)
    {
      if (missedPongs == 2)
      {
        uiManager.ReconnectionPopup();
      }
      missedPongs++;
      Debug.LogWarning($"⚠️ Pong missed #{missedPongs}/{MaxMissedPongs}");

      if (missedPongs >= MaxMissedPongs)
      {
        Debug.LogError("❌ Unable to connect to server — 5 consecutive pongs missed.");
        isConnected = false;
        uiManager.DisconnectionPopup();
        yield break;
      }
    }

    waitingForPong = true;
    lastPongTime = Time.time;
    SendDataWithNamespace("ping");
    yield return new WaitForSeconds(pingInterval);
  }
}
```

### Common failure modes
- `Debug.Log` left on the ping-sent / pong-received success path → console fills at ~1 line per
  `pingInterval` for the whole session, making it useless for spotting an actual error live.
- The fix applied by deleting the log call outright vs. commenting it out — either is acceptable, but
  don't replace it with a lower-frequency version (e.g. "log every 10th ping") — that's still
  unrequested behavior the reference doesn't have; just remove it.
- Other periodic loops elsewhere in the game (auto-spin ticking, animation frame callbacks, resize
  handlers) carrying the same pattern — the fix here is ping/pong-specific, but the same review
  question ("does this log on every tick of a loop that runs for the whole session?") applies to any
  such loop found elsewhere.

---

## Verdict template (use per check)

```
Check N — <name>
Status: PASS | FAIL | MISSING | UNVERIFIED
Location: <file>:<line>   (or "not found")
Notes: <what matched / what's wrong>
Fix (if FAIL/MISSING): <adapted code block>
```

## Final report

| # | Check | Status | Location | Action taken |
|---|---|---|---|---|
| 1 | Visibility listener registration (.jslib + wrapper + Awake) | | | |
| 2 | `OnFocusChanged` public callback | | | |
| 3 | Audio mute/unmute honors user sound setting (both focus paths call one shared mute-flag method, reentrancy-guarded; invariant + reverse invariant; no Pause/UnPause) | | | |
| 4 | 60s background timeout (`WaitForSecondsRealtime`) | | | |
| 5 | `OnError` (session-expired vs generic) | | | |
| 6 | Instant JS-side mute (WebAudio suspend in `.jslib`) | | | |
| 7 | Orientation change / responsive scaling (`SwitchDisplay` + continuous match) | | | |
| 8 | WebGL template canvas fit (`resizeCanvas` + listeners, macros intact) | | | |
| 9 | No blanket console suppression in WebGL template | | | |
| 10 | Balance sync socket event (`balance:sync`) | | | |
| 11 | No high-frequency/heartbeat debug logging | | | |

---

*Reference: [FEATURE_PORTING_GUIDE.md](FEATURE_PORTING_GUIDE.md) has additional step-by-step porting
detail for the browser-focus feature. This doc is the verification/audit counterpart and additionally
covers the audio/sound-setting interaction and the `OnError` handler.*
