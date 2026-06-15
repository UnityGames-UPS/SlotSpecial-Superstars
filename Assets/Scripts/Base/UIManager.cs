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
  // 8 entries, idx 0 -> count 2, idx 7 -> count 9.
  [SerializeField] private TMP_Text[] infoPageDiamondPayoutText;

  [Header("Pagination")]
  int CurrentIndex = 0;
  [SerializeField] private GameObject[] paytableList;
  [SerializeField] private Image[] pageIndicators;
  [SerializeField] private Sprite indicatorOn;
  [SerializeField] private Sprite indicatorOff;

  [Header("Pagination - Drag")]
  [SerializeField] private float pageWidth = 1453.13f;
  [SerializeField] private float dragSnapThreshold = 0.25f;
  [SerializeField] private float edgeResistance = 0.35f;
  [SerializeField] private float snapDuration = 0.3f;
  private float _dragAccum;
  private int _neighborIndex = -1;
  private bool _dragAtEdge;

  [Header("Sound Toggle")]
  [SerializeField] private Button SoundON_Button;
  [SerializeField] private Button SoundOFF_Button;
  private bool isSound = true;

  [Header("Win Animation")]
  [SerializeField] internal WinAnimController winAnim;


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
  [SerializeField] private TMP_Text playerCurrentWinning;
  [SerializeField] private TMP_Text playerBalance;

  [Header("Diamonds Payout UI")]
  // 8 rows, index 0 = count 2, index 7 = count 9.
  [SerializeField] private ImageAnimation diamondPayoutOverlayShine;
  [SerializeField] private RectTransform[] diamondPayoutRowBg;
  [SerializeField] private ImageAnimation[] diamondPayoutRowWinAnim;
  [SerializeField] private RectTransform[] diamondPayoutRowWinAnimRect;
  [SerializeField] private TMP_Text[] diamondPayoutRowText;
  [SerializeField] private float diamondPayoutRowWinWidthDelta = 40f;
  private Vector2[] _diamondPayoutRowBgDefaultSize;
  private Vector2[] _diamondPayoutRowWinAnimDefaultSize;
  private int _diamondActiveRowIndex = -1;

  internal Action<bool> ToggleAudio;
  internal Action<string> playButtonAudio;

  internal Action OnExit;
  internal Action OnLowBalConfirm;

  private void Awake()
  {
    if (jsFunctCalls != null)
      jsFunctCalls.RegisterVisibilityListener(gameObject.name);

    CacheDiamondPayoutDefaults();
  }

  private void CacheDiamondPayoutDefaults()
  {
    if (diamondPayoutRowBg != null)
    {
      _diamondPayoutRowBgDefaultSize = new Vector2[diamondPayoutRowBg.Length];
      for (int i = 0; i < diamondPayoutRowBg.Length; i++)
        if (diamondPayoutRowBg[i] != null)
          _diamondPayoutRowBgDefaultSize[i] = diamondPayoutRowBg[i].sizeDelta;
    }
    if (diamondPayoutRowWinAnimRect != null)
    {
      _diamondPayoutRowWinAnimDefaultSize = new Vector2[diamondPayoutRowWinAnimRect.Length];
      for (int i = 0; i < diamondPayoutRowWinAnimRect.Length; i++)
        if (diamondPayoutRowWinAnimRect[i] != null)
          _diamondPayoutRowWinAnimDefaultSize[i] = diamondPayoutRowWinAnimRect[i].sizeDelta;
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

    // Initialize other settings
    foreach (var page in paytableList)
    {
      RectTransform rt = page.transform as RectTransform;
      rt.anchoredPosition = new Vector2(0, rt.anchoredPosition.y);
    }
    paytableList[CurrentIndex = 0].SetActive(true);
    InitIndicators();
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
    double winAmount = socketController.ResultData?.payload?.winAmount ?? 0.00;
    if(winAmount>0)
      playerCurrentWinning.text = TextFormatter.FormatSprite(winAmount, TextFormatter.GetSignificantDecimals(winAmount));
    else
     playerCurrentWinning.text = TextFormatter.FormatSprite(0, 2); 

    SetPlayerBalance(socketController.PlayerData.balance);
  }

  void ResetWinUIText()
  {
    playerCurrentWinning.text = TextFormatter.FormatSprite(0.00, 2);
  }

  internal void SetPlayerCurrentWinning(double value)
  {
    if (playerCurrentWinning != null)
      playerCurrentWinning.text = TextFormatter.FormatSprite(value, TextFormatter.GetSignificantDecimals(value));
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

  internal void PopulateInfoPageDiamondPayouts()
  {
    if (infoPageDiamondPayoutText == null) return;
    var payout = socketController?.InitData?.features?.diamondPayout;
    if (payout == null) return;

    for (int i = 0; i < infoPageDiamondPayoutText.Length; i++)
    {
      if (infoPageDiamondPayoutText[i] == null) continue;
      int count = i + 2;
      infoPageDiamondPayoutText[i].text = payout.TryGetValue(count, out int mult) ? $"x{mult}" : "";
    }
  }

  internal void PlayDiamondPayoutShineOverlay()
  {
    if (diamondPayoutOverlayShine == null) return;
    diamondPayoutOverlayShine.StopAnimation();
    diamondPayoutOverlayShine.doLoopAnimation = false;
    diamondPayoutOverlayShine.StartAnimation();
  }

  // Maps diamondCount (2..9) to row index (0..7) and plays the per-row glow looped, with the
  // row's bg + win-anim rects widened by diamondPayoutRowWinWidthDelta. StopDiamondPayoutRowWin
  // unwinds the resize.
  internal void PlayDiamondPayoutRowWin(int diamondCount)
  {
    int idx = diamondCount - 2;
    if (diamondPayoutRowWinAnim == null || idx < 0 || idx >= diamondPayoutRowWinAnim.Length) return;
    StopDiamondPayoutRowWin();
    _diamondActiveRowIndex = idx;

    if (diamondPayoutRowBg != null && idx < diamondPayoutRowBg.Length && diamondPayoutRowBg[idx] != null)
    {
      var rt = diamondPayoutRowBg[idx];
      rt.sizeDelta = _diamondPayoutRowBgDefaultSize[idx] + new Vector2(diamondPayoutRowWinWidthDelta, 0f);
    }
    if (diamondPayoutRowWinAnimRect != null && idx < diamondPayoutRowWinAnimRect.Length && diamondPayoutRowWinAnimRect[idx] != null)
    {
      var rt = diamondPayoutRowWinAnimRect[idx];
      rt.sizeDelta = _diamondPayoutRowWinAnimDefaultSize[idx] + new Vector2(diamondPayoutRowWinWidthDelta, 0f);
    }

    var anim = diamondPayoutRowWinAnim[idx];
    if (anim != null)
    {
      anim.StopAnimation();
      anim.doLoopAnimation = true;
      anim.gameObject.SetActive(true);
      anim.StartAnimation();
    }
  }

  internal void StopDiamondPayoutRowWin()
  {
    if (_diamondActiveRowIndex < 0) return;
    int idx = _diamondActiveRowIndex;

    if (diamondPayoutRowWinAnim != null && idx < diamondPayoutRowWinAnim.Length && diamondPayoutRowWinAnim[idx] != null)
    {
      var anim = diamondPayoutRowWinAnim[idx];
      anim.StopAnimation();
      anim.ResetToFirstFrame();
      anim.gameObject.SetActive(false);
    }
    if (diamondPayoutRowBg != null && idx < diamondPayoutRowBg.Length && diamondPayoutRowBg[idx] != null
        && _diamondPayoutRowBgDefaultSize != null && idx < _diamondPayoutRowBgDefaultSize.Length)
    {
      diamondPayoutRowBg[idx].sizeDelta = _diamondPayoutRowBgDefaultSize[idx];
    }
    if (diamondPayoutRowWinAnimRect != null && idx < diamondPayoutRowWinAnimRect.Length && diamondPayoutRowWinAnimRect[idx] != null
        && _diamondPayoutRowWinAnimDefaultSize != null && idx < _diamondPayoutRowWinAnimDefaultSize.Length)
    {
      diamondPayoutRowWinAnimRect[idx].sizeDelta = _diamondPayoutRowWinAnimDefaultSize[idx];
    }
    _diamondActiveRowIndex = -1;
  }

  internal void RefreshDiamondPayoutTexts(double totalBet = 0)
  {
    if (diamondPayoutRowText == null) return;
    var payout = socketController?.InitData?.features?.diamondPayout;
    var bets = socketController?.InitLineBetData?.bets;
    if (payout == null || bets == null || gameManager == null) return;
    if (gameManager.betCounter < 0 || gameManager.betCounter >= bets.Count) return;
    double lineBet = bets[gameManager.betCounter];

    for (int i = 0; i < diamondPayoutRowText.Length; i++)
    {
      if (diamondPayoutRowText[i] == null) continue;
      int count = i + 2;
      if (!payout.TryGetValue(count, out int mult)) { diamondPayoutRowText[i].text = ""; continue; }
      diamondPayoutRowText[i].text = TextFormatter.FormatMoney(mult * totalBet);
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

  private void InitIndicators()
  {
    for (int i = 0; i < pageIndicators.Length; i++)
      pageIndicators[i].sprite = i == 0 ? indicatorOn : indicatorOff;
  }

  internal void OnDragBegin()
  {
    DOTween.Kill(paytableList[CurrentIndex].transform as RectTransform);
    if (_neighborIndex >= 0 && _neighborIndex < paytableList.Length)
      DOTween.Kill(paytableList[_neighborIndex].transform as RectTransform);
    _dragAccum = 0f;
    _neighborIndex = -1;
    _dragAtEdge = false;
  }

  internal void OnDragDelta(float deltaX)
  {
    _dragAccum += deltaX;

    bool atLeftEdge = CurrentIndex == 0 && _dragAccum > 0f;
    bool atRightEdge = CurrentIndex == paytableList.Length - 1 && _dragAccum < 0f;
    _dragAtEdge = atLeftEdge || atRightEdge;

    RectTransform current = paytableList[CurrentIndex].transform as RectTransform;

    if (_dragAtEdge)
    {
      if (_neighborIndex != -1)
      {
        RectTransform old = paytableList[_neighborIndex].transform as RectTransform;
        old.anchoredPosition = new Vector2(0, old.anchoredPosition.y);
        paytableList[_neighborIndex].SetActive(false);
        _neighborIndex = -1;
      }
      float resisted = _dragAccum * edgeResistance;
      current.anchoredPosition = new Vector2(resisted, current.anchoredPosition.y);
      return;
    }

    int wantNeighbor = _dragAccum > 0f ? CurrentIndex - 1 : CurrentIndex + 1;
    if (wantNeighbor < 0 || wantNeighbor >= paytableList.Length) return;

    if (wantNeighbor != _neighborIndex)
    {
      if (_neighborIndex != -1)
      {
        RectTransform old = paytableList[_neighborIndex].transform as RectTransform;
        old.anchoredPosition = new Vector2(0, old.anchoredPosition.y);
        paytableList[_neighborIndex].SetActive(false);
      }
      _neighborIndex = wantNeighbor;
      paytableList[_neighborIndex].SetActive(true);
    }

    RectTransform neighbor = paytableList[_neighborIndex].transform as RectTransform;
    float neighborOffset = _dragAccum > 0f ? -pageWidth : pageWidth;
    current.anchoredPosition = new Vector2(_dragAccum, current.anchoredPosition.y);
    neighbor.anchoredPosition = new Vector2(_dragAccum + neighborOffset, neighbor.anchoredPosition.y);
  }

  internal void OnDragEnd()
  {
    RectTransform current = paytableList[CurrentIndex].transform as RectTransform;

    if (_dragAtEdge || _neighborIndex == -1)
    {
      current.DOAnchorPosX(0f, snapDuration).SetEase(Ease.OutCubic);
      _dragAccum = 0f;
      _dragAtEdge = false;
      return;
    }

    RectTransform neighbor = paytableList[_neighborIndex].transform as RectTransform;
    bool commit = Mathf.Abs(_dragAccum) >= dragSnapThreshold * pageWidth;

    if (commit)
    {
      int prevIndex = CurrentIndex;
      int nextIndex = _neighborIndex;
      float currentEndX = _dragAccum > 0f ? pageWidth : -pageWidth;

      if (prevIndex < pageIndicators.Length) pageIndicators[prevIndex].sprite = indicatorOff;
      if (nextIndex < pageIndicators.Length) pageIndicators[nextIndex].sprite = indicatorOn;
      CurrentIndex = nextIndex;

      DOTween.Sequence()
        .Append(current.DOAnchorPosX(currentEndX, snapDuration).SetEase(Ease.OutCubic))
        .Join(neighbor.DOAnchorPosX(0f, snapDuration).SetEase(Ease.OutCubic))
        .OnComplete(() =>
        {
          paytableList[prevIndex].SetActive(false);
          current.anchoredPosition = new Vector2(0, current.anchoredPosition.y);
        });
    }
    else
    {
      int oldNeighbor = _neighborIndex;
      float neighborEndX = _dragAccum > 0f ? -pageWidth : pageWidth;
      DOTween.Sequence()
        .Append(current.DOAnchorPosX(0f, snapDuration).SetEase(Ease.OutCubic))
        .Join(neighbor.DOAnchorPosX(neighborEndX, snapDuration).SetEase(Ease.OutCubic))
        .OnComplete(() =>
        {
          paytableList[oldNeighbor].SetActive(false);
          neighbor.anchoredPosition = new Vector2(0, neighbor.anchoredPosition.y);
        });
    }

    _dragAccum = 0f;
    _neighborIndex = -1;
    _dragAtEdge = false;
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
    if (winAnim != null) winAnim.Trigger(winAmount, betAmount);
  }

  internal void ResetWinAnimation()
  {
    ResetWinUIText();
    if (winAnim != null) winAnim.Skip();
  }

  internal System.Collections.IEnumerator WaitWinAnimDone()
  {
    if (winAnim == null) yield break;
    yield return winAnim.WaitUntilDone();
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
