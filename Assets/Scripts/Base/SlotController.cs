using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;
using System.Linq;
using System;
using TMPro;
using Newtonsoft.Json;

public class SlotController : MonoBehaviour
{
  [SerializeField] internal AudioController audioController;
  [SerializeField] internal GameManager gameManager;

  [Header("Sprites")]
  [SerializeField] private Sprite[] iconImages;

  [Header("Special Win Animations")]
  [SerializeField] private List<Sprite> wildBgAnimationSprites;
  [SerializeField] private List<Sprite> wildIconAnimationSprites;
  [SerializeField] private List<Sprite> ladyIconAnimationSprites;

  [Header("Slot Images")]
  [SerializeField] internal List<SlotImage> slotMatrix;
  [SerializeField] private List<SlotImage> allMatrix;

  [Header("Animation Overlay")]
  [SerializeField] private RectTransform animationOverlayParent;
  // Full-slot black (alpha ~200) overlay. Enabled only for the looping win pass; animating
  // symbols are lifted to animationOverlayParent (above this) so they stay lit while the rest darken.
  [SerializeField] private GameObject darkOverlay;

  // Wild symbol id — when it is the middle symbol of a line we suppress that line's payout label.
  private const int WildId = 5;

  [Header("Slots Transforms")]
  [SerializeField] private RectTransform[] Slot_Transform;

  private List<Tweener> alltweens = new List<Tweener>();
  [SerializeField] private List<SlotIconView> animatingIcons = new List<SlotIconView>();
  [SerializeField] private float betweenLineDelay = 0.25f;
  private Coroutine WinLoopCorutine = null;
  private Coroutine scatterChainCoroutine = null;
  // Tracks the lines currently being presented by WinPresentation so StopWinLoop can run the
  // per-symbol cleanup (Drop, hide border/text, restore iconImage.enabled) instead of just
  // killing the coroutine and leaving symbols stranded in animationOverlayParent.
  private List<LineWin> _activeWinLines = null;
  // Per-icon win iteration coroutines started by PlaySyncedPass / SingleLineLoop / PerLineLoop.
  // StopCoroutine(WinLoopCorutine) only kills the outer presentation; without tracking these,
  // the inner PlayWinIteration coroutines stay suspended at PlayPulse / PlayOverlaySequence and
  // resume *after* StartSpin's synchronous teardown — racing the new spin and leaving icons in
  // half-cleaned-up states (stranded on the overlay, sprite stale, position offset).
  private List<Coroutine> _activeWinIterCoroutines = new List<Coroutine>();

  [Header("Symbol Win Animations")]
  [SerializeField] private List<SymbolWinAnim> symbolWinAnims = new List<SymbolWinAnim>();
  private Dictionary<int, SymbolWinAnim> _winAnimById;

  internal SymbolWinAnim GetWinAnim(int id)
    => (_winAnimById != null && _winAnimById.TryGetValue(id, out var a)) ? a : null;

  [Header("Free Spin Symbol Animation")]
  [SerializeField] private List<Sprite> freeSpinSymbolAnimSprites = new List<Sprite>();
  [SerializeField] private float freeSpinSymbolAnimSpeed = 5f;

  [Header("Diamond Symbol Animations")]
  [SerializeField] private List<Sprite> diamondIdleSprites = new List<Sprite>();
  [SerializeField] private float diamondIdleSpeed = 5f;
  [SerializeField] private List<Sprite> diamondTriggeredSprites = new List<Sprite>();
  [SerializeField] private float diamondTriggeredSpeed = 5f;

  [Header("Extra Column (2-scatter teaser)")]
  [SerializeField] private CanvasGroup extraColumnCG;
  [SerializeField] private RectTransform extraColumnTransform;
  [SerializeField] private RectTransform extraColumnReel;
  [SerializeField] private SlotImage extraColumnSlot;
  [SerializeField] private float extraColumnScaleBump = 0.05f;
  [SerializeField] private float extraColumnFadeDuration = 0.35f;
  [SerializeField] private float extraColumnReelHoldSeconds = 1f;
  [SerializeField] private float extraColumnInterTeaserDelay = 0.4f;

  [Header("Free Spin Triggered Sequence")]
  [SerializeField] private List<Sprite> freeSpinTriggeredSprites = new List<Sprite>();
  [SerializeField] private float freeSpinTriggeredSpeed = 5f;
  [SerializeField] private RectTransform freeSpinCenterTarget;
  [SerializeField] private float freeSpinCenterMoveDuration = 0.7f;

  private Coroutine extraTeaserCoroutine = null;
  private Tweener _extraReelTween = null;
  private bool _teaserActive = false;

  // Free-spin (scatter) symbol id. It does not contribute to win lines; it is animated on reel
  // stop, column by column. Free spins trigger when each of the first FreeSpinTriggerColumns
  // columns holds at least one of these symbols.
  private const int FreeSpinSymbolId = 6;
  private const int FreeSpinTriggerColumns = 3;

  // Set true by each reel's landing tween OnComplete so the scatter chain can fire a column's
  // animation the instant that reel lands (while reels to the right are still spinning).
  private bool[] _reelLanded;

  void Awake()
  {
    ShuffleMatrix();

    _winAnimById = new Dictionary<int, SymbolWinAnim>();
    foreach (var a in symbolWinAnims)
      if (a != null && !_winAnimById.ContainsKey(a.id)) _winAnimById[a.id] = a;
  }

  internal IEnumerator StartSpin()
  {
    StopWinLoop();
    StopIconAnimation();
    SetDarkOverlay(false);
    ResetExtraColumn();
    KillAllTweens();
    ResetAllIcons();

    List<Tween> initTweens = new();
    audioController.Play("spinning");
    for (int i = 0; i < Slot_Transform.Length; i++)
    {
      initTweens.Add(InitializeTweening(Slot_Transform[i]));
    }
    yield return initTweens[0].WaitForCompletion();
    yield return initTweens[^1].WaitForCompletion();
  }

  internal void PopulateSlotMatrix(List<List<string>> resultData)
  {
    for (int i = 0; i < resultData.Count; i++)
    {
      for (int j = 0; j < resultData[i].Count; j++)
      {
        int id = int.Parse(resultData[i][j]);
        slotMatrix[j].slotImages[i].SetIcon(ID: id, image: iconImages[id]); // matrix is [row][col]; slotMatrix is column-major
      }
    }
  }

  internal IEnumerator StopSpin(Action playFallAudio)
  {
    if (_reelLanded == null || _reelLanded.Length != Slot_Transform.Length)
      _reelLanded = new bool[Slot_Transform.Length];
    for (int i = 0; i < _reelLanded.Length; i++) _reelLanded[i] = false;

    // Two-scatter teaser is active when cols 0 and 1 both hold a scatter (regardless of col 2 —
    // a 3-scatter trigger still gets the extra-column reveal). When active, defer col 2's landing
    // to the teaser coroutine so the real col 2 keeps looping during the reveal.
    _teaserActive = TwoScatterTeaserActive() && extraColumnCG != null && Slot_Transform.Length >= 3;
    int reelsToLand = _teaserActive ? Slot_Transform.Length - 1 : Slot_Transform.Length;

    // Runs alongside the reel-stop loop: animates each scatter the moment its reel lands.
    // Not awaited here — the win presentation waits on it instead, so the buttons can re-enable and
    // a spin click can interrupt it (StartSpin -> StopWinLoop stops this coroutine).
    scatterChainCoroutine = StartCoroutine(FreeSpinSymbolChain());

    if (_teaserActive)
      extraTeaserCoroutine = StartCoroutine(PlayExtraColumnTeaser(playFallAudio));

    for (int i = 0; i < reelsToLand; i++)
    {
      StopTweening(Slot_Transform[i], i);
      int landed = i;
      alltweens[i].OnComplete(() => { if (landed < _reelLanded.Length) _reelLanded[landed] = true; });

      // Immediate-stop case still needs one reel-stop SFX for the simultaneous landing — so play
      // unconditionally on reel 0, then per-reel only while the user hasn't pressed Stop.
      bool immediate = gameManager.immediateStop;
      if (i == 0 || !immediate)
        playFallAudio?.Invoke();

      if (!immediate)
      {
        // Interruptible inter-reel delay: the moment Stop is pressed (immediateStop -> true)
        // bail out of the wait so the remaining reels are stopped on the same frame.
        float wait = gameManager.turboMode ? 0.2f : 0.6f;
        float elapsed = 0f;
        while (elapsed < wait && !gameManager.immediateStop)
        {
          elapsed += Time.unscaledDeltaTime;
          yield return null;
        }
      }
    }

    // Wait for the reels we did stop (skips col 2 when teaser is active — teaser awaits it itself).
    for (int i = 0; i < reelsToLand; i++)
      yield return alltweens[i].WaitForCompletion();

    if (extraTeaserCoroutine != null)
    {
      yield return extraTeaserCoroutine;
      extraTeaserCoroutine = null;
    }

    // Snap reel columns to exactly RestY so the win presentation's Lift() reads a final
    // world position regardless of any OutBack overshoot frame race.
    for (int i = 0; i < Slot_Transform.Length; i++)
      Slot_Transform[i].localPosition = new Vector2(Slot_Transform[i].localPosition.x, RestY);
    KillAllTweens();

    // Let UI layout flush before any Lift samples world position.
    Canvas.ForceUpdateCanvases();
    yield return null;
  }

  // Walks the first FreeSpinTriggerColumns columns left to right. For each, waits until that
  // reel has landed, then plays the scatter (id 6) animation if the column holds one. The chain
  // breaks the moment a column has no scatter — so a scatter that lands in a later column without
  // scatters in every preceding column is never animated (no free-spin trigger is possible).
  IEnumerator FreeSpinSymbolChain()
  {
    for (int col = 0; col < FreeSpinTriggerColumns && col < slotMatrix.Count; col++)
    {
      yield return new WaitUntil(() => _reelLanded != null && col < _reelLanded.Length && _reelLanded[col]);

      var scatter = GetColumnFreeSpinIcon(col);
      if (scatter == null)
        yield break; // chain broken: this column's last-row symbol isn't a scatter

      yield return scatter.PlayFreeSpinAnim(freeSpinSymbolAnimSprites, freeSpinSymbolAnimSpeed);
    }
  }

  // If a column holds the free-spin (id 6) symbol in more than one row, only the lowest one
  // (highest row index) animates, so at most one free-spin animation plays per column.
  // Returns null when the column has no free-spin symbol.
  SlotIconView GetColumnFreeSpinIcon(int col)
  {
    if (col < 0 || col >= slotMatrix.Count) return null;
    var images = slotMatrix[col].slotImages;
    if (images == null) return null;
    SlotIconView last = null;
    for (int row = 0; row < images.Count; row++)
      if (images[row] != null && images[row].id == FreeSpinSymbolId) last = images[row];
    return last;
  }

  bool TwoScatterTeaserActive()
    => GetColumnFreeSpinIcon(0) != null && GetColumnFreeSpinIcon(1) != null;

  // Two-scatter teaser. Runs alongside StopSpin's reel loop. Cols 0/1 land normally via the loop;
  // col 2 stays infinite-looping until this coroutine stops it together with the extra column so
  // both land on the same frame and their scatter anims (if col 2 has one) play simultaneously.
  IEnumerator PlayExtraColumnTeaser(Action playFallAudio)
  {
    // Mirror col-2's result symbols onto the extra column so when it lands the visible rows
    // already display the correct sprites. PopulateSlotMatrix has already run by the time
    // StopSpin starts, so slotMatrix[2].slotImages[row].id is the resolved result.
    if (extraColumnSlot != null && extraColumnSlot.slotImages != null && slotMatrix.Count > 2)
    {
      for (int row = 0; row < extraColumnSlot.slotImages.Count && row < slotMatrix[2].slotImages.Count; row++)
      {
        int id = slotMatrix[2].slotImages[row].id;
        if (id < 0 || id >= iconImages.Length) continue;
        extraColumnSlot.slotImages[row].SetIcon(ID: id, image: iconImages[id]);
      }
    }

    // Wait for cols 0 and 1 to finish landing before revealing the extra column.
    while (!gameManager.immediateStop && (_reelLanded.Length < 2 || !_reelLanded[0] || !_reelLanded[1]))
      yield return null;

    if (gameManager.immediateStop)
    {
      yield return CancelExtraColumnTeaser();
      yield break;
    }

    SetDarkOverlay(true);

    // Start the extra reel's infinite tween BEFORE the fade so the user sees a spinning reel
    // through the fade-in.
    if (extraColumnReel != null)
    {
      extraColumnReel.localPosition = new Vector2(extraColumnReel.localPosition.x, SpinTopY);
      _extraReelTween = extraColumnReel.DOLocalMoveY(SpinBottomY, DurationFor(SpinTopY, SpinBottomY))
        .SetLoops(-1, LoopType.Restart)
        .SetEase(Ease.Linear);
    }

    if (extraColumnCG != null)
    {
      extraColumnCG.gameObject.SetActive(true);
      extraColumnCG.alpha = 0f;
    }
    if (extraColumnTransform != null) extraColumnTransform.localScale = Vector3.one;

    Sequence reveal = DOTween.Sequence();
    if (extraColumnCG != null) reveal.Join(extraColumnCG.DOFade(1f, extraColumnFadeDuration));
    if (extraColumnTransform != null) reveal.Join(extraColumnTransform.DOScale(1f + extraColumnScaleBump, extraColumnFadeDuration));
    yield return WaitOrCancel(reveal);
    if (gameManager.immediateStop) { yield return CancelExtraColumnTeaser(); yield break; }

    // Hold (interruptible by stop).
    float elapsed = 0f;
    while (elapsed < extraColumnReelHoldSeconds && !gameManager.immediateStop)
    {
      elapsed += Time.unscaledDeltaTime;
      yield return null;
    }
    if (gameManager.immediateStop) { yield return CancelExtraColumnTeaser(); yield break; }

    // Stop both reels on the same frame. Real col 2 uses the standard StopTweening path so the
    // scatter chain's _reelLanded[2] gate unblocks; extra reel mirrors the OutBack land.
    StopTweening(Slot_Transform[2], 2);
    alltweens[2].OnComplete(() => { if (2 < _reelLanded.Length) _reelLanded[2] = true; });

    Tween extraStop = null;
    if (extraColumnReel != null)
    {
      _extraReelTween?.Kill();
      _extraReelTween = null;
      extraColumnReel.localPosition = new Vector2(extraColumnReel.localPosition.x, SpinTopY);
      extraStop = extraColumnReel.DOLocalMoveY(RestY, DurationFor(SpinTopY, RestY)).SetEase(Ease.OutBack, 0.9f);
    }
    playFallAudio?.Invoke();

    yield return alltweens[2].WaitForCompletion();
    if (extraStop != null) yield return extraStop.WaitForCompletion();
    Canvas.ForceUpdateCanvases();

    // If col 2 has a scatter, mirror the scatter animation on the extra column's last scatter row.
    // The real col 2 anim fires from FreeSpinSymbolChain when _reelLanded[2] flips (above).
    SlotIconView extraScatter = GetExtraColumnFreeSpinIcon();
    if (extraScatter != null && freeSpinSymbolAnimSprites != null && freeSpinSymbolAnimSprites.Count > 0)
    {
      extraScatter.Lift(animationOverlayParent);
      yield return extraScatter.PlayFreeSpinAnim(freeSpinSymbolAnimSprites, freeSpinSymbolAnimSpeed);
      extraScatter.Drop();
    }

    // Wait for the scatter chain (cols 0,1, and 2 if it has a scatter) before fading the extra column.
    if (scatterChainCoroutine != null) yield return scatterChainCoroutine;

    yield return FadeOutExtraColumn();

    if (gameManager.isAutoSpin || gameManager.isFreeSpin)
      yield return new WaitForSecondsRealtime(extraColumnInterTeaserDelay);
  }

  SlotIconView GetExtraColumnFreeSpinIcon()
  {
    if (extraColumnSlot == null || extraColumnSlot.slotImages == null) return null;
    SlotIconView last = null;
    foreach (var icon in extraColumnSlot.slotImages)
      if (icon != null && icon.id == FreeSpinSymbolId) last = icon;
    return last;
  }

  IEnumerator WaitOrCancel(Sequence seq)
  {
    while (seq != null && seq.IsActive() && seq.IsPlaying())
    {
      if (gameManager.immediateStop) { seq.Kill(); yield break; }
      yield return null;
    }
  }

  IEnumerator CancelExtraColumnTeaser()
  {
    // User pressed Stop mid-teaser. Forfeit the reveal entirely, then run the normal stop path
    // for col 2 so it lands like every other reel.
    if (_extraReelTween != null) { _extraReelTween.Kill(); _extraReelTween = null; }
    if (extraColumnCG != null) { extraColumnCG.alpha = 0f; extraColumnCG.gameObject.SetActive(false); }
    if (extraColumnTransform != null) extraColumnTransform.localScale = Vector3.one;
    SetDarkOverlay(false);

    if (2 < Slot_Transform.Length && 2 < alltweens.Count && alltweens[2] != null && !alltweens[2].IsComplete())
    {
      StopTweening(Slot_Transform[2], 2);
      alltweens[2].OnComplete(() => { if (2 < _reelLanded.Length) _reelLanded[2] = true; });
      yield return alltweens[2].WaitForCompletion();
    }
  }

  IEnumerator FadeOutExtraColumn()
  {
    if (extraColumnCG == null && extraColumnTransform == null) yield break;
    Sequence exit = DOTween.Sequence();
    if (extraColumnCG != null) exit.Join(extraColumnCG.DOFade(0f, extraColumnFadeDuration));
    if (extraColumnTransform != null) exit.Join(extraColumnTransform.DOScale(1f, extraColumnFadeDuration));
    yield return exit.WaitForCompletion();
    if (extraColumnCG != null) extraColumnCG.gameObject.SetActive(false);
    SetDarkOverlay(false);
  }

  void ResetExtraColumn()
  {
    _teaserActive = false;
    if (_extraReelTween != null) { _extraReelTween.Kill(); _extraReelTween = null; }
    if (extraTeaserCoroutine != null) { StopCoroutine(extraTeaserCoroutine); extraTeaserCoroutine = null; }
    if (extraColumnCG != null) { extraColumnCG.alpha = 0f; extraColumnCG.gameObject.SetActive(false); }
    if (extraColumnTransform != null) extraColumnTransform.localScale = Vector3.one;
    if (extraColumnSlot != null && extraColumnSlot.slotImages != null)
      foreach (var icon in extraColumnSlot.slotImages) if (icon != null) icon.Reset();
  }

  // Centered triggered reveal: lifts the three scatter icons, plays the triggered sequence twice
  // while they converge on freeSpinCenterTarget, returns them, then plays the third time in place.
  // The caller (GameManager.OnSpinEnd) is responsible for fading in the Start panel during the
  // third play.
  internal IEnumerator PlayFreeSpinTriggeredSequence(List<string> scatterPositions, Action onThirdPlayStart = null)
  {
    if (freeSpinTriggeredSprites == null || freeSpinTriggeredSprites.Count == 0)
      yield break;

    audioController.Play("freespinhit");

    // Select the same icons as the per-column chain anim: lowest-row scatter in each of the
    // first three columns. Ignores scatterPositions to keep the two paths consistent when a
    // column holds more than one scatter.
    var icons = new List<SlotIconView>();
    var originalWorld = new List<Vector3>();
    for (int col = 0; col < FreeSpinTriggerColumns; col++)
    {
      var icon = GetColumnFreeSpinIcon(col);
      if (icon == null) continue;
      originalWorld.Add(icon.transform.position);
      icon.Lift(animationOverlayParent);
      icons.Add(icon);
    }

    if (icons.Count == 0) yield break;

    // World-space convergence: anchor independent. All 3 icons overlap at the same center —
    // freeSpinCenterTarget if assigned, otherwise animationOverlayParent's world position.
    Vector3 centerWorld = freeSpinCenterTarget != null
      ? freeSpinCenterTarget.position
      : animationOverlayParent.position;

    // Move all icons to the same center in parallel — they overlap there during the two
    // triggered plays, then move back to their originals.
    Sequence moveOut = DOTween.Sequence();
    for (int i = 0; i < icons.Count; i++)
      moveOut.Join(icons[i].transform.DOMove(centerWorld, freeSpinCenterMoveDuration).SetEase(Ease.OutCubic));

    var playRoutines = new List<Coroutine>();
    for (int i = 0; i < icons.Count; i++)
      playRoutines.Add(StartCoroutine(PlayTriggeredTwice(icons[i])));

    yield return moveOut.WaitForCompletion();
    foreach (var c in playRoutines) if (c != null) yield return c;

    // Return to pre-Lift world positions.
    Sequence moveBack = DOTween.Sequence();
    for (int i = 0; i < icons.Count; i++)
      moveBack.Join(icons[i].transform.DOMove(originalWorld[i], freeSpinCenterMoveDuration).SetEase(Ease.InOutCubic));
    yield return moveBack.WaitForCompletion();

    // Third play — caller hooks in here to fade in the Start panel.
    onThirdPlayStart?.Invoke();
    var thirdPlays = new List<Coroutine>();
    for (int i = 0; i < icons.Count; i++)
      thirdPlays.Add(StartCoroutine(icons[i].PlayTriggeredOnce(freeSpinTriggeredSprites, freeSpinTriggeredSpeed)));
    foreach (var c in thirdPlays) if (c != null) yield return c;

    foreach (var icon in icons) icon.Drop();
  }

  IEnumerator PlayTriggeredTwice(SlotIconView icon)
  {
    yield return icon.PlayTriggeredOnce(freeSpinTriggeredSprites, freeSpinTriggeredSpeed);
    yield return icon.PlayTriggeredOnce(freeSpinTriggeredSprites, freeSpinTriggeredSpeed);
  }

  internal void ShuffleMatrix(bool ignoreResultMatrix = false)
  {
    HashSet<SlotIconView>[] resultSets = null;
    if (ignoreResultMatrix)
    {
      resultSets = new HashSet<SlotIconView>[slotMatrix.Count];
      for (int i = 0; i < slotMatrix.Count; i++)
        resultSets[i] = new HashSet<SlotIconView>(slotMatrix[i].slotImages);
    }

    for (int i = 0; i < allMatrix.Count; i++)
    {
      for (int j = 0; j < allMatrix[i].slotImages.Count; j++)
      {
        if (ignoreResultMatrix && i < resultSets.Length && resultSets[i].Contains(allMatrix[i].slotImages[j]))
          continue;
        int randomIndex = UnityEngine.Random.Range(0, iconImages.Length - 1);
        allMatrix[i].slotImages[j].SetIcon(ID: randomIndex, image: iconImages[randomIndex]);
      }
    }
  }

  void StopWinLoop()
  {
    if (WinLoopCorutine != null)
    {
      StopCoroutine(WinLoopCorutine);
      WinLoopCorutine = null;
    }
    if (scatterChainCoroutine != null)
    {
      StopCoroutine(scatterChainCoroutine);
      scatterChainCoroutine = null;
    }
    // Kill any inner PlayWinIteration coroutines spawned by PlaySyncedPass / SingleLineLoop /
    // PerLineLoop. StopCoroutine on the outer presentation doesn't reach these.
    for (int i = 0; i < _activeWinIterCoroutines.Count; i++)
    {
      if (_activeWinIterCoroutines[i] != null) StopCoroutine(_activeWinIterCoroutines[i]);
    }
    _activeWinIterCoroutines.Clear();
    // StopCoroutine doesn't run any cleanup, so symbols mid-PlayWinIteration stay parented to
    // animationOverlayParent with their border / win text / overlay state intact. Drop them now.
    if (_activeWinLines != null)
    {
      foreach (var lw in _activeWinLines) StopAnimateLineWin(lw);
      _activeWinLines = null;
    }
    SetDarkOverlay(false);
  }

  // Called by SlotIconView.PlayWinIteration at the start of each iteration so that
  // StopIconAnimation (invoked from StartSpin) can run StopAnim on every symbol whose
  // animation coroutine is still in flight when a new spin begins.
  internal void RegisterAnimatingIcon(SlotIconView icon)
  {
    if (icon != null && !animatingIcons.Contains(icon)) animatingIcons.Add(icon);
  }

  // Called when an animation completes naturally (e.g. diamond idle finishes and Drops itself).
  // Without this, the icon stays in the list until the next StopIconAnimation, which keeps a
  // stale reference around — and any teardown that iterates the list (StopAnim → ForceRestoreToRest)
  // hits it unnecessarily on the next spin.
  internal void UnregisterAnimatingIcon(SlotIconView icon)
  {
    if (icon != null) animatingIcons.Remove(icon);
  }

  internal void StopIconAnimation()
  {
    foreach (var item in animatingIcons)
    {
      item.StopAnim();
    }
    animatingIcons.Clear();
  }

  internal void ResetAllIcons()
  {
    foreach (var item in allMatrix)
    {
      foreach (var item1 in item.slotImages)
      {
        item1.Reset();
      }
    }
    // slotMatrix (visible window) may hold icon refs that are not present in allMatrix
    // (off-screen buffer). Reset is idempotent, so cover both lists to guarantee that any
    // symbol left mid-Lift from the previous spin's win loop is dropped and cleaned up.
    foreach (var col in slotMatrix)
    {
      foreach (var icon in col.slotImages)
      {
        icon.Reset();
      }
    }
  }

  // Reel travel speed in local units/second. Durations are derived from this so
  // every move (intro, loop, stop) runs at a constant speed regardless of distance.
  [SerializeField] private float reelSpeed = 2857f;

  private const float SpinTopY = 1900f;
  private const float SpinBottomY = -1900f;
  private const float RestY = 145.402496f;

  // Duration needed to travel between two Y positions at reelSpeed.
  private float DurationFor(float fromY, float toY)
    => Mathf.Abs(toY - fromY) / Mathf.Max(reelSpeed, 0.0001f);

  #region TweeningCode
  private Tween InitializeTweening(Transform slotTransform)
  {
    Sequence seq = DOTween.Sequence();
    float startY = slotTransform.localPosition.y;
    seq.Append(slotTransform.DOLocalMoveY(SpinBottomY, DurationFor(startY, SpinBottomY)).SetEase(Ease.Linear));
    seq.AppendCallback(() =>
    {
      slotTransform.localPosition = new Vector2(slotTransform.localPosition.x, SpinTopY);
      // ShuffleMatrix(ignoreResultMatrix: true);
      Tweener tweener = slotTransform.DOLocalMoveY(SpinBottomY, DurationFor(SpinTopY, SpinBottomY))
        .SetLoops(-1, LoopType.Restart)
        .SetEase(Ease.Linear);
      alltweens.Add(tweener);
    });
    return seq;
  }

  private void StopTweening(Transform slotTransform, int index)
  {
    alltweens[index].Kill();
    slotTransform.localPosition = new Vector2(slotTransform.localPosition.x, SpinTopY);
    alltweens[index] = slotTransform.DOLocalMoveY(RestY, DurationFor(SpinTopY, RestY)).SetEase(Ease.OutBack, 0.9f);
  }

  private void KillAllTweens()
  {
    for (int i = 0; i < alltweens.Count; i++)
    {
      alltweens[i].Kill();
    }
    alltweens.Clear();
  }
  #endregion

  internal static bool TryParseDiamondPos(string pos, out int row, out int col) => TryParsePosition(pos, out row, out col);

  // Parses a "row,col" position string from the server lineWins payload.
  static bool TryParsePosition(string pos, out int row, out int col)
  {
    row = col = 0;
    var parts = pos?.Split(',');
    if (parts == null || parts.Length != 2) return false;
    return int.TryParse(parts[0], out row) && int.TryParse(parts[1], out col);
  }

  // Yields until the scatter (free-spin symbol) animation chain started in StopSpin has finished,
  // so the win-line presentation always runs after the scatter animations.
  internal IEnumerator WaitForScatterChain()
  {
    if (scatterChainCoroutine != null) yield return scatterChainCoroutine;
  }

  internal IEnumerator AnimateLineWins(List<LineWin> lineWins)
  {
    // During a free-spin loop spinsRemaining > 0 means more spins coming, so present one-shot
    // (auto-style). On the last spin spinsRemaining is 0 and we want the indefinite manual loop
    // so the user can read the final win while the end panel fades in.
    bool freeSpinActive = gameManager.isFreeSpin && gameManager.freeSpinController != null
                          && gameManager.freeSpinController.spinsRemaining > 0;
    bool autoStart = gameManager.isAutoSpin || freeSpinActive;

    if (!autoStart)
    {
      // Manual spin: run the whole presentation (synced pass -> loop) in the background so the
      // caller returns immediately and the bottom-bar buttons are re-enabled while the synced pass
      // is still playing. The user can skip / start the next spin at any point; the next StartSpin
      // kills this via StopWinLoop().
      _activeWinLines = lineWins;
      WinLoopCorutine = StartCoroutine(WinPresentation(lineWins));
      yield break;
    }

    // Auto / free spin: block on the synced pass to keep the win visible before the next chained
    // spin. Wait for the scatter animations first.
    yield return WaitForScatterChain();
    bool singleLine = lineWins.Count == 1;
    yield return PlaySyncedPass(lineWins, showPayouts: singleLine);

    // Free spin always stops here. Auto spin stops only if the user hasn't pressed stop mid-pass.
    // StopAutoSpinCoroutine flips isAutoSpin and blocks on !isSpinning, so the background loop we
    // kick off below survives the SpinRoutine returning and gets cleaned up on the next StartSpin.
    if (gameManager.isAutoSpin || gameManager.isFreeSpin)
    {
      // Leave the line-win animations running — the next StartSpin's StopWinLoop walks
      // _activeWinLines to tear them down, so we just register them for cleanup and exit.
      _activeWinLines = lineWins;
      yield break;
    }

    _activeWinLines = lineWins;
    WinLoopCorutine = StartCoroutine(WinLoopAfterSync(lineWins, singleLine));
  }

  // Mid-flow upgrade path: the synced pass already ran inline on the auto branch, so resume with
  // just the loop portion of WinPresentation.
  IEnumerator WinLoopAfterSync(List<LineWin> lineWins, bool singleLine)
  {
    if (singleLine)
    {
      yield return SingleLineLoop(lineWins[0]);
    }
    else
    {
      SetDarkOverlay(true);
      foreach (var lineWin in lineWins) StopAnimateLineWin(lineWin);
      yield return PerLineLoop(lineWins);
    }
  }

  // Manual-spin win presentation: wait for the scatter animations, then the synced first pass
  // (no darkening), then an indefinite loop.
  IEnumerator WinPresentation(List<LineWin> lineWins)
  {
    _activeWinLines = lineWins;
    yield return WaitForScatterChain();

    bool singleLine = lineWins.Count == 1;

    // Synchronized first pass shows every winning line with no darkening. Payouts shown only
    // when there is exactly one winning line.
    yield return PlaySyncedPass(lineWins, showPayouts: singleLine);

    if (singleLine)
    {
      // Icons stay on; loop the per-iteration animations indefinitely with the line payout still visible.
      yield return SingleLineLoop(lineWins[0]);
    }
    else
    {
      // Darken the whole slot for the multi-line looping pass; animating symbols lift above the overlay.
      SetDarkOverlay(true);
      foreach (var lineWin in lineWins) StopAnimateLineWin(lineWin);
      yield return PerLineLoop(lineWins);
    }
  }

  IEnumerator PlaySyncedPass(List<LineWin> lineWins, bool showPayouts)
  {
    // Payout label sits on the middle symbol of each winning line.
    var midPerLine = new Dictionary<(int col, int row), double>();
    if (showPayouts)
    {
      foreach (var lineWin in lineWins)
      {
        int c = lineWin.positions.Count;
        if (c == 0) continue;
        if (!TryParsePosition(lineWin.positions[c / 2], out int midRow, out int midCol)) continue;
        if (midCol < 0 || midCol >= slotMatrix.Count) continue;
        if (midRow < 0 || midRow >= slotMatrix[midCol].slotImages.Count) continue;
        // Skip the payout label when the middle symbol is a wild.
        if (slotMatrix[midCol].slotImages[midRow].id == WildId) continue;
        midPerLine[(midCol, midRow)] = lineWin.winAmount;
      }
    }

    var seen = new HashSet<(int, int)>();
    _activeWinIterCoroutines.Clear();
    foreach (var lineWin in lineWins)
    {
      for (int i = 0; i < lineWin.positions.Count; i++)
      {
        if (!TryParsePosition(lineWin.positions[i], out int row, out int col)) continue;
        if (col < 0 || col >= slotMatrix.Count) continue;
        if (row < 0 || row >= slotMatrix[col].slotImages.Count) continue;
        if (!seen.Add((col, row))) continue;

        bool show = midPerLine.TryGetValue((col, row), out double payout);
        _activeWinIterCoroutines.Add(StartCoroutine(slotMatrix[col].slotImages[row].PlayWinIteration(this, animationOverlayParent, show, show ? payout : 0, isSyncedPass: true)));
      }
    }
    for (int i = 0; i < _activeWinIterCoroutines.Count; i++)
      if (_activeWinIterCoroutines[i] != null) yield return _activeWinIterCoroutines[i];
    _activeWinIterCoroutines.Clear();
  }

  IEnumerator SingleLineLoop(LineWin lineWin)
  {
    while (true)
    {
      yield return new WaitForSecondsRealtime(betweenLineDelay);

      _activeWinIterCoroutines.Clear();
      int count = lineWin.positions.Count;
      int midIndex = count / 2;
      for (int i = 0; i < count; i++)
      {
        if (!TryParsePosition(lineWin.positions[i], out int row, out int col)) continue;
        if (col < 0 || col >= slotMatrix.Count) continue;
        if (row < 0 || row >= slotMatrix[col].slotImages.Count) continue;
        bool show = i == midIndex && slotMatrix[col].slotImages[row].id != WildId;
        _activeWinIterCoroutines.Add(StartCoroutine(slotMatrix[col].slotImages[row].PlayWinIteration(this, animationOverlayParent, show, show ? lineWin.winAmount : 0, isSyncedPass: false)));
      }
      for (int i = 0; i < _activeWinIterCoroutines.Count; i++)
        if (_activeWinIterCoroutines[i] != null) yield return _activeWinIterCoroutines[i];
      _activeWinIterCoroutines.Clear();
    }
  }

  IEnumerator PerLineLoop(List<LineWin> lineWins)
  {
    while (true)
    {
      foreach (var lineWin in lineWins)
      {
        _activeWinIterCoroutines.Clear();
        int count = lineWin.positions.Count;
        int midIndex = count / 2;
        for (int i = 0; i < count; i++)
        {
          if (!TryParsePosition(lineWin.positions[i], out int row, out int col)) continue;
          if (col < 0 || col >= slotMatrix.Count) continue;
          if (row < 0 || row >= slotMatrix[col].slotImages.Count) continue;
          bool show = i == midIndex && slotMatrix[col].slotImages[row].id != WildId;
          double payout = show ? lineWin.winAmount : 0;
          _activeWinIterCoroutines.Add(StartCoroutine(slotMatrix[col].slotImages[row].PlayWinIteration(this, animationOverlayParent, show, payout, isSyncedPass: false)));
        }
        for (int i = 0; i < _activeWinIterCoroutines.Count; i++)
          if (_activeWinIterCoroutines[i] != null) yield return _activeWinIterCoroutines[i];
        _activeWinIterCoroutines.Clear();

        yield return new WaitForSecondsRealtime(betweenLineDelay);
        StopAnimateLineWin(lineWin);
      }
    }
  }

  void StopAnimateLineWin(LineWin lineWins)
  {
    for (int i = 0; i < lineWins.positions.Count; i++)
    {
      if (!TryParsePosition(lineWins.positions[i], out int row, out int col)) continue;
      if (col < 0 || col >= slotMatrix.Count) continue;
      if (row < 0 || row >= slotMatrix[col].slotImages.Count) continue;

      slotMatrix[col].slotImages[row].ResetLineAnim();
    }
  }

  // Single-diamond idle path: caller (GameManager.OnSpinEnd) starts this and does not yield.
  internal IEnumerator PlayDiamondIdle(int row, int col)
  {
    if (col < 0 || col >= slotMatrix.Count) yield break;
    if (row < 0 || row >= slotMatrix[col].slotImages.Count) yield break;
    yield return slotMatrix[col].slotImages[row].PlayDiamondIdleAnim(
      this, animationOverlayParent, diamondIdleSprites, diamondIdleSpeed);
  }

  // Diamond-trigger path: fires each diamond's looped, oversized overlay. Returns immediately;
  // the loops run until StartSpin -> StopIconAnimation tears them down on the next spin.
  internal void StartDiamondTriggered(List<string> diamondPositions)
  {
    if (diamondPositions == null) return;
    audioController.Play("diamond");
    for (int i = 0; i < diamondPositions.Count; i++)
    {
      if (!TryParsePosition(diamondPositions[i], out int row, out int col)) continue;
      if (col < 0 || col >= slotMatrix.Count) continue;
      if (row < 0 || row >= slotMatrix[col].slotImages.Count) continue;
      StartCoroutine(slotMatrix[col].slotImages[row].PlayDiamondTriggeredLoop(
        this, animationOverlayParent, diamondTriggeredSprites, diamondTriggeredSpeed));
    }
  }

  // Auto-spin variant: plays each diamond's triggered animation as a single non-looped cycle in
  // parallel and yields until they have all completed once (each icon resets itself on completion).
  internal IEnumerator PlayDiamondTriggeredCycle(List<string> diamondPositions)
  {
    if (diamondPositions == null) yield break;
    audioController.Play("diamond");
    var coros = new List<Coroutine>();
    for (int i = 0; i < diamondPositions.Count; i++)
    {
      if (!TryParsePosition(diamondPositions[i], out int row, out int col)) continue;
      if (col < 0 || col >= slotMatrix.Count) continue;
      if (row < 0 || row >= slotMatrix[col].slotImages.Count) continue;
      coros.Add(StartCoroutine(slotMatrix[col].slotImages[row].PlayDiamondTriggeredOnce(
        this, animationOverlayParent, diamondTriggeredSprites, diamondTriggeredSpeed)));
    }
    for (int i = 0; i < coros.Count; i++)
      if (coros[i] != null) yield return coros[i];
  }

  internal void SetDarkOverlay(bool state)
  {
    if (darkOverlay != null) darkOverlay.SetActive(state);
  }
}

[Serializable]
public class SlotImage
{
  public List<SlotIconView> slotImages = new List<SlotIconView>(10);
}

[Serializable]
public class SymbolWinAnim
{
  public int id;
  public List<Sprite> syncedSprites = new List<Sprite>(); // first synced pass frames
  public float syncedSpeed = 5f;
  public List<Sprite> loopSprites = new List<Sprite>();    // single/multiline loop pass frames
  public float loopSpeed = 5f;
  public bool loopUsesPulse = false;                       // gold bars (id 0,1): pulse instead of frames on loop
}
