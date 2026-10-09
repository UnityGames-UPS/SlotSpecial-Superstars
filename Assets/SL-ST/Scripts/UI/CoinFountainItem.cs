using System;
using DG.Tweening;
using UnityEngine;

// One coin in the win fountain. Lives on the prefab PARENT: this object handles the parabolic flight
// (DOAnchorPos) + fade, while the child ImageAnimation plays the looping coin flip. Splitting parent /
// child lets the parent own a random spawn rotation and the arc without disturbing the flip animation.
[RequireComponent(typeof(RectTransform))]
public class CoinFountainItem : MonoBehaviour
{
  [SerializeField] private CanvasGroup canvasGroup; // on this parent — single fade source
  [SerializeField] private ImageAnimation flipAnim; // child looping coin-flip animation
  [SerializeField] private RectTransform rect;      // this parent's RectTransform

  private Sequence _flightSeq;
  private Tween _fadeTween;

  private void OnValidate()
  {
    if (rect == null) rect = GetComponent<RectTransform>();
    if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
  }

  // Launches one parabolic flight from startAnchoredPos. The coin fades in fast, rises by riseY then
  // falls back, drifting driftX in X across the whole flight (driftX may be negative = left). onComplete
  // fires when the arc finishes so the pool can reclaim the coin.
  internal void Launch(Vector2 startAnchoredPos, float fadeInDur, float riseY, float driftX,
                       float upDur, float downDur, float downToY, Ease upEase, Ease downEase, Action onComplete)
  {
    KillTweens();

    if (rect != null)
    {
      rect.anchoredPosition = startAnchoredPos;
      // Random spawn rotation 0-360 so coins don't look uniform.
      rect.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
    }
    if (canvasGroup != null) canvasGroup.alpha = 0f;
    if (flipAnim != null) flipAnim.StartAnimation();

    _flightSeq = DOTween.Sequence();
    // Vertical parabola: rise above the origin, then fall all the way down to downToY (off-screen).
    _flightSeq.Append(rect.DOAnchorPosY(startAnchoredPos.y + riseY, upDur).SetEase(upEase));
    _flightSeq.Append(rect.DOAnchorPosY(downToY, downDur).SetEase(downEase));
    // Fast fade-in, inserted at t=0 so it runs in parallel with the rise.
    if (canvasGroup != null)
      _flightSeq.Insert(0f, canvasGroup.DOFade(1f, fadeInDur));
    // Single horizontal drift spanning the full flight, also from t=0.
    _flightSeq.Insert(0f, rect.DOAnchorPosX(startAnchoredPos.x + driftX, upDur + downDur).SetEase(Ease.Linear));
    _flightSeq.OnComplete(() => onComplete?.Invoke());
  }

  // Fades the coin out but keeps the flip playing (used during the celebration's graceful fade-out).
  internal Tween FadeOut(float duration)
  {
    if (canvasGroup == null) return null;
    _fadeTween?.Kill();
    _fadeTween = canvasGroup.DOFade(0f, duration);
    return _fadeTween;
  }

  // Full reset before the coin returns to the pool.
  internal void ResetState()
  {
    KillTweens();
    if (flipAnim != null) flipAnim.StopAnimation();
    if (canvasGroup != null) canvasGroup.alpha = 0f;
  }

  private void OnDisable()
  {
    KillTweens();
  }

  private void KillTweens()
  {
    _flightSeq?.Kill();
    _flightSeq = null;
    _fadeTween?.Kill();
    _fadeTween = null;
  }
}
