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
  [SerializeField] private TMP_Text[] totalBetDigits; // per-digit BET HUD (left-to-right, dot excluded)
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
      // [Superstars] No paylines in init model; the selected bet is the total stake per spin.
      // (Diamond Riches multiplied lineBet by lines.Count.)
      currentTotalBet = socketController.InitLineBetData.bets[betCounter];
      currentBalance = socketController.PlayerData.balance;
      uIManager.UpdatePlayerInfo();
      TextFormatter.ApplyMoneyDigits(totalBetDigits, currentTotalBet);
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
    if (isAutoSpin || isSpinning) return;

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
    if (isAutoSpin || isSpinning) return;
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

    if (!isAutoSpin)
    {
      ToggleButtonGrp(true);
    }
    isSpinning = false;
    spinRoutine = null;
  }

  IEnumerator OneSpinFlow()
  {
    immediateStop = false;
    yield return OnSpin();
    // yield return new WaitForSecondsRealtime(0.5f);
    yield return OnSpinEnd();
    // Auto / free spin loops must wait for the win-animation sequence to fully reset before the
    // next spin can kick off. Skip() (called from ExecuteSpin / StartAutoSpin / OnSpinStart) makes
    // this resolve promptly.
    if (isAutoSpin) yield return uIManager.WaitWinAnimDone();
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

    if (currentBalance < currentTotalBet)
    {
      uIManager.LowBalPopup();
      return false;
    }
    isSpinning = true;
    return true;
  }

  IEnumerator OnSpin()
  {
    uIManager.SetPlayerBalance(socketController.PlayerData.balance - currentTotalBet);

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
      immediateStop = true;
    }

    if (!immediateStop)
    {
      int waitFor = 10;
      for (int i = 0; i < waitFor; i++)
      {
        if (immediateStop)
        {
          break;
        }
        yield return new WaitForSecondsRealtime(0.1f);
      }
    }

    yield return slotManager.StopSpin(() => audioController.Play("reelstop"), socketController.ResultData.matrix);
    immediateStop = false;

    if (StopSpin_Button.gameObject.activeSelf)
      StopSpin_Button.gameObject.SetActive(false);
  }

  IEnumerator OnSpinEnd()
  {
    currentBalance = socketController.ResultData.player.balance;
    uIManager.UpdatePlayerInfo();

    yield return null;
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
    if (ToatlBetMinus_Button) ToatlBetMinus_Button.interactable = toggle;
    if (TotalBetPlus_Button) TotalBetPlus_Button.interactable = toggle;
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
    currentTotalBet = socketController.InitLineBetData.bets[betCounter];
    TextFormatter.ApplyMoneyDigits(totalBetDigits, currentTotalBet);
    uIManager.PopulateSymbolsPayout(socketController.InitSymbolData);
    uIManager.RefreshDiamondPayoutTexts(currentTotalBet);
  }
}
