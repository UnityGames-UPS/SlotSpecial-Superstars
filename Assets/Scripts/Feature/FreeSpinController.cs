using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FreeSpinController : MonoBehaviour
{
  [Header("Start Panel")]
  [SerializeField] private CanvasGroup startPanelCG;
  [SerializeField] private TMP_Text awardedCountText;
  [SerializeField] private Button startPanelOk_Button;
  [SerializeField] private RectTransform startBgRect;
  [SerializeField] private ImageAnimation startBgLoopAnim;
  [SerializeField] private RectTransform startFreeSpinsTextRect;
  [SerializeField] private RectTransform startYouWonGroupRect;

  [Header("UI Panel (during free spins)")]
  [SerializeField] private CanvasGroup uiPanelCG;
  [SerializeField] private TMP_Text countText;
  [SerializeField] private TMP_Text totalWinText;

  [Header("End Panel")]
  [SerializeField] private CanvasGroup endPanelCG;
  [SerializeField] private RectTransform endBgRect;
  [SerializeField] private ImageAnimation endBgLoopAnim;
  [SerializeField] private RectTransform endTotalWinImageRect;
  [SerializeField] private RectTransform endTotalWinValueRect;
  [SerializeField] private TMP_Text endTotalWinValueText;
  [SerializeField] private Button endPanelOk_Button;

  [Header("Background swap")]
  [SerializeField] private GameObject freeSpinsBg;
  [SerializeField] private Image normalBgImage;
  [SerializeField] private Image freeSpinsBgImage;
  [SerializeField] private CanvasGroup normalBgDotsCG;
  [SerializeField] private BackgroundDotsField normalBgDotsField;

  [Header("Timing")]
  [SerializeField] private float fadeDuration = 0.4f;
  [SerializeField] private float pulseHigh = 1.05f;
  [SerializeField] private float pulseLow = 0.95f;
  [SerializeField] private float pulseDuration = 0.6f;
  [SerializeField] private float interSpinDelay = 1f;
  [SerializeField] private float endPanelDelay = 1f;

  internal int spinsRemaining;
  internal int spinsTotal;
  internal int spinsCompleted;
  internal double totalWin;

  private bool _startOkClicked;
  private bool _endOkClicked;
  private readonly List<Tween> _pulseTweens = new();

  internal GameManager gameManager;
  internal Action<string> playButtonAudio;

  void Awake()
  {
    if (startPanelOk_Button != null)
    {
      startPanelOk_Button.onClick.RemoveAllListeners();
      startPanelOk_Button.onClick.AddListener(() =>
      {
        playButtonAudio?.Invoke("button");
        _startOkClicked = true;
        startPanelOk_Button.interactable = false;
      });
    }
    if (endPanelOk_Button != null)
    {
      endPanelOk_Button.onClick.RemoveAllListeners();
      endPanelOk_Button.onClick.AddListener(() =>
      {
        playButtonAudio?.Invoke("button");
        _endOkClicked = true;
        endPanelOk_Button.interactable = false;
      });
    }

    SnapHide(startPanelCG);
    SnapHide(uiPanelCG);
    SnapHide(endPanelCG);

    if (freeSpinsBgImage != null)
    {
      var c = freeSpinsBgImage.color; c.a = 0f; freeSpinsBgImage.color = c;
    }
    if (freeSpinsBg != null) freeSpinsBg.SetActive(false);
  }

  // Resets accumulator state at the start of a fresh free-spin session (initial trigger only —
  // retriggers call RegisterAward without clearing totals).
  internal void BeginSession()
  {
    spinsRemaining = 0;
    spinsTotal = 0;
    spinsCompleted = 0;
    totalWin = 0;
    RefreshCountText();
    RefreshTotalWinText();
  }

  internal void RegisterAward(int awarded)
  {
    spinsRemaining += awarded;
    spinsTotal += awarded;
    RefreshCountText();
  }

  internal void AccumulateWin(double winThisSpin)
  {
    totalWin += winThisSpin;
    RefreshTotalWinText();
  }

  void RefreshCountText()
  {
    if (countText == null) return;
    countText.text = TextFormatter.FormatSpriteFraction(spinsCompleted, spinsTotal);
  }

  void RefreshTotalWinText()
  {
    if (totalWinText == null) return;
    int decimals = TextFormatter.GetSignificantDecimals(totalWin, 2);
    // FS UI panel font: '.' lives at sprite=11 (default FormatSprite mapping).
    totalWinText.text = TextFormatter.FormatSprite(totalWin, decimals);
  }

  // Section C — Start panel fade-in + scale-up + pulses. Caller (GameManager.OnSpinEnd) invokes
  // this from inside SlotController.PlayFreeSpinTriggeredSequence's third-play hook.
  internal IEnumerator PlayStartPanelIn(int awarded)
  {
    if (awardedCountText != null)
      awardedCountText.text = $"You have won {awarded} Free Spins!";
    _startOkClicked = false;
    if (startPanelOk_Button != null) startPanelOk_Button.interactable = true;

    if (startPanelCG != null)
    {
      startPanelCG.gameObject.SetActive(true);
      startPanelCG.alpha = 0f;
      SetCgInteractable(startPanelCG, false);
    }
    if (startBgRect != null) startBgRect.localScale = Vector3.zero;
    if (startFreeSpinsTextRect != null) startFreeSpinsTextRect.localScale = Vector3.zero;
    if (startYouWonGroupRect != null) startYouWonGroupRect.localScale = Vector3.zero;
    if (startBgLoopAnim != null) { startBgLoopAnim.doLoopAnimation = true; startBgLoopAnim.StartAnimation(); }

    Sequence enter = DOTween.Sequence();
    if (startPanelCG != null) enter.Join(startPanelCG.DOFade(1f, fadeDuration));
    if (startBgRect != null) enter.Join(startBgRect.DOScale(1f, fadeDuration).SetEase(Ease.OutBack));
    if (startFreeSpinsTextRect != null) enter.Join(startFreeSpinsTextRect.DOScale(1f, fadeDuration).SetEase(Ease.OutBack));
    if (startYouWonGroupRect != null) enter.Join(startYouWonGroupRect.DOScale(1f, fadeDuration).SetEase(Ease.OutBack));
    yield return enter.WaitForCompletion();

    SetCgInteractable(startPanelCG, true);
    StartPulse(startFreeSpinsTextRect);
    StartPulse(startYouWonGroupRect);
  }

  internal IEnumerator WaitStartPanelOk()
  {
    while (!_startOkClicked) yield return null;
    KillPulses();

    Sequence exit = DOTween.Sequence();
    if (startPanelCG != null) exit.Join(startPanelCG.DOFade(0f, fadeDuration));
    if (startBgRect != null) exit.Join(startBgRect.DOScale(0f, fadeDuration).SetEase(Ease.InBack));
    if (startFreeSpinsTextRect != null) exit.Join(startFreeSpinsTextRect.DOScale(0f, fadeDuration).SetEase(Ease.InBack));
    if (startYouWonGroupRect != null) exit.Join(startYouWonGroupRect.DOScale(0f, fadeDuration).SetEase(Ease.InBack));
    yield return exit.WaitForCompletion();

    if (startBgLoopAnim != null) startBgLoopAnim.StopAnimation();
    if (startPanelCG != null)
    {
      SetCgInteractable(startPanelCG, false);
      startPanelCG.gameObject.SetActive(false);
    }
  }

  internal IEnumerator FadeInFreeSpinUi()
  {
    if (freeSpinsBg != null) freeSpinsBg.SetActive(true);
    if (uiPanelCG != null)
    {
      uiPanelCG.gameObject.SetActive(true);
      uiPanelCG.alpha = 0f;
      SetCgInteractable(uiPanelCG, true);
    }

    Sequence enter = DOTween.Sequence();
    if (normalBgImage != null) enter.Join(normalBgImage.DOFade(0f, fadeDuration));
    if (normalBgDotsCG != null) enter.Join(normalBgDotsCG.DOFade(0f, fadeDuration));
    if (freeSpinsBgImage != null) enter.Join(freeSpinsBgImage.DOFade(1f, fadeDuration));
    if (uiPanelCG != null) enter.Join(uiPanelCG.DOFade(1f, fadeDuration));
    yield return enter.WaitForCompletion();

    // Layer is fully transparent — stop the Update tick to skip 2500-renderer cost during FS.
    if (normalBgDotsField != null) normalBgDotsField.Pause();
  }

  internal IEnumerator FadeOutFreeSpinUi()
  {
    // Resume the sparkles before the fade-in so dots are already animating as alpha climbs back to 1.
    if (normalBgDotsField != null) normalBgDotsField.Resume();

    Sequence exit = DOTween.Sequence();
    if (normalBgImage != null) exit.Join(normalBgImage.DOFade(1f, fadeDuration));
    if (normalBgDotsCG != null) exit.Join(normalBgDotsCG.DOFade(1f, fadeDuration));
    if (freeSpinsBgImage != null) exit.Join(freeSpinsBgImage.DOFade(0f, fadeDuration));
    if (uiPanelCG != null) exit.Join(uiPanelCG.DOFade(0f, fadeDuration));
    yield return exit.WaitForCompletion();

    if (uiPanelCG != null)
    {
      SetCgInteractable(uiPanelCG, false);
      uiPanelCG.gameObject.SetActive(false);
    }
    if (freeSpinsBg != null) freeSpinsBg.SetActive(false);
  }

  // Per-spin gate: bumps completed count BEFORE running the spin so the UI reads "X/Y" with
  // the current spin already counted (Casino-style: "Spin 1 of 10").
  internal void BeforeSpin()
  {
    spinsCompleted++;
    RefreshCountText();
  }

  // Drives the inter-spin pause. Stop button (gameManager.immediateStop) shortens it.
  internal IEnumerator InterSpinDelay()
  {
    float elapsed = 0f;
    while (elapsed < interSpinDelay)
    {
      if (gameManager != null && gameManager.immediateStop) yield break;
      elapsed += Time.unscaledDeltaTime;
      yield return null;
    }
  }

  // End panel: fade + scale-up + pulses + wait OK + fade-out. Caller writes the final total back
  // into the HUD via applyTotalToHud after the panel resolves.
  internal IEnumerator ShowEndPanel(Action<double> applyTotalToHud)
  {
    yield return new WaitForSecondsRealtime(endPanelDelay);

    if (endTotalWinValueText != null)
    {
      int decimals = TextFormatter.GetSignificantDecimals(totalWin, 2);
      endTotalWinValueText.text = TextFormatter.FormatSprite(totalWin, decimals, true);
    }

    _endOkClicked = false;
    if (endPanelOk_Button != null) endPanelOk_Button.interactable = true;

    if (endPanelCG != null)
    {
      endPanelCG.gameObject.SetActive(true);
      endPanelCG.alpha = 0f;
      SetCgInteractable(endPanelCG, false);
    }
    if (endBgRect != null) endBgRect.localScale = Vector3.zero;
    if (endTotalWinImageRect != null) endTotalWinImageRect.localScale = Vector3.zero;
    if (endTotalWinValueRect != null) endTotalWinValueRect.localScale = Vector3.zero;
    if (endBgLoopAnim != null) { endBgLoopAnim.doLoopAnimation = true; endBgLoopAnim.StartAnimation(); }

    Sequence enter = DOTween.Sequence();
    if (endPanelCG != null) enter.Join(endPanelCG.DOFade(1f, fadeDuration));
    if (endBgRect != null) enter.Join(endBgRect.DOScale(1f, fadeDuration).SetEase(Ease.OutBack));
    if (endTotalWinImageRect != null) enter.Join(endTotalWinImageRect.DOScale(1f, fadeDuration).SetEase(Ease.OutBack));
    if (endTotalWinValueRect != null) enter.Join(endTotalWinValueRect.DOScale(1f, fadeDuration).SetEase(Ease.OutBack));
    yield return enter.WaitForCompletion();

    SetCgInteractable(endPanelCG, true);
    StartPulse(endTotalWinImageRect);
    StartPulse(endTotalWinValueRect);

    while (!_endOkClicked) yield return null;
    KillPulses();

    Sequence exit = DOTween.Sequence();
    if (endPanelCG != null) exit.Join(endPanelCG.DOFade(0f, fadeDuration));
    if (endBgRect != null) exit.Join(endBgRect.DOScale(0f, fadeDuration).SetEase(Ease.InBack));
    if (endTotalWinImageRect != null) exit.Join(endTotalWinImageRect.DOScale(0f, fadeDuration).SetEase(Ease.InBack));
    if (endTotalWinValueRect != null) exit.Join(endTotalWinValueRect.DOScale(0f, fadeDuration).SetEase(Ease.InBack));
    yield return exit.WaitForCompletion();

    if (endBgLoopAnim != null) endBgLoopAnim.StopAnimation();
    if (endPanelCG != null)
    {
      SetCgInteractable(endPanelCG, false);
      endPanelCG.gameObject.SetActive(false);
    }

    applyTotalToHud?.Invoke(totalWin);

    yield return FadeOutFreeSpinUi();
  }

  void StartPulse(RectTransform rt)
  {
    if (rt == null) return;
    rt.localScale = Vector3.one;
    Tween t = rt.DOScale(Vector3.one * pulseHigh, pulseDuration * 0.5f)
      .SetEase(Ease.InOutSine)
      .SetLoops(-1, LoopType.Yoyo);
    _pulseTweens.Add(t);
  }

  void KillPulses()
  {
    foreach (var t in _pulseTweens) t?.Kill();
    _pulseTweens.Clear();
  }

  void SetCgInteractable(CanvasGroup cg, bool on)
  {
    if (cg == null) return;
    cg.interactable = on;
    cg.blocksRaycasts = on;
  }

  void SnapHide(CanvasGroup cg)
  {
    if (cg == null) return;
    cg.alpha = 0f;
    SetCgInteractable(cg, false);
    cg.gameObject.SetActive(false);
  }
}
