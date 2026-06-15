using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class FallingParticleView : MonoBehaviour
{
  [SerializeField] internal Image rendererDelegate;
  [SerializeField] internal ImageAnimation coinAnim;
  [SerializeField] internal bool isDiamond;
  [SerializeField] private float diamondRotateDegrees = 180f;

  private Tween _moveTween;
  private Tween _rotateTween;
  private Tween _fadeTween;
  private Action _onComplete;
  private RectTransform _rt;

  private void Awake()
  {
    _rt = (RectTransform)transform;
    if (rendererDelegate == null) rendererDelegate = GetComponent<Image>();
  }

  internal void Play(Vector3 startWorldPos, float startRotZ, float deltaY, float fallDuration, float coinAnimSpeed, Action onComplete)
  {
    KillTweens();
    _onComplete = onComplete;

    transform.position = startWorldPos;
    transform.localRotation = Quaternion.Euler(0f, 0f, startRotZ);

    if (rendererDelegate != null)
    {
      Color c = rendererDelegate.color;
      rendererDelegate.color = new Color(c.r, c.g, c.b, 1f);
    }

    if (!isDiamond && coinAnim != null)
    {
      coinAnim.AnimationSpeed = coinAnimSpeed;
      coinAnim.doLoopAnimation = false;
      coinAnim.StopAnimation();
      coinAnim.StartAnimation();
    }

    if (isDiamond)
    {
      _rotateTween = transform
        .DOLocalRotate(new Vector3(0f, 0f, startRotZ + diamondRotateDegrees), fallDuration, RotateMode.FastBeyond360)
        .SetEase(Ease.Linear);
    }

    Vector3 endPos = startWorldPos + new Vector3(0f, deltaY, 0f);
    _moveTween = transform.DOMove(endPos, fallDuration)
      .SetEase(Ease.InQuad)
      .OnComplete(() =>
      {
        var cb = _onComplete;
        _onComplete = null;
        cb?.Invoke();
      });
  }

  internal Tween FadeOut(float duration, Action onComplete)
  {
    if (rendererDelegate == null)
    {
      onComplete?.Invoke();
      return null;
    }
    _fadeTween?.Kill();
    _fadeTween = rendererDelegate.DOFade(0f, duration).OnComplete(() =>
    {
      KillMoveAndRotate();
      onComplete?.Invoke();
    });
    return _fadeTween;
  }

  private void KillMoveAndRotate()
  {
    _moveTween?.Kill();
    _moveTween = null;
    _rotateTween?.Kill();
    _rotateTween = null;
  }

  private void KillTweens()
  {
    KillMoveAndRotate();
    _fadeTween?.Kill();
    _fadeTween = null;
  }

  private void OnDisable()
  {
    KillTweens();
    _onComplete = null;
    if (!isDiamond && coinAnim != null) coinAnim.StopAnimation();
  }
}
