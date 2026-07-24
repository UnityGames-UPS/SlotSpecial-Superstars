# Focus / Visibility / Audio-Mute / Timeout / OnError / Orientation — Audit Runbook

> **Who this is for:** an LLM auditing ONE of our ~70 slot games. Every game shares this
> lifecycle logic but with different class/method names. Your job: verify each of the 9 checks
> below against the reference contract, report **PASS / FAIL / MISSING** with a `file:line`, and
> where it's FAIL or MISSING, paste the corrected implementation.
>
> The reference game is **Diamond Riches**. All reference code below is copied verbatim from it.
> (Exception: Check 7 — Orientation change — uses **Age of Gods** as its reference, since that game
> carries the current responsive-scaling implementation.)

---

## How to use this doc

1. Read all 9 checks first so you understand how the pieces interlock (they cooperate — a
   correct `OnFocusChanged` is useless if the visibility listener was never registered).
2. For each check: locate the equivalent code in the target game, compare against **What to look
   for** and the **Reference implementation**, then run through **Common failure modes**.
3. Emit a verdict per check using the template, then fill in the **Final report** table at the end.
4. If a piece is MISSING or FAIL, output the exact code to add/fix, adapted to the target game's
   names (see the naming map).

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
| Mute-all method | `SetMuteAll(bool)` | `SetMute(bool)`, `PauseAllAudio()`/`ResumeAudio()` |
| Orientation/scaling handler | `OrientationChange` | `ResolutionManager`, `AspectController`, `ScreenOrientationHandler` |
| Orientation entry point | `SwitchDisplay(string)` | `OnResize(string)`, `SetOrientation(string)` |
| WebGL template | `Assets/WebGLTemplates/custom/index.html` | `Assets/WebGLTemplates/<template>/index.html` |

If a game splits mute into `PauseAllAudio()` / `ResumeAudio()` instead of a single
`SetMuteAll(bool)`, that is acceptable **only if** resume still respects the user's sound setting
(see Check 3's invariant).

### The 9 checks at a glance

1. **Visibility listener registration** — `.jslib` + `RegisterVisibilityListener` wrapper, called from `Awake`.
2. **`OnFocusChanged` callback** — `public`, routes to audio + socket.
3. **Audio mute/unmute honoring the runtime sound setting** — blur mutes, focus restores to user's choice.
4. **60-second background timeout** — closes the socket if the player stays away too long.
5. **`OnError(Error err)`** — session-expired vs generic error handling.
6. **Instant JS-side mute** — `.jslib` suspends the WebAudio context on blur so audio stops immediately.
7. **Orientation change / responsive scaling** — `SwitchDisplay` rotates the UI and retunes the CanvasScaler match on resize.
8. **WebGL template canvas fit** — `custom/index.html` has a clean `resizeCanvas` + resize/orientationchange listeners; template macros intact.
9. **No blanket console suppression in the WebGL template** — `custom/index.html` must not override `console.log`/`warn`/`error` to no-ops.

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

## Check 3 — Audio mute/unmute honoring the runtime sound setting

### What to look for
Two paths mute audio, and **both must restore to the user's chosen setting on focus**, never
force-unmute:
- **WebGL path** — via `OnFocusChanged` → `SetMuteAll(focused ? !isSound : true)` (Check 2).
- **Editor/native path** — `AudioController.OnApplicationFocus(bool)` mutes on blur, restores to
  `userMuted` on focus.

And the user's sound toggle must feed `userMuted`:
- `isSound` is the user's runtime flag; `SetSound(bool)` updates it and calls `ToggleAudio?.Invoke(!isSound)`.
- `GameManager` wires `uIManager.ToggleAudio = audioController.SetMuteAll`.
- `SetMuteAll(bool mute)` stores `userMuted = mute` before muting sources.

### The invariant (verify this explicitly)
> **Regaining focus must never un-mute a game the user muted.**
> On focus, the WebGL path restores to `!isSound` and the native path restores to `userMuted` —
> both equal the user's current choice. On blur, both force `mute = true` regardless of setting.

### Reference implementation
`AudioController.cs`:
```csharp
internal void SetMuteAll(bool mute)
{
    userMuted = mute;
    foreach (var entry in entries) entry.source.mute = mute;
}

private void OnApplicationFocus(bool focus)
{
    foreach (var entry in entries)
    {
        entry.source.mute = focus ? userMuted : true;
    }
}
```

`UIManager.cs` (user toggle):
```csharp
private void SetSound(bool soundOn)
{
    isSound = soundOn;
    // SetMuteAll(true) mutes; isSound==true means audio plays, so invoke with !isSound.
    ToggleAudio?.Invoke(!isSound);
    ApplySoundButtonVisibility();
}
```

`GameManager.cs` (wiring):
```csharp
uIManager.ToggleAudio = audioController.SetMuteAll;
```

### Common failure modes
- `OnApplicationFocus` restores with `false` (hard un-mute) instead of `userMuted` → breaks the invariant.
- `SetMuteAll` mutes sources but forgets to store `userMuted` → the native focus path later restores to a stale value.
- `ToggleAudio` never wired in the manager (`GameManager`/bootstrap) → the sound button does nothing.
- Polarity inversion: passing `isSound` instead of `!isSound` to a *mute* method → button is backwards.
- Game has no `OnApplicationFocus` at all → editor/native builds don't mute on blur (WebGL still works via `OnFocusChanged`; flag as partial FAIL if native/editor focus muting is expected).

---

## Check 4 — 60-second background timeout

### What to look for
On the socket manager: fields, a `HandleFocusChange(bool)` entry point (called from Check 2), and
a `FocusTimeoutCheck` coroutine that closes the socket after `maxBackgroundTime` (60s) away.

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
| 3 | Audio mute/unmute honors user sound setting (both paths + invariant) | | | |
| 4 | 60s background timeout (`WaitForSecondsRealtime`) | | | |
| 5 | `OnError` (session-expired vs generic) | | | |
| 6 | Instant JS-side mute (WebAudio suspend in `.jslib`) | | | |
| 7 | Orientation change / responsive scaling (`SwitchDisplay` + continuous match) | | | |
| 8 | WebGL template canvas fit (`resizeCanvas` + listeners, macros intact) | | | |
| 9 | No blanket console suppression in WebGL template | | | |

---

*Reference: [FEATURE_PORTING_GUIDE.md](FEATURE_PORTING_GUIDE.md) has additional step-by-step porting
detail for the browser-focus feature. This doc is the verification/audit counterpart and additionally
covers the audio/sound-setting interaction and the `OnError` handler.*
