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

  internal Action<bool> ToggleAudio;
  internal Action<string> playButtonAudio;

  internal Action OnExit;
  internal Action OnLowBalConfirm;

  private void Awake()
  {
    if (jsFunctCalls != null)
      jsFunctCalls.RegisterVisibilityListener(gameObject.name);

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
    TextFormatter.ApplyMoneyDigits(winDigits, winAmount);

    SetPlayerBalance(socketController.PlayerData.balance);
  }

  internal void LowBalPopup()
  {
    OpenPopup(LowBalancePopup_Object);
  }

  internal bool IsLowBalPopupOpen => LowBalancePopup_Object != null && LowBalancePopup_Object.activeSelf;

  internal void PopulateSymbolsPayout(UiData uiData)
  {
    if (uiData?.paylines?.symbols == null || SymbolsTexts == null) return;
    var bets = socketController?.InitLineBetData?.bets;
    if (bets == null || gameManager == null) return;
    if (gameManager.betCounter < 0 || gameManager.betCounter >= bets.Count) return;
    double lineBet = bets[gameManager.betCounter];

    foreach (var symbolText in SymbolsTexts)
    {
      if (symbolText?.symbolText == null) continue;
      Symbol symbol = uiData.paylines.symbols.FirstOrDefault(s => s.name == symbolText.symbolName);
      if (symbol == null || symbol.payout <= 0)
      {
        symbolText.symbolText.ForEach(t => { if (t != null) t.text = ""; });
        continue;
      }
      string formatted = (symbol.payout * lineBet).ToString("0.##");
      symbolText.symbolText.ForEach(t => { if (t != null) t.text = formatted; });
    }
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

  internal void TriggerWinAnimation(double winAmount, double betAmount)
  {
    if (winAmount <= 0 || betAmount <= 0) return;
    // if (winAnim != null) winAnim.Trigger(winAmount, betAmount);
  }

  internal void DisconnectionPopup()
  {
    if (!isExit)
    {
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
