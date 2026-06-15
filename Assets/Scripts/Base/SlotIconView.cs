using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SlotIconView : MonoBehaviour
{
  [Header("required fields")]
  [SerializeField] internal int pos;
  [SerializeField] internal int id = -1;
  [SerializeField] internal Image iconImage;
  [SerializeField] internal ImageAnimation bgImage;
  [SerializeField] internal ImageAnimation borderAnimation;
  [SerializeField] private TMP_Text WinAmountText;

  [Header("Special Symbol Animation Overlay")]
  [SerializeField] internal Image AnimLayerImage;
  [SerializeField] internal ImageAnimation AnimLayerIA;

  [Header("Win Iteration")]
  [SerializeField] private float winPulsePeak = 1.05f;
  [SerializeField] private float winPulseDuration = 0.7f;

  internal bool IsSpecialSymbol => id == 9 || id == 10;

  [Header("Debug")]
  [SerializeField] private bool previewAnimations = false;

  Tween iconAnim;
  // Awake-time "rest" snapshot — the prefab parent + sibling index + localPosition. Reset() and
  // StopAnim() force-restore to this regardless of Lift/Drop cache state, so an icon stranded on
  // animationOverlayParent (or any wrong parent) is always brought home on the next spin.
  private Transform _restParent;
  private int _restSiblingIndex;
  private Vector3 _restLocalPosition;
  // Lift/Drop pair: captured at Lift() time, cleared by Drop(). Null _cachedParent means
  // "not currently lifted" — so a re-Lift while already lifted won't overwrite the snapshot
  // with the overlay parent.
  private Transform _cachedParent;
  private int _cachedSiblingIndex;
  private Vector3 _cachedLocalPosition;
  private Vector2 _specialAnimDefaultSize;
  private Vector3 _specialAnimDefaultAnchoredPos;
  private Vector2 _iconImageDefaultSize;
  private Vector3 _iconImageDefaultAnchoredPos;
  private Vector2 _bgDefaultSize;
  private Vector3 _bgDefaultAnchoredPos;
  private float _bgDefaultSpeed;
  private float _specialAnimDefaultSpeed;

  static readonly Vector2 LadySpecialAnimSize = new Vector2(350.1554f, 285.9313f);
  static readonly Vector3 LadySpecialAnimPos = new Vector3(0f, 11.0395f, 0f);
  static readonly Vector2 WildSpecialAnimSize = new Vector2(320f, 320f);
  static readonly Vector3 WildSpecialAnimPos = Vector3.zero;
  static readonly Vector2 WildBgSize = new Vector2(320f, 328.472f);
  static readonly Vector3 WildBgPos = Vector3.zero;

  const int WildId = 5;
  const int GoldAId = 0;
  const int GoldBId = 1;
  static readonly Vector2 WildWinSizeBump = new Vector2(250f, 250f);
  // Hardcoded rest sizes for iconImage and AnimLayerImage. Awake-time snapshots of
  // rectTransform.sizeDelta were unreliable in play mode (some cells captured 0,0 or 250,250),
  // so we authoritatively pin defaults here instead of trusting the runtime rect state.
  static readonly Vector2 IconImageDefaultSize = new Vector2(350f, 350f);
  static readonly Vector2 AnimLayerDefaultSize = new Vector2(350f, 350f);
  [SerializeField] private float wildYOffset = 0f;
  [SerializeField] private Vector2 goldSizeBump = Vector2.zero;
  [SerializeField] private float goldYOffset = 0f;

  Vector2 SizeBumpFor(int symbolId) =>
    symbolId == WildId ? WildWinSizeBump :
    (symbolId == GoldAId || symbolId == GoldBId) ? goldSizeBump :
    Vector2.zero;

  float YOffsetFor(int symbolId) =>
    symbolId == WildId ? wildYOffset :
    (symbolId == GoldAId || symbolId == GoldBId) ? goldYOffset :
    0f;

  void Awake()
  {
    _restParent = transform.parent;
    _restSiblingIndex = transform.GetSiblingIndex();
    _restLocalPosition = transform.localPosition;
    if (iconImage != null)
    {
      _iconImageDefaultSize = IconImageDefaultSize;
      _iconImageDefaultAnchoredPos = iconImage.rectTransform.anchoredPosition3D;
    }
    if (AnimLayerImage != null)
    {
      _specialAnimDefaultSize = AnimLayerDefaultSize;
      _specialAnimDefaultAnchoredPos = AnimLayerImage.rectTransform.anchoredPosition3D;
    }
    if (AnimLayerIA != null)
    {
      _specialAnimDefaultSpeed = AnimLayerIA.AnimationSpeed;
      AnimLayerIA.StopAnimation();
      AnimLayerIA.ResetToFirstFrame();
    }
    if (AnimLayerImage != null) AnimLayerImage.color = new Color(1f, 1f, 1f, 0f);
    if (bgImage != null)
    {
      _bgDefaultSpeed = bgImage.AnimationSpeed;
      var bgRt = bgImage.GetComponent<RectTransform>();
      if (bgRt != null)
      {
        _bgDefaultSize = bgRt.sizeDelta;
        _bgDefaultAnchoredPos = bgRt.anchoredPosition3D;
      }
    }
    if(borderAnimation.gameObject.activeInHierarchy)
      borderAnimation.gameObject.SetActive(false);
  }

  // void Start()
  // {
    // if (!previewAnimations) return;

    // bgImage.gameObject.SetActive(true);
    // bgImage.StartAnimation();

    // borderAnimation.gameObject.SetActive(true);
    // borderAnimation.StartAnimation();
  // }

  internal void Lift(Transform overlayParent)
  {
    if (overlayParent == null) return;
    if (transform.parent == overlayParent) return; // already lifted; don't recache
    // Snapshot the pre-lift state so Drop() restores to exactly where we were, regardless
    // of any layout drift since Awake.
    if (_cachedParent == null)
    {
      _cachedParent = transform.parent;
      _cachedSiblingIndex = transform.GetSiblingIndex();
      _cachedLocalPosition = transform.localPosition;
    }
    transform.SetParent(overlayParent, worldPositionStays: true);
  }

  internal void Drop()
  {
    if (_cachedParent != null)
    {
      transform.SetParent(_cachedParent, worldPositionStays: false);
      transform.SetSiblingIndex(_cachedSiblingIndex);
      transform.localPosition = _cachedLocalPosition;
      _cachedParent = null;
      return;
    }
    // No lift cache but the icon isn't where it belongs (e.g., previous lift skipped caching
    // due to a re-entrancy guard). Fall back to the Awake-time rest snapshot rather than
    // leaving the symbol stranded.
    if (_restParent != null && transform.parent != _restParent)
      ForceRestoreToRest();
  }

  // Unconditionally snap back to the Awake-captured rest state. Used by Reset/StopAnim during
  // spin-start so a symbol stranded on the wrong parent (e.g., animationOverlayParent because a
  // mid-pulse coroutine was killed before Drop could run) is always brought home.
  private void ForceRestoreToRest()
  {
    if (_restParent == null) return;
    if (transform.parent != _restParent)
      transform.SetParent(_restParent, worldPositionStays: false);
    transform.SetSiblingIndex(_restSiblingIndex);
    transform.localPosition = _restLocalPosition;
    _cachedParent = null;
  }

  internal void SetIcon(Sprite image, int ID)
  {
    iconImage.sprite = image;
    id = ID;
    // Debug.Log(_iconImageDefaultSize);
    iconImage.rectTransform.sizeDelta = _iconImageDefaultSize + SizeBumpFor(ID);
    iconImage.rectTransform.anchoredPosition3D = _iconImageDefaultAnchoredPos + new Vector3(0f, YOffsetFor(ID), 0f);
  }

  internal void Reset()
  {
    ForceRestoreToRest();

    // Kill any running tween
    iconAnim?.Kill();
    iconAnim = null;

    // Reset visuals
    borderAnimation.StopAnimation();
    borderAnimation.doLoopAnimation = false;
    borderAnimation.gameObject.SetActive(false);
    if (WinAmountText.gameObject.activeSelf)
    {
      WinAmountText.gameObject.SetActive(false);
      WinAmountText.text = "";
    }

    // Stop animations safely
    bgImage.StopAnimation();
    bgImage.ResetToFirstFrame();
    bgImage.doLoopAnimation = false;
    bgImage.AnimationSpeed = _bgDefaultSpeed;
    {
      var bgRt = bgImage.GetComponent<RectTransform>();
      if (bgRt != null)
      {
        bgRt.sizeDelta = _bgDefaultSize;
        bgRt.anchoredPosition3D = _bgDefaultAnchoredPos;
      }
    }

    if (AnimLayerIA != null)
    {
      AnimLayerIA.StopAnimation();
      AnimLayerIA.ResetToFirstFrame();
      AnimLayerIA.AnimationSpeed = _specialAnimDefaultSpeed;
    }
    if (AnimLayerImage != null)
    {
      AnimLayerImage.color = new Color(1f, 1f, 1f, 0f);
      AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize;
      AnimLayerImage.rectTransform.anchoredPosition3D = _specialAnimDefaultAnchoredPos;
    }
    // Spin-start cleanup: clear iconImage to absolute defaults. SetIcon (via PopulateSlotMatrix
    // and ShuffleMatrix) re-applies the per-id bump for the new symbol. Keeping the bump here
    // based on the lingering wild id left the next spin's reel symbols visibly offset because
    // off-screen positions never get a fresh SetIcon to clear it.
    iconImage.rectTransform.sizeDelta = _iconImageDefaultSize;
    iconImage.rectTransform.anchoredPosition3D = _iconImageDefaultAnchoredPos;
    iconImage.color = new Color(1f, 1f, 1f, 1f);
    iconImage.enabled = true;

    // Reset transform
    iconImage.transform.localScale = Vector3.one;
  }

  internal IEnumerator PlayWinIteration(SlotController controller, Transform overlayParent, bool showWinLineText, double winAmount, bool isSyncedPass)
  {
    controller.RegisterAnimatingIcon(this);
    Lift(overlayParent);

    if (AnimLayerImage != null)
    {
      AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize + SizeBumpFor(id);
      AnimLayerImage.rectTransform.anchoredPosition3D = _specialAnimDefaultAnchoredPos + new Vector3(0f, YOffsetFor(id), 0f);
    }

    if (showWinLineText)
    {
      WinAmountText.gameObject.SetActive(true);
      WinAmountText.text = TextFormatter.FormatSprite(winAmount, TextFormatter.GetSignificantDecimals(winAmount), true);
    }

    borderAnimation.gameObject.SetActive(true);
    borderAnimation.doLoopAnimation = true;
    borderAnimation.delayBetweenLoop = 0f;
    borderAnimation.StartAnimation();

    // iconAnim?.Kill();
    // iconImage.transform.localScale = Vector3.one;
    // float halfPulse = winPulseDuration * 0.5f;
    // Sequence pulseSeq = DOTween.Sequence();
    // pulseSeq.Append(iconImage.transform.DOScale(Vector3.one * winPulsePeak, halfPulse).SetEase(Ease.OutSine));
    // pulseSeq.Append(iconImage.transform.DOScale(Vector3.one, halfPulse).SetEase(Ease.InSine));
    // pulseSeq.OnKill(() => iconAnim = null);
    // iconAnim = pulseSeq;

    // yield return new WaitUntil(() =>
    // {
    //   // bool pulseDone = pulseSeq == null || !pulseSeq.IsActive() || !pulseSeq.IsPlaying();
    //   // bool bgDone = !bgImage.isplaying;
    //   // bool specialDone = !IsSpecialSymbol || specialAnim == null || !specialAnim.isplaying;
    //   bool borderDone = !IsSpecialSymbol || !borderAnimation.isplaying;
    //   return borderDone; //&& bgDone && specialDone && borderDone;
    // });

    var winAnim = controller.GetWinAnim(id);

    if (isSyncedPass)
    {
      if (winAnim != null && winAnim.syncedSprites != null && winAnim.syncedSprites.Count > 0)
        yield return PlayOverlaySequence(winAnim.syncedSprites, winAnim.syncedSpeed);
      else
        yield return new WaitForSecondsRealtime(2f); // fallback: ids without sequences yet (2,3,…)
    }
    else // loop pass
    {
      if (winAnim != null && winAnim.loopUsesPulse)
        yield return PlayPulse();                          // gold bars id 0,1
      else if (winAnim != null && winAnim.loopSprites != null && winAnim.loopSprites.Count > 0)
        yield return PlayOverlaySequence(winAnim.loopSprites, winAnim.loopSpeed);
      else
        yield return new WaitForSecondsRealtime(2f);       // fallback
    }
  }

  // Free-spin (scatter) symbol animation, played in place on reel stop (no lift / no dark overlay).
  internal IEnumerator PlayFreeSpinAnim(List<Sprite> sprites, float speed)
  {
    if (sprites == null || sprites.Count == 0) yield break;
    yield return PlayOverlaySequence(sprites, speed);
  }

  // Single-diamond idle: one-shot overlay at default AnimLayer size. Lifts to overlayParent so
  // it stays above siblings, drops back when finished. Non-blocking from the caller's perspective —
  // they StartCoroutine without yielding.
  internal IEnumerator PlayDiamondIdleAnim(SlotController controller, Transform overlayParent, List<Sprite> sprites, float speed)
  {
    if (sprites == null || sprites.Count == 0) yield break;
    controller.RegisterAnimatingIcon(this);
    Lift(overlayParent);
    yield return PlayOverlaySequence(sprites, speed);
    Drop();
    controller.UnregisterAnimatingIcon(this);
  }

  // Multi-diamond trigger: looped overlay at +100 width/height. Yields forever — caller fires it
  // and forgets; teardown happens via StopAnim/Reset on next spin (which restores
  // _specialAnimDefaultSize and Drops the icon).
  internal IEnumerator PlayDiamondTriggeredLoop(SlotController controller, Transform overlayParent, List<Sprite> sprites, float speed)
  {
    if (sprites == null || sprites.Count == 0 || AnimLayerIA == null || AnimLayerImage == null) yield break;
    controller.RegisterAnimatingIcon(this);
    Lift(overlayParent);
    AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize + new Vector2(100f, 100f);
    iconImage.enabled = false;
    AnimLayerImage.color = new Color(1f, 1f, 1f, 1f);
    AnimLayerIA.AnimationSpeed = speed;
    AnimLayerIA.holdAtLastFrame = false;
    AnimLayerIA.gameObject.SetActive(true);
    AnimLayerIA.PlaySequence(sprites, loop: true);
    // Keep coroutine alive so the icon stays registered in animatingIcons until StopIconAnimation
    // is called on the next spin (which invokes StopAnim and unwinds the +100 size).
    while (AnimLayerIA != null && AnimLayerIA.isplaying) yield return null;
  }

  // Auto-spin variant: plays the triggered sequence once (non-looped) and self-restores rest state
  // so the next auto-spin can start clean without relying on StopIconAnimation.
  internal IEnumerator PlayDiamondTriggeredOnce(SlotController controller, Transform overlayParent, List<Sprite> sprites, float speed)
  {
    if (sprites == null || sprites.Count == 0 || AnimLayerIA == null || AnimLayerImage == null) yield break;
    controller.RegisterAnimatingIcon(this);
    Lift(overlayParent);
    AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize + new Vector2(100f, 100f);
    iconImage.enabled = false;
    AnimLayerImage.color = new Color(1f, 1f, 1f, 1f);
    AnimLayerIA.AnimationSpeed = speed;
    AnimLayerIA.holdAtLastFrame = false;
    AnimLayerIA.gameObject.SetActive(true);
    AnimLayerIA.PlaySequence(sprites, loop: false);
    yield return new WaitUntil(() => AnimLayerIA == null || !AnimLayerIA.isplaying);

    if (AnimLayerIA != null) { AnimLayerIA.StopAnimation(); AnimLayerIA.ResetToFirstFrame(); }
    if (AnimLayerImage != null)
    {
      AnimLayerImage.color = new Color(1f, 1f, 1f, 0f);
      AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize;
      AnimLayerImage.gameObject.SetActive(false);
    }
    iconImage.enabled = true;
    Drop();
    controller.UnregisterAnimatingIcon(this);
  }

  // Free-spin trigger centered sequence: one non-looped overlay play. Lift/Drop are the caller's
  // responsibility (SlotController.PlayFreeSpinTriggeredSequence wraps three of these around the
  // converge-and-return move). Restores AnimLayer to default size on exit so the icon can be
  // safely re-played without inheriting stale geometry.
  internal IEnumerator PlayTriggeredOnce(List<Sprite> sprites, float speed)
  {
    if (sprites == null || sprites.Count == 0 || AnimLayerIA == null || AnimLayerImage == null) yield break;
    AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize;
    AnimLayerImage.rectTransform.anchoredPosition3D = _specialAnimDefaultAnchoredPos;
    yield return PlayOverlaySequence(sprites, speed);
  }

  IEnumerator PlayOverlaySequence(List<Sprite> sprites, float speed)
  {
    if (AnimLayerIA == null || AnimLayerImage == null)
    {
      yield return new WaitForSecondsRealtime(2f);
      yield break;
    }
    iconImage.enabled = false; // hide the static icon rendering behind the overlay while it plays
    AnimLayerImage.color = new Color(1f, 1f, 1f, 1f);
    AnimLayerIA.AnimationSpeed = speed;
    AnimLayerIA.holdAtLastFrame = false;
    AnimLayerIA.gameObject.SetActive(true);
    AnimLayerIA.PlaySequence(sprites, loop: false);
    yield return new WaitUntil(() => !AnimLayerIA.isplaying);
    AnimLayerIA.StopAnimation();
    AnimLayerIA.ResetToFirstFrame();
    AnimLayerImage.color = new Color(1f, 1f, 1f, 0f); // hide overlay between iterations
    AnimLayerIA.gameObject.SetActive(false);
    iconImage.enabled = true;
  }

  IEnumerator PlayPulse()
  {
    iconAnim?.Kill();
    iconImage.transform.localScale = Vector3.one;
    float halfPulse = winPulseDuration * 0.5f;
    Sequence pulseSeq = DOTween.Sequence();
    pulseSeq.Append(iconImage.transform.DOScale(Vector3.one * winPulsePeak, halfPulse).SetEase(Ease.OutSine));
    pulseSeq.Append(iconImage.transform.DOScale(Vector3.one, halfPulse).SetEase(Ease.InSine));
    pulseSeq.OnKill(() => iconAnim = null);
    iconAnim = pulseSeq;
    yield return pulseSeq.WaitForCompletion();
  }

  internal void ResetLineAnim()
  {
    Drop();
    iconAnim?.Kill();
    iconAnim = null;
    iconImage.transform.localScale = Vector3.one;
    borderAnimation.doLoopAnimation = false;
    borderAnimation.StopAnimation();
    borderAnimation.gameObject.SetActive(false);
    bgImage.StopAnimation();
    bgImage.ResetToFirstFrame();
    bgImage.doLoopAnimation = false;
    bgImage.AnimationSpeed = _bgDefaultSpeed;
    {
      var bgRt = bgImage.GetComponent<RectTransform>();
      if (bgRt != null)
      {
        bgRt.sizeDelta = _bgDefaultSize;
        bgRt.anchoredPosition3D = _bgDefaultAnchoredPos;
      }
    }
    if (AnimLayerIA != null)
    {
      AnimLayerIA.StopAnimation();
      AnimLayerIA.ResetToFirstFrame();
      AnimLayerIA.AnimationSpeed = _specialAnimDefaultSpeed;
    }
    if (AnimLayerImage != null)
    {
      AnimLayerImage.color = new Color(1f, 1f, 1f, 0f);
      AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize;
      AnimLayerImage.rectTransform.anchoredPosition3D = _specialAnimDefaultAnchoredPos;
    }
    iconImage.rectTransform.sizeDelta = _iconImageDefaultSize + SizeBumpFor(id);
    iconImage.rectTransform.anchoredPosition3D = _iconImageDefaultAnchoredPos + new Vector3(0f, YOffsetFor(id), 0f);
    iconImage.color = new Color(1f, 1f, 1f, 1f);
    iconImage.enabled = true;
    if (WinAmountText.gameObject.activeSelf)
    {
      WinAmountText.gameObject.SetActive(false);
      WinAmountText.text = "";
    }
  }

  internal void StopAnim()
  {
    ForceRestoreToRest();
    iconAnim?.Kill();
    iconImage.transform.localScale = Vector3.one;
    borderAnimation.StopAnimation();
    borderAnimation.gameObject.SetActive(false);
    bgImage.StopAnimation();
    bgImage.ResetToFirstFrame();
    bgImage.AnimationSpeed = _bgDefaultSpeed;
    {
      var bgRt = bgImage.GetComponent<RectTransform>();
      if (bgRt != null)
      {
        bgRt.sizeDelta = _bgDefaultSize;
        bgRt.anchoredPosition3D = _bgDefaultAnchoredPos;
      }
    }
    if (AnimLayerIA != null)
    {
      AnimLayerIA.StopAnimation();
      AnimLayerIA.ResetToFirstFrame();
      AnimLayerIA.AnimationSpeed = _specialAnimDefaultSpeed;
    }
    if (AnimLayerImage != null)
    {
      AnimLayerImage.color = new Color(1f, 1f, 1f, 0f);
      AnimLayerImage.rectTransform.sizeDelta = _specialAnimDefaultSize;
      AnimLayerImage.rectTransform.anchoredPosition3D = _specialAnimDefaultAnchoredPos;
    }
    // Same rationale as Reset(): clear to absolute defaults at spin-start so a lingering wild
    // id can't leave the next spin's reels visibly shifted at that position.
    iconImage.rectTransform.sizeDelta = _iconImageDefaultSize;
    iconImage.rectTransform.anchoredPosition3D = _iconImageDefaultAnchoredPos;
    iconImage.color = new Color(1f, 1f, 1f, 1f);
    iconImage.enabled = true;
  }
}
