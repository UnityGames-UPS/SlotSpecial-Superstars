# Feature Porting Guide

Two features to implement in the target game:

1. **Browser Focus Toggle** — mutes audio and starts a disconnect timer when the player tabs away or backgrounds the app.
2. **Startup Popup (show-once)** — shows a popup the first time the player opens the game; never shows again on subsequent opens.

---



- Unity 6 WebGL project with a `JSFunctCalls.cs` wrapper script and a `CustomJsLib.jslib` plugin.
- An `AudioManager` component with `PauseAllAudio()` and `ResumeAudio()` internal methods.
- A `SocketIOManager` (or equivalent) component that manages the socket connection lifecycle.
- DOTween is **not** required for the startup popup in this game (close is plain `SetActive(false)`).

---

## Feature 1: Browser Focus Toggle

### What it does

Detects when the browser tab/window loses or regains focus and:
- Pauses all audio on blur/hide.
- Resumes audio on focus/visible.
- Starts a background-timeout coroutine in `SocketIOManager`; if the player stays away too long, the socket is closed and a disconnect popup is shown.

### Step 1 — Add JS functions to `CustomJsLib.jslib`

Add these two functions inside the `mergeInto(LibraryManager.library, { ... })` block:

```js
RegisterVisibilityChangeListener: function(gameObjectNamePtr) {
  var gameObjectName = UTF8ToString(gameObjectNamePtr);
  console.log('[JS] RegisterVisibilityChangeListener called for GameObject:', gameObjectName);

  function sendFocusToUnity(focused) {
      try {
          var value = focused ? '1' : '0';
          if (typeof SendMessage === 'function') {
              SendMessage(gameObjectName, 'OnFocusChanged', value);
          } else if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
              unityInstance.SendMessage(gameObjectName, 'OnFocusChanged', value);
          } else {
              console.warn('[JS] Unity SendMessage not available for focus change');
          }
          console.log('[JS] Sent focus state to Unity: ' + value);
      } catch (err) {
          console.error('[JS] Error sending focus message to Unity:', err);
      }
  }

  window._unityVisibilityCallback = function() {
      var hidden = document.hidden || document.webkitHidden;
      console.log('[JS] visibilitychange fired. Hidden:', hidden);
      sendFocusToUnity(!hidden);
  };

  window._unityWindowBlurCallback = function() {
      console.log('[JS] window blur fired');
      sendFocusToUnity(false);
  };

  window._unityWindowFocusCallback = function() {
      console.log('[JS] window focus fired');
      sendFocusToUnity(true);
  };

  // Remove before re-adding to avoid duplicates
  document.removeEventListener('visibilitychange',       window._unityVisibilityCallback);
  document.removeEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
  window.removeEventListener('blur',  window._unityWindowBlurCallback);
  window.removeEventListener('focus', window._unityWindowFocusCallback);

  document.addEventListener('visibilitychange',       window._unityVisibilityCallback);
  document.addEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
  window.addEventListener('blur',  window._unityWindowBlurCallback);
  window.addEventListener('focus', window._unityWindowFocusCallback);

  console.log('[JS] Visibility/focus event listeners registered for:', gameObjectName);
},
```

> The callback name `OnFocusChanged` and the argument values `'1'` (focused) / `'0'` (blurred) are the contract Unity expects. Do not change them.

### Step 2 — Add the DllImport and wrapper method to `JSFunctCalls.cs`

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

### Step 3 — Register the listener and handle the callback in your UI/Manager script

In whichever MonoBehaviour holds references to `JSFunctCalls` and `AudioManager` (e.g. `UIManager`), add the following:

**In `Awake()` (after `jsFunctCalls` is assigned):**
```csharp
if (jsFunctCalls != null)
    jsFunctCalls.RegisterVisibilityListener(gameObject.name);
```

> Pass `gameObject.name` — the JS layer calls `SendMessage(gameObjectName, 'OnFocusChanged', value)`, so the receiver **must** be the same GameObject.

**Add the public callback method (must be `public`, not `internal`, for `SendMessage` to reach it):**
```csharp
public void OnFocusChanged(string value)
{
    bool focused = value == "1";
    Debug.Log("UNITY FOCUS CHANGED: " + value + " (focused: " + focused + ")");
    if (focused)
        audioController?.ResumeAudio();
    else
        audioController?.PauseAllAudio();
    socketManager?.HandleFocusChange(focused);
}
```

### Step 4 — Add focus-timeout logic to `SocketIOManager`

Add these fields at class level:

```csharp
private bool hasFocus = true;
private float focusLostTime = 0f;
private Coroutine focusCheckRoutine;
private float maxBackgroundTime = 60f; // seconds before disconnect; adjust as needed
```

Add this method:

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

private IEnumerator FocusTimeoutCheck()
{
    while (!hasFocus && !isExiting && !isBeingDestroyed)
    {
        if (Time.time - focusLostTime >= maxBackgroundTime)
        {
            Debug.LogWarning("[SOCKET] Background timeout");
            isConnected = false;
            // stop ping routine if you have one

            if (manager != null)
            {
                try { manager.Close(); }
                catch (Exception e) { Debug.LogWarning($"[SOCKET] Focus close error: {e.Message}"); }
            }

            uiManager.OpenDisconnectPopup();
            focusCheckRoutine = null;
            yield break;
        }

        yield return new WaitForSecondsRealtime(1f);
    }

    focusCheckRoutine = null;
}
```

> Replace `isExiting`, `isBeingDestroyed`, `manager`, and `uiManager.OpenDisconnectPopup()` with the equivalent names in the target game. If the target game has a ping/pong routine, stop it when the timeout fires too.

---

## Feature 2: Startup Popup (show-once, no "do not show again" toggle)

### What it does

Shows a popup the **first time** the player opens the game. On the next session (and every session after), the popup is skipped. Closing the popup just calls `SetActive(false)` — no scale animations. No "do not show again" checkbox.

The popup is triggered after the server's `game:init` event is processed (i.e. once the game is ready and the player data is loaded).

### Step 1 — Create the popup prefab/GameObject in Unity

- Add a UI panel named e.g. `StartupPanel` somewhere in the Canvas hierarchy.
- Add a **Close Button** as a child of that panel.
- Make sure `StartupPanel` starts **inactive** in the scene (it will be shown at runtime when needed).

### Step 2 — Add serialized fields to your UI/Manager script

```csharp
[SerializeField] private GameObject StartupPanel;
[SerializeField] private Button CloseStartupPanelBtn;
```

### Step 3 — Add the PlayerPrefs key and startup logic

```csharp
private const string HasSeenStartupKey = "hasSeenStartup";
```

**Check method (called before showing):**
```csharp
internal bool ShouldShowStartupGuide()
{
    return PlayerPrefs.GetInt(HasSeenStartupKey, 0) == 0;
}
```

**Open method (called from SocketIOManager after game:init):**
```csharp
internal void OpenStartupGuidePopup()
{
    PlayerPrefs.SetInt(HasSeenStartupKey, 1);
    PlayerPrefs.Save();
    if (StartupPanel != null)
        StartupPanel.SetActive(true);
}
```

> Writing the flag **when opening** (not when closing) means even if the player force-quits without closing the popup, they still won't see it again.

### Step 4 — Wire the close button in `Awake()` / `AssignButtonListeners()`

```csharp
if (CloseStartupPanelBtn) CloseStartupPanelBtn.onClick.RemoveAllListeners();
if (CloseStartupPanelBtn) CloseStartupPanelBtn.onClick.AddListener(() =>
{
    if (StartupPanel != null)
        StartupPanel.SetActive(false);
});
```

No DOTween, no scale animation — just a plain `SetActive(false)`.

### Step 5 — Trigger the popup after `game:init` in `SocketIOManager`

After you finish processing the `game:init` server event and calling your equivalent of `uiManager.OnInit(...)`:

```csharp
if (uiManager != null && uiManager.ShouldShowStartupGuide())
{
    uiManager.OpenStartupGuidePopup();
}
```

### Step 6 — Initialize `StartupPanel` state at startup

In `Awake()` (or wherever you initialize other popups), make sure the panel starts hidden:

```csharp
if (StartupPanel != null)
    StartupPanel.SetActive(false);
```

---

## Editor Wiring Checklist

| Field | Where to assign |
|---|---|
| `StartupPanel` | Drag the startup popup root GameObject |
| `CloseStartupPanelBtn` | Drag the close Button inside `StartupPanel` |
| `jsFunctCalls` | Drag the GameObject holding `JSFunctCalls.cs` |
| `audioController` | Drag the AudioManager |
| `socketManager` | Drag the SocketIOManager |

> The `OnFocusChanged` receiver must be on the **same GameObject** you pass to `RegisterVisibilityListener`. In practice this is the GameObject holding the UI manager script — pass `gameObject.name` from `Awake()`.
