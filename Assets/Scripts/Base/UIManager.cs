using System.Collections;
using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
public class UIManager : MonoBehaviour
{
  [SerializeField] private SocketController socketController;
  [SerializeField] private GameManager gameManager;

  [Header("Paytable Popup")]
  [SerializeField] internal Button paytable_Button;
  [SerializeField] private GameObject payTablePopup_Object;
  [SerializeField] private Button paytableExit_Button;
  [SerializeField] private Button paytableBgExit_Button;

  [Header("Paytable Texts")]
  [SerializeField] private SymbolPayoutTexts[] SymbolsTexts;

  [Header("Sound Toggle")]
  [SerializeField] private Button SoundON_Button;
  [SerializeField] private Button SoundOFF_Button;
  private bool isSound = true;

  [Header("low balance popup")]
  [SerializeField] private GameObject LowBalancePopup_Object;
  [SerializeField] private Button Close_Button;

  [Header("disconnection popup")]
  [SerializeField] private GameObject DisconnectPopup_Object;
  [SerializeField] private Button CloseDisconnect_Button;

  [Header("Reconnection Popup")]
  [SerializeField] private GameObject ReconectionPopup_Object;

  [Header("Quit Popup")]
  [SerializeField] private GameObject QuitPopupObject;
  [SerializeField] private Button GameExit_Button;
  [SerializeField] private Button no_Button;
  [SerializeField] private Button yes_Button;
  private bool isExit = false;
  [Header("JS / Audio")]
  [SerializeField] private JSFunctCalls jsFunctCalls;
  [SerializeField] private AudioController audioController;
  [Header("player texts")]
  [SerializeField] private TMP_Text[] winDigits; // per-digit WIN HUD (left-to-right, dot excluded)
  [SerializeField] private TMP_Text playerBalance;

  [Header("Win Celebration - Timing")]
  [SerializeField] private float winThreshold = 7f;        // win >= winThreshold * totalBet => big win
  [SerializeField] private float valueLerpDuration = 1.5f; // low-win count-up duration 0 -> winValue
  [SerializeField] private float bigWinValueLerpDuration = 3f; // big-win count-up duration
  [SerializeField] private float redFadeInDuration = 0.4f;
  [SerializeField] private float yellowFadeInDuration = 0.4f;
  [SerializeField] private float postLerpHold = 2f;        // wait after lerp before fading out
  [SerializeField] private float coinFadeOutDuration = 0.4f;
  [SerializeField] private float panelFadeOutDuration = 0.5f;

  [Header("Win Celebration - WinPanel")]
  [SerializeField] private CanvasGroup winPanelCanvasGroup;
  [SerializeField] private RectTransform winPanelBG;       // BG image: scaled + moved in Y
  [SerializeField] private float bgHiddenY;                // reset/start anchored Y
  [SerializeField] private float bgShowingY;               // shown anchored Y
  [SerializeField] private float bgScaleUpDuration = 0.4f;
  [SerializeField] private float bgMoveDuration = 0.4f;
  [SerializeField] private Ease bgScaleEase = Ease.OutBack;
  [SerializeField] private Ease bgMoveEase = Ease.OutCubic;
  [SerializeField] private TMP_Text[] winValueTexts;       // panel per-digit array (lerps with HUD)

  [Header("Win Celebration - Column Overlays")]
  [SerializeField] private CanvasGroup redCanvasGroup;     // low-win glow (fades in, stays on)
  [SerializeField] private CanvasGroup yellowCanvasGroup;  // big-win glow
  [SerializeField] private ImageAnimation[] yellowColumnAnims; // looping anims under yellow overlays

  [Header("Win Celebration - Coins")]
  [SerializeField] private CoinFountainPool coinPool;

  [Header("Win Celebration - Debug")]
  [SerializeField] private bool debugWinTesting = false; // enable keyboard testing in play mode
  [SerializeField] private double debugLowWinValue = 5.0;
  [SerializeField] private double debugBigWinValue = 500.0;

  // Win-celebration runtime state.
  private double _targetWin;
  private double _displayValue;
  private Tween _valueTween;
  private Coroutine _bigWinRoutine;
  private Coroutine _fadeRoutine; // tracks a standalone GracefulFadeOut (skip path / debug)
  private bool _bigWinActive; // true from big-win start until fully reset (gates auto-spin + skip)
  private bool _lerpDone;
  private bool _valueLerping; // true while the win count-up tween runs (gates auto-spin for low wins too)
  private bool _fadingOut;

  internal Action<bool> ToggleAudio;
  internal Action<string> playButtonAudio;

  internal Action OnExit;
  internal Action OnLowBalConfirm;

  private void Awake()
  {
    if (jsFunctCalls != null)
      jsFunctCalls.RegisterVisibilityListener(gameObject.name);

    // Hide all win-celebration visuals at startup (alpha only — never interactable/blocksRaycasts).
    if (redCanvasGroup != null) redCanvasGroup.alpha = 0f;
    if (yellowCanvasGroup != null) yellowCanvasGroup.alpha = 0f;
    if (winPanelCanvasGroup != null) winPanelCanvasGroup.alpha = 0f;
    if (winPanelBG != null)
    {
      winPanelBG.localScale = Vector3.zero;
      Vector2 p = winPanelBG.anchoredPosition;
      winPanelBG.anchoredPosition = new Vector2(p.x, bgHiddenY);
    }
  }

  private void Start()
  {
    // Set up each button with the appropriate action
    SetButton(yes_Button, CallOnExitFunction);
    SetButton(no_Button, () => { if (!isExit) QuitPopupObject.SetActive(false); });
    SetButton(GameExit_Button, () => { OpenPopup(QuitPopupObject); });
    SetButton(paytable_Button, () => { OpenPopup(payTablePopup_Object); });
    SetButton(paytableExit_Button, () => payTablePopup_Object.SetActive(false));
    SetButton(paytableBgExit_Button, () => payTablePopup_Object.SetActive(false));
    SetButton(SoundON_Button, () => SetSound(false));
    SetButton(SoundOFF_Button, () => SetSound(true));
    SetButton(CloseDisconnect_Button, CallOnExitFunction);
    SetButton(Close_Button, () => { LowBalancePopup_Object.SetActive(false); OnLowBalConfirm?.Invoke(); });
    ApplySoundButtonVisibility();
  }

  private void SetSound(bool soundOn)
  {
    isSound = soundOn;
    // SetMuteAll(true) mutes; isSound==true means audio plays, so invoke with !isSound.
    ToggleAudio?.Invoke(!isSound);
    ApplySoundButtonVisibility();
  }

  private void ApplySoundButtonVisibility()
  {
    if (SoundON_Button) SoundON_Button.gameObject.SetActive(isSound);
    if (SoundOFF_Button) SoundOFF_Button.gameObject.SetActive(!isSound);
  }
 
  private void SetButton(Button button, Action action)
  {
    if (button == null)
    {
      Debug.LogError("Button is null");
      return;
    }

    button.onClick.RemoveAllListeners();
    button.onClick.AddListener(() =>
    {
      playButtonAudio?.Invoke("button");
      action?.Invoke();
    });
  }

  internal void UpdatePlayerInfo()
  {
    double winAmount = socketController.ResultData?.payload?.currentWinning ?? 0.00;
    SetPlayerBalance(socketController.PlayerData.balance);
    // Win value is no longer set instantly — the celebration lerps it (and runs red/yellow visuals).
    PlayWinCelebration(winAmount, gameManager.CurrentTotalBet);
  }

  internal void LowBalPopup()
  {
    OpenPopup(LowBalancePopup_Object);
  }

  internal bool IsLowBalPopupOpen => LowBalancePopup_Object != null && LowBalancePopup_Object.activeSelf;

  // Populated only at init (not on bet change): each paytable row shows a bet-independent multiplier
  // label "BET x {payout}". Names are matched against the uiData symbols first, then the
  // features.anyPayouts entries (anyBars, anyMixed, ...).
  internal void PopulateSymbolsPayout(UiData uiData)
  {
    if (uiData?.paylines?.symbols == null || SymbolsTexts == null) return;
    var anyPayouts = socketController?.InitData?.features?.anyPayouts;

    foreach (var symbolText in SymbolsTexts)
    {
      if (symbolText?.symbolText == null) continue;
      double? payout = ResolvePayout(symbolText.symbolName, uiData, anyPayouts);
      if (payout == null || payout.Value <= 0)
      {
        symbolText.symbolText.ForEach(t => { if (t != null) t.text = ""; });
        continue;
      }
      string formatted = "BET x " + payout.Value.ToString("0.##");
      symbolText.symbolText.ForEach(t => { if (t != null) t.text = formatted; });
    }
  }

  // Resolves a paytable row name to its payout: symbols first, then anyPayouts by raw JSON key.
  // Matching is case-insensitive so backend casing changes don't break the lookup. Returns null
  // when the name matches neither.
  private double? ResolvePayout(string name, UiData uiData, AnyPayouts anyPayouts)
  {
    if (string.IsNullOrEmpty(name)) return null;
    Symbol symbol = uiData.paylines.symbols.FirstOrDefault(
      s => string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase));
    if (symbol != null) return symbol.payout;
    if (anyPayouts != null)
    {
      switch (name.ToLowerInvariant())
      {
        case "anybars": return anyPayouts.anyBars;
        case "anymixed": return anyPayouts.anyMixed;
        case "anysevens": return anyPayouts.anySevens;
        case "onestarseven": return anyPayouts.oneStarSeven;
        case "twostarsevens": return anyPayouts.twoStarSevens;
      }
    }
    return null;
  }

  private void CallOnExitFunction()
  {
    isExit = true;
    // OnExit?.Invoke();
    StartCoroutine(socketController.CloseSocket());
  }

  private void OpenPopup(GameObject Popup)
  {
    if (Popup) Popup.SetActive(true);
  }

  private void ClosePopup(GameObject Popup)
  {
    if (DisconnectPopup_Object.activeSelf)
    {
      Debug.LogError("Disconnect popup is active, cant open Popup: " + Popup.name);
      return;
    } 
    if (Popup) Popup.SetActive(false);
  }

  internal void CheckAndClosePopup()
  {
    if (ReconectionPopup_Object.activeInHierarchy)
    {
      ClosePopup(ReconectionPopup_Object);
    }
  }
  internal void ReconnectionPopup()
  {
    OpenPopup(ReconectionPopup_Object);
  }

  internal void SetPlayerBalance(double amount)
  {
    string formatted = TextFormatter.FormatMoney(amount);
    if (playerBalance != null) playerBalance.text = formatted;
  }

  // ===== Win Celebration =====

  // Entry point from UpdatePlayerInfo. Branches into the low-win (red glow) or big-win (yellow + panel +
  // coins) presentation; in both cases the win value counts up rather than snapping.
  private void PlayWinCelebration(double winValue, double totalBet)
  {
    if (winValue <= 0) return; // HUD already reset to 0 at spin start

    bool bigWin = totalBet > 0 && winValue >= winThreshold * totalBet;
    if (bigWin)
    {
      _bigWinActive = true;
      _bigWinRoutine = StartCoroutine(BigWinSequence(winValue));
    }
    else
    {
      StartValueLerp(winValue, valueLerpDuration);
      if (redCanvasGroup != null) redCanvasGroup.DOFade(1f, redFadeInDuration); // fade in and keep on
    }
  }

  // Lerps the win value 0 -> target, updating the panel digits and the HUD digits together so both
  // show the same value while counting up.
  private void StartValueLerp(double target, float duration)
  {
    _valueTween?.Kill();
    _displayValue = 0;
    _lerpDone = false;
    _valueLerping = true;
    _targetWin = target;
    _valueTween = DOTween.To(() => _displayValue, v =>
    {
      _displayValue = v;
      TextFormatter.ApplyMoneyDigits(winValueTexts, v); // panel
      TextFormatter.ApplyMoneyDigits(winDigits, v);     // HUD
    }, target, duration).SetEase(Ease.Linear).OnComplete(() => { _lerpDone = true; _valueLerping = false; });
  }

  private IEnumerator BigWinSequence(double winValue)
  {
    // Reset BG before the reveal (also done in Awake).
    if (winPanelBG != null)
    {
      winPanelBG.localScale = Vector3.zero;
      Vector2 p = winPanelBG.anchoredPosition;
      winPanelBG.anchoredPosition = new Vector2(p.x, bgHiddenY);
    }
    if (winPanelCanvasGroup != null) winPanelCanvasGroup.alpha = 0f;

    // Yellow column glow + looping animations.
    if (yellowCanvasGroup != null) yellowCanvasGroup.DOFade(1f, yellowFadeInDuration);
    if (yellowColumnAnims != null)
      foreach (var a in yellowColumnAnims) if (a != null) a.StartAnimation();

    // Count-up runs in parallel with the panel reveal.
    StartValueLerp(winValue, bigWinValueLerpDuration);

    // Reveal the panel: fade CG in while scaling up + moving to the shown Y.
    if (winPanelCanvasGroup != null) winPanelCanvasGroup.DOFade(1f, bgScaleUpDuration);
    Sequence panelSeq = DOTween.Sequence();
    if (winPanelBG != null)
    {
      panelSeq.Append(winPanelBG.DOScale(1f, bgScaleUpDuration).SetEase(bgScaleEase));
      panelSeq.Join(winPanelBG.DOAnchorPosY(bgShowingY, bgMoveDuration).SetEase(bgMoveEase));
    }
    yield return panelSeq.WaitForCompletion();

    // Panel is in place — start the coin fountain.
    if (coinPool != null) coinPool.StartFountain();

    yield return new WaitUntil(() => _lerpDone);
    yield return new WaitForSeconds(postLerpHold);

    yield return GracefulFadeOut();

    _bigWinActive = false;
    _bigWinRoutine = null;
  }

  // Fades coins + panel out together, then stops the fountain and resets everything. Shared by the
  // normal end of the big-win sequence and by the skip path.
  private IEnumerator GracefulFadeOut()
  {
    if (_fadingOut) yield break;
    _fadingOut = true;

    // Kill any in-flight reveal tweens (possible on the skip path) so they don't fight the fade-out.
    if (winPanelCanvasGroup != null) winPanelCanvasGroup.DOKill();
    if (winPanelBG != null) winPanelBG.DOKill();

    if (coinPool != null) coinPool.FadeOutAllActive(coinFadeOutDuration); // keep spawning while fading

    if (winPanelCanvasGroup != null)
    {
      Tween pf = winPanelCanvasGroup.DOFade(0f, panelFadeOutDuration);
      yield return pf.WaitForCompletion();
    }

    if (coinPool != null) coinPool.ClearAll();
    // NOTE: the yellow column overlays/animations are intentionally NOT stopped here — they keep
    // looping until the next spin (cleared in ResetWinForNewSpin).
    if (winPanelBG != null)
    {
      winPanelBG.localScale = Vector3.zero;
      Vector2 p = winPanelBG.anchoredPosition;
      winPanelBG.anchoredPosition = new Vector2(p.x, bgHiddenY);
    }

    _fadingOut = false;
    _bigWinActive = false;
  }

  // Called at the start of every successful spin. Directly resets the HUD/panel digits + red glow (no
  // lerp). If a big win is still showing, this is the skip: snap the panel to its final value and run
  // the graceful fade-out in the background while the new spin proceeds.
  internal void ResetWinForNewSpin()
  {
    _valueTween?.Kill();
    _valueLerping = false;
    TextFormatter.ApplyMoneyDigits(winValueTexts, 0);
    TextFormatter.ApplyMoneyDigits(winDigits, 0);
    // Kill the in-flight fade-in before zeroing alpha — otherwise a still-running DOFade from the
    // previous low win keeps animating alpha back to 1 after this reset, leaving red lit through the
    // next spin (it would only appear to clear on the spin after that). Mirrors the yellow reset below.
    if (redCanvasGroup != null) { redCanvasGroup.DOKill(); redCanvasGroup.alpha = 0f; }

    // Stop the looping yellow column overlays — they run until this next spin begins.
    if (yellowCanvasGroup != null) { yellowCanvasGroup.DOKill(); yellowCanvasGroup.alpha = 0f; }
    if (yellowColumnAnims != null)
      foreach (var a in yellowColumnAnims) if (a != null) a.StopAnimation();

    if (_bigWinActive && !_fadingOut)
    {
      if (_bigWinRoutine != null)
      {
        StopCoroutine(_bigWinRoutine);
        _bigWinRoutine = null;
      }
      // Snap the panel value to its final so the count-up doesn't visibly cut off; HUD already 0.
      TextFormatter.ApplyMoneyDigits(winValueTexts, _targetWin);
      _fadeRoutine = StartCoroutine(GracefulFadeOut());
    }
  }

  // Auto-spin waits on this so the next spin doesn't start until a big-win celebration fully resets.
  internal IEnumerator WaitWinAnimDone()
  {
    // Wait for both the big-win celebration AND the (low-win) value count-up so auto-spin doesn't
    // truncate the count-up by starting the next spin (which kills the tween in ResetWinForNewSpin).
    yield return new WaitUntil(() => !_bigWinActive && !_valueLerping);
  }

  // ===== Debug testing (play mode only, gated by debugWinTesting) =====
  // 1 -> low win (red glow + value lerp)   2 -> big win (yellow + panel + coins)   3 -> reset/skip seq.
  private void Update()
  {
    if (!debugWinTesting) return;

    if (Input.GetKeyDown(KeyCode.Alpha1))
    {
      DebugHardReset();
      StartValueLerp(debugLowWinValue, valueLerpDuration);
      if (redCanvasGroup != null) redCanvasGroup.DOFade(1f, redFadeInDuration);
    }
    else if (Input.GetKeyDown(KeyCode.Alpha2))
    {
      DebugHardReset();
      _bigWinActive = true;
      _bigWinRoutine = StartCoroutine(BigWinSequence(debugBigWinValue));
    }
    else if (Input.GetKeyDown(KeyCode.Alpha3))
    {
      // Exercises the real spin-start path: skips/fades a live big win, else just clears.
      ResetWinForNewSpin();
    }
  }

  // Immediately tears down any in-progress celebration so a debug effect can start from a clean state.
  private void DebugHardReset()
  {
    if (_bigWinRoutine != null) { StopCoroutine(_bigWinRoutine); _bigWinRoutine = null; }
    if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
    _valueTween?.Kill();
    _bigWinActive = false;
    _fadingOut = false;
    _lerpDone = false;
    _valueLerping = false;

    if (coinPool != null) coinPool.ClearAll();
    if (redCanvasGroup != null) { redCanvasGroup.DOKill(); redCanvasGroup.alpha = 0f; }
    if (yellowCanvasGroup != null) { yellowCanvasGroup.DOKill(); yellowCanvasGroup.alpha = 0f; }
    if (yellowColumnAnims != null)
      foreach (var a in yellowColumnAnims) if (a != null) a.StopAnimation();
    if (winPanelCanvasGroup != null) { winPanelCanvasGroup.DOKill(); winPanelCanvasGroup.alpha = 0f; }
    if (winPanelBG != null)
    {
      winPanelBG.DOKill();
      winPanelBG.localScale = Vector3.zero;
      Vector2 p = winPanelBG.anchoredPosition;
      winPanelBG.anchoredPosition = new Vector2(p.x, bgHiddenY);
    }
    TextFormatter.ApplyMoneyDigits(winValueTexts, 0);
    TextFormatter.ApplyMoneyDigits(winDigits, 0);
  }

  internal void DisconnectionPopup()
  {
    if (!isExit)
    {
      if(ReconectionPopup_Object.activeInHierarchy) ReconectionPopup_Object.SetActive(true);
      OpenPopup(DisconnectPopup_Object);
    }
  }

  public void OnFocusChanged(string value)
  {
    bool focused = value == "1";
    Debug.Log("UNITY FOCUS CHANGED: " + value + " (focused: " + focused + ")");
    audioController?.SetMuteAll(!focused);
    socketController?.HandleFocusChange(focused);
  }

}

[Serializable]
public class SymbolPayoutTexts
{
  public string symbolName;
  public List<TMP_Text> symbolText;
}
