using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

public class GameManager : MonoBehaviour
{
  [Header("Scripts")]
  [SerializeField] private SlotController slotManager;
  [SerializeField] private UIManager uIManager;
  [SerializeField] private SocketController socketController;
  [SerializeField] private AudioController audioController;
  [SerializeField] internal FreeSpinController freeSpinController;

  [Header("For Spins")]
  [SerializeField] private Button SlotStart_Button;
  [SerializeField] private Button StopSpin_Button;
  [SerializeField] private Button ToatlBetMinus_Button;
  [SerializeField] private Button TotalBetPlus_Button;
  [SerializeField] private TMP_Text totalBet_text;
  [SerializeField] private TMP_Text LineBet_Text;
  [SerializeField] private bool isSpinning;
  [SerializeField] private Button TurboON_Button;
  [SerializeField] private Button TurboOFF_Button;

  [Header("For Auto Spins")]
  [SerializeField] private Button AutoSpin_Button;
  [SerializeField] private Button AutoSpinStop_Button;
  [SerializeField] private GameObject AutoSpinGlow;
  [SerializeField] internal bool isAutoSpin;
  [SerializeField] private float autoSpinDelay = 1.5f;

  private double currentBalance;
  [SerializeField] private double currentTotalBet;
  [SerializeField] internal int betCounter = 0;

  private Coroutine autoSpinRoutine;
  private int _autoSpinRemaining;
  private bool _autoUntilFeature;
  [SerializeField] internal bool isFreeSpin;

  private bool initiated;
  [SerializeField] internal bool turboMode;
  [SerializeField] internal bool immediateStop;
  private Coroutine spinRoutine;


  void Start()
  {
    SetButton(SlotStart_Button, () => ExecuteSpin(), true);
    SetButton(AutoSpin_Button, () => StartAutoSpin(-1), true);
    SetButton(AutoSpinStop_Button, () => StartCoroutine(StopAutoSpinCoroutine()));
    SetButton(ToatlBetMinus_Button, () => { OnBetChange(false); });
    SetButton(TotalBetPlus_Button, () => { OnBetChange(true); });
    SetButton(TurboON_Button, () => { ToggleTurboMode(); });
    SetButton(TurboOFF_Button, () => { ToggleTurboMode(); });
    SetButton(StopSpin_Button, () => StartCoroutine(StopSpin()));

    if (freeSpinController != null)
    {
      freeSpinController.gameManager = this;
      freeSpinController.playButtonAudio = (s) => audioController.Play(s);
    }

    socketController.OnInit = InitGame;
    uIManager.ToggleAudio = audioController.SetMuteAll;
    uIManager.playButtonAudio = (s) => audioController.Play(s);
    if (uIManager.winAnim != null)
    {
      uIManager.winAnim.playAudio = (s) => audioController.Play(s);
      uIManager.winAnim.fadeAudio = (s, d) => audioController.FadeOut(s, d);
    }
    uIManager.OnExit = () => socketController.CloseSocket();
    uIManager.OnLowBalConfirm = () => ToggleButtonGrp(true);
    socketController.ShowDisconnectionPopup = uIManager.DisconnectionPopup;

    ApplyTurboButtonVisibility();
  }

  private void SetButton(Button button, Action action, bool slotButton = false)
  {
    if (button == null) return;

    button.onClick.RemoveAllListeners();
    button.onClick.AddListener(() =>
    {
      uIManager.playButtonAudio?.Invoke("button");
      action?.Invoke();
    });
  }

  void InitGame()
  {
    if (!initiated)
    {
      initiated = true;
      betCounter = 0;
      currentTotalBet = socketController.InitLineBetData.bets[betCounter] * socketController.InitLineBetData.lines.Count;
      currentBalance = socketController.PlayerData.balance;
      uIManager.UpdatePlayerInfo();
      if (totalBet_text) totalBet_text.text = TextFormatter.FormatMoney(currentTotalBet);
      LineBet_Text.text = TextFormatter.FormatMoney(socketController.InitLineBetData.bets[betCounter]);
      UpdateBetButtonsInteractable();
      if (currentBalance < currentTotalBet)
      {
        ToggleButtonGrp(false);
        uIManager.LowBalPopup();
      }

      uIManager.PopulateSymbolsPayout(socketController.InitSymbolData);
    }
    else
    {
      uIManager.PopulateSymbolsPayout(socketController.InitSymbolData);
    }
    uIManager.RefreshDiamondPayoutTexts(currentTotalBet);
    uIManager.PopulateInfoPageDiamondPayouts();
  }

  void ExecuteSpin()
  {
    // Block re-entry: during auto / free spin AutoSpinRoutine and RunFreeSpins drive SpinRoutine
    // without populating the spinRoutine field, so the null-check below would otherwise let a stray
    // click launch a parallel SpinRoutine that fights the active one for the reel tweens.
    if (isAutoSpin || isFreeSpin || isSpinning) return;

    // If the win-animation sequence is mid-flight, treat the spin click as a skip so OneSpinFlow's
    // WaitWinAnimDone gate resolves promptly and we can launch the next spin.
    if (uIManager.winAnim != null && uIManager.winAnim.IsPlaying)
      uIManager.winAnim.Skip();

    if (spinRoutine != null)
    {
      StopCoroutine(spinRoutine);
      spinRoutine = null;
      isSpinning = false;
    }
    spinRoutine = StartCoroutine(SpinRoutine());
  }

  internal void StartAutoSpin(int count)
  {
    if (isAutoSpin || isFreeSpin || isSpinning) return;
    if (uIManager.winAnim != null && uIManager.winAnim.IsPlaying)
      uIManager.winAnim.Skip();
    _autoUntilFeature = (count < 0);
    _autoSpinRemaining = count;
    isAutoSpin = true;
    SetAutoSpinUI(true);
    autoSpinRoutine = StartCoroutine(AutoSpinRoutine());
  }

  void ToggleTurboMode()
  {
    turboMode = !turboMode;
    ApplyTurboButtonVisibility();
  }

  // ON button (no cross) visible when turbo is on; OFF button (cross) visible when turbo is off.
  void ApplyTurboButtonVisibility()
  {
    if (TurboON_Button) TurboON_Button.gameObject.SetActive(turboMode);
    if (TurboOFF_Button) TurboOFF_Button.gameObject.SetActive(!turboMode);
  }

  IEnumerator AutoSpinRoutine()
  {
    if (isSpinning)
    {
      if (spinRoutine != null)
      {
        StopCoroutine(spinRoutine);
        spinRoutine = null;
      }
      isSpinning = false;
    }

    while (isAutoSpin)
    {
      yield return SpinRoutine();

      if (isFreeSpin)
        yield break;

      // _autoUntilFeature cutoff is handled mid-spin in HandleAutoUntilFeatureCutoff (right after the
      // result arrives), so isAutoSpin will already be false here and the while-loop exits on its own.
      if (!_autoUntilFeature)
      {
        _autoSpinRemaining--;
        if (_autoSpinRemaining <= 0) break;
      }

      yield return new WaitForSeconds(autoSpinDelay);
    }

    // CLEAN EXIT (NO coroutine calls)
    isSpinning = false;
    isAutoSpin = false;

    // --- OLD (Age of Gods): only restored the auto-spin button
    // AutoSpin_Button.gameObject.SetActive(true);
    SetAutoSpinUI(false);
    ToggleButtonGrp(true);
  }

  private IEnumerator StopAutoSpinCoroutine()
  {
    isAutoSpin = false;

    // Lock stop button so repeat clicks no-op while the current spin finishes.
    if (AutoSpinStop_Button) AutoSpinStop_Button.interactable = false;

    yield return new WaitUntil(() => !isSpinning);

    SetAutoSpinUI(false);

    if (!uIManager.IsLowBalPopupOpen)
      ToggleButtonGrp(true);

    if (spinRoutine != null)
    {
      StopCoroutine(spinRoutine);
      spinRoutine = null;
    }

    if (autoSpinRoutine != null)
    {
      StopCoroutine(autoSpinRoutine);
      autoSpinRoutine = null;
    }
  }
  IEnumerator SpinRoutine()
  {
    ToggleButtonGrp(false);
    bool start = OnSpinStart();

    // // ===== CASE 1: Spin did not start (low balance etc.)
    if (!start)
    {
      spinRoutine = null;
      isSpinning = false;

      if (isAutoSpin)
      {
        StartCoroutine(StopAutoSpinCoroutine());
      }

      yield break;
    }

    yield return OneSpinFlow();

    // The trigger spin's OnSpinEnd flipped isFreeSpin and faded in the FS UI. Drive the awarded
    // spins inline so retriggers (which run inside OnSpinEnd and bump spinsRemaining) extend
    // the loop transparently.
    if (isFreeSpin)
      yield return RunFreeSpinLoop();

    if (!isAutoSpin && !isFreeSpin)
    {
      ToggleButtonGrp(true);
    }
    isSpinning = false;
    spinRoutine = null;
  }

  IEnumerator RunFreeSpinLoop()
  {
    while (freeSpinController.spinsRemaining > 0)
    {
      freeSpinController.BeforeSpin();
      freeSpinController.spinsRemaining--;

      // OneSpinFlow drives a full server spin → reels → diamond/lineWins. Retrigger inside
      // OnSpinEnd will RegisterAward, bumping spinsRemaining back up — the while-loop then
      // continues for the extra spins.
      yield return OneSpinFlow();

      freeSpinController.AccumulateWin(LastSpinWinAmount());

      if (freeSpinController.spinsRemaining > 0)
        yield return freeSpinController.InterSpinDelay();
    }

    // Final spin completed without retrigger. Its lineWins already ran one-shot (spinsRemaining
    // hit 0 inside that AnimateLineWins call — see SlotController.AnimateLineWins). End panel
    // overlays the looping diamond/lineWins behind it.
    isFreeSpin = false;
    yield return freeSpinController.ShowEndPanel(t => uIManager.SetPlayerCurrentWinning(t));
    audioController.Play("bg");
  }

  bool LastSpinTriggeredFreeSpins()
  {
    var triggered = socketController.ResultData?.payload?.triggeredFeatures;
    if (triggered == null) return false;
    foreach (var t in triggered)
      if (t != null && string.Equals(t.ToString(), "FREE_SPINS", StringComparison.OrdinalIgnoreCase))
        return true;
    return false;
  }

  int LastSpinFreeSpinAward() => socketController.ResultData?.payload?.freeSpins?.awarded ?? 0;
  double LastSpinWinAmount() => socketController.ResultData?.payload?.winAmount ?? 0;
  List<string> LastSpinScatterPositions() => socketController.ResultData?.payload?.freeSpins?.scatterPositions;

  IEnumerator OneSpinFlow()
  {
    immediateStop = false;
    if (isFreeSpin) uIManager.ResetWinAnimation();
    yield return OnSpin();
    // yield return new WaitForSecondsRealtime(0.5f);
    yield return OnSpinEnd();
    // Auto / free spin loops must wait for the win-animation sequence to fully reset before the
    // next spin can kick off. Skip() (called from ExecuteSpin / StartAutoSpin / OnSpinStart) makes
    // this resolve promptly.
    if (isAutoSpin || isFreeSpin) yield return uIManager.WaitWinAnimDone();
  }

  IEnumerator StopSpin()
  {
    if (immediateStop)
      yield break;
    immediateStop = true;
    StopSpin_Button.gameObject.SetActive(false);
    StopSpin_Button.interactable = false;
    yield return new WaitUntil(() => !isSpinning);
    immediateStop = false;
    StopSpin_Button.interactable = true;
  }

  bool OnSpinStart()
  {
    uIManager.ResetWinAnimation();
    // Tear down any leftover winning-row anim from the previous spin so a click mid-presentation
    // resets the row immediately (size/loop), then fire the one-shot shine across all rows.
    uIManager.StopDiamondPayoutRowWin();
    uIManager.PlayDiamondPayoutShineOverlay();

    if (currentBalance < currentTotalBet && !isFreeSpin)
    {
      uIManager.LowBalPopup();
      return false;
    }
    isSpinning = true;
    return true;
  }

  IEnumerator OnSpin()
  {
    if (!isFreeSpin)
      uIManager.SetPlayerBalance(socketController.PlayerData.balance - currentTotalBet);

    // Stop button is usable in manual, auto, AND free-spin modes (per spec: stop must remain
    // available to interrupt auto/free chains). Explicitly re-enable interactable — the previous
    // spin's StopSpin click flow flips it false on press and back true on release, so a click
    // that races with spin teardown can leave the next spin starting with interactable=false.
    // Turbo mode auto-stops every spin, so the manual stop button stays hidden throughout.
    immediateStop = false;
    if (!turboMode)
    {
      StopSpin_Button.gameObject.SetActive(true);
      StopSpin_Button.interactable = true;
    }

    yield return slotManager.StartSpin();

    socketController.AccumulateResult(betCounter);
    yield return new WaitUntil(() => socketController.isResultdone);
    
    slotManager.PopulateSlotMatrix(socketController.ResultData.matrix);

    if(turboMode)
    {
      // Turbo: behave as if the user pressed StopSpin the instant the result arrived. The flag
      // also makes SlotController.StopSpin skip its per-reel stagger so all reels land together.
      immediateStop = true;
    }
    int waitFor = 10;
    for (int i = 0; i < waitFor; i++)
    {
      if (immediateStop && i > 7)
      {
        break;
      }

      yield return new WaitForSecondsRealtime(0.1f);
    }

    yield return slotManager.StopSpin(() => audioController.Play("reelstop"));
    immediateStop = false;

    if (StopSpin_Button.gameObject.activeSelf)
      StopSpin_Button.gameObject.SetActive(false);
  }

  IEnumerator OnSpinEnd()
  {
    currentBalance = socketController.ResultData.player.balance;
    uIManager.UpdatePlayerInfo();

    // FS trigger / retrigger is pre-armed here (auto-spin kill, BeginSession, RegisterAward, flip
    // isFreeSpin on entry) but the centered scatter sequence + Start panel + FS UI fade are
    // deferred until AFTER this spin's win presentation. Otherwise the user dismisses the Start
    // panel and only then sees the trigger spin's big-win / line / diamond animations playing on
    // top of the already-faded-in FS background.
    //
    // Flipping isFreeSpin early also routes AnimateLineWins through its synced-pass-only branch
    // (SlotController.AnimateLineWins checks isFreeSpin && spinsRemaining > 0), which is exactly
    // what we want for the trigger spin.
    bool fsTriggered = LastSpinTriggeredFreeSpins();
    int awarded = 0;
    bool fsIsEntry = false;
    if (fsTriggered)
    {
      // Auto-spin must turn off the moment FS triggers (per spec).
      if (isAutoSpin)
      {
        isAutoSpin = false;
        SetAutoSpinUI(false);
        if (autoSpinRoutine != null) { StopCoroutine(autoSpinRoutine); autoSpinRoutine = null; }
      }

      awarded = LastSpinFreeSpinAward();
      fsIsEntry = !isFreeSpin;
      if (fsIsEntry) freeSpinController.BeginSession();
      freeSpinController.RegisterAward(awarded);
      if (fsIsEntry) isFreeSpin = true;
    }

    if (socketController.ResultData.payload.winAmount > 0)
      uIManager.TriggerWinAnimation(socketController.ResultData.payload.winAmount, currentTotalBet);

    // Diamond feature: server always sends diamondCount + diamondPositions (even for the
    // single-diamond idle case).
    var feats = socketController.ResultData.payload.featureWins;
    bool diamondTrigger = feats != null && feats.diamondPositions != null && feats.diamondCount >= 2;
    bool diamondIdle = feats != null && feats.diamondPositions != null && feats.diamondCount == 1;

    if (diamondTrigger) uIManager.PlayDiamondPayoutRowWin(feats.diamondCount);

    // Matrix diamond animation:
    //   - auto-spin or FS-trigger spin: single non-looped cycle yielded in parallel with the
    //     line-wins synced pass; the icons reset themselves on completion. For FS-trigger we use
    //     the one-shot so we can proceed to the centered scatter sequence afterward without
    //     leaving a forever-loop running underneath it. If the user stops auto mid-cycle we
    //     re-arm the looping animation below so the trigger stays visible.
    //   - manual non-FS: existing forever-loop, torn down on next StartSpin's StopIconAnimation.
    Coroutine diamondCycle = null;
    if (diamondTrigger)
    {
      if (isAutoSpin || fsTriggered) diamondCycle = StartCoroutine(slotManager.PlayDiamondTriggeredCycle(feats.diamondPositions));
      else slotManager.StartDiamondTriggered(feats.diamondPositions);
    }
    else if (diamondIdle && SlotController.TryParseDiamondPos(feats.diamondPositions[0], out int idleRow, out int idleCol))
    {
      StartCoroutine(slotManager.PlayDiamondIdle(idleRow, idleCol));
    }

    if (socketController.ResultData.payload.lineWins.Count > 0)
    {
      // "win" SFX now fires with the win-line animations (after the scatter animations), inside
      // SlotController's win presentation.
      yield return slotManager.AnimateLineWins(socketController.ResultData.payload.lineWins);
    }

    if (diamondCycle != null) yield return diamondCycle;

    // Auto-spin was stopped during the parallel cycles: re-arm the looping diamond animation so
    // the matrix matches what a manual-spin trigger would have shown.
    if (diamondTrigger && !isAutoSpin && !isFreeSpin)
      slotManager.StartDiamondTriggered(feats.diamondPositions);

    // FS centered scatter sequence + Start panel + FS UI fade-in run AFTER the trigger spin's
    // win presentation has finished. Wait for the big-win animation to fully resolve first so it
    // doesn't get covered by the centered scatters; OneSpinFlow's tail-wait then becomes a no-op.
    if (fsTriggered)
    {
      yield return uIManager.WaitWinAnimDone();

      Coroutine startPanelIn = null;
      yield return slotManager.PlayFreeSpinTriggeredSequence(
        LastSpinScatterPositions(),
        onThirdPlayStart: () =>
        {
          startPanelIn = StartCoroutine(freeSpinController.PlayStartPanelIn(awarded));
        }
      );
      if (startPanelIn != null) yield return startPanelIn;
      yield return freeSpinController.WaitStartPanelOk();

      if (fsIsEntry)
      {
        audioController.Play("fbg");
        yield return freeSpinController.FadeInFreeSpinUi();
      }
    }
  }

  void SetAutoSpinUI(bool autoActive)
  {
    if (AutoSpin_Button) AutoSpin_Button.gameObject.SetActive(!autoActive);
    if (AutoSpinStop_Button)
    {
      AutoSpinStop_Button.gameObject.SetActive(autoActive);
      AutoSpinStop_Button.interactable = autoActive;
    }
    if (AutoSpinGlow) AutoSpinGlow.SetActive(autoActive);
  }

  internal void ToggleButtonGrp(bool toggle)
  {
    if (SlotStart_Button) SlotStart_Button.interactable = toggle;
    if (AutoSpin_Button) AutoSpin_Button.interactable = toggle;
    if (toggle)
    {
      UpdateBetButtonsInteractable();
    }
    else
    {
      if (ToatlBetMinus_Button) ToatlBetMinus_Button.interactable = false;
      if (TotalBetPlus_Button) TotalBetPlus_Button.interactable = false;
    }
    uIManager.paytable_Button.interactable = toggle;
    TurboON_Button.interactable = toggle;
    TurboOFF_Button.interactable = toggle;
  }

  private void OnBetChange(bool inc)
  {
    int lastIndex = socketController.InitLineBetData.bets.Count - 1;
    if (inc)
    {
      betCounter = (betCounter >= lastIndex) ? 0 : betCounter + 1;
    }
    else
    {
      betCounter = (betCounter <= 0) ? lastIndex : betCounter - 1;
    }

    currentTotalBet = socketController.InitLineBetData.bets[betCounter] * socketController.InitLineBetData.lines.Count;
    if (totalBet_text) totalBet_text.text = TextFormatter.FormatMoney(currentTotalBet);
    LineBet_Text.text = TextFormatter.FormatMoney(socketController.InitLineBetData.bets[betCounter]);
    UpdateBetButtonsInteractable();
    uIManager.PopulateSymbolsPayout(socketController.InitSymbolData);
    uIManager.RefreshDiamondPayoutTexts(currentTotalBet);
  }

  void UpdateBetButtonsInteractable()
  {
    if (socketController == null || socketController.InitLineBetData == null || socketController.InitLineBetData.bets == null) return;
    if (ToatlBetMinus_Button) ToatlBetMinus_Button.interactable = true;
    if (TotalBetPlus_Button) TotalBetPlus_Button.interactable = true;
  }
}
