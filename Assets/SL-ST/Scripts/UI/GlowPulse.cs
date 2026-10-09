using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Standalone pulse component dropped onto a glow Image GameObject (typically a child of a Button).
/// While the tracked Button is interactable the glow fades in and loops between two alpha values;
/// when the button becomes non-interactable the glow fades out to 0. If no button is assigned the
/// pulse simply loops indefinitely.
/// </summary>
internal class GlowPulse : MonoBehaviour
{
  [SerializeField] private Button trackedButton;
  [SerializeField] private Image glowImage;
  [SerializeField, Range(0f, 1f)] private float minAlpha = 0.1f;
  [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.2f;
  [SerializeField] private float pulseDuration = 0.8f;
  [SerializeField] private float fadeInDuration = 0.25f;
  [SerializeField] private float fadeOutDuration = 0.25f;
  [SerializeField] private Ease pulseEase = Ease.InOutSine;

  private Tween fadeTween;
  private Tween loopTween;
  private bool lastInteractable;

  private void Reset()
  {
    glowImage = GetComponent<Image>();
  }

  private void Awake()
  {
    if (glowImage == null) glowImage = GetComponent<Image>();
  }

  private void OnEnable()
  {
    lastInteractable = ShouldPulse();
    if (lastInteractable) StartPulse();
    else SetAlpha(0f);
  }

  private void OnDisable()
  {
    Stop();
  }

  private void Update()
  {
    // Button.interactable has no change event, so poll for transitions.
    if (trackedButton == null) return;

    bool interactable = trackedButton.interactable;
    if (interactable == lastInteractable) return;

    lastInteractable = interactable;
    if (interactable) StartPulse();
    else FadeOut();
  }

  private bool ShouldPulse()
  {
    return trackedButton == null || trackedButton.interactable;
  }

  private void StartPulse()
  {
    if (glowImage == null) return;
    Stop();

    // Fade in from the current alpha, then loop between min and max via Yoyo.
    // An infinite-loop tween cannot be nested in a Sequence, so chain it via OnComplete.
    fadeTween = glowImage.DOFade(maxAlpha, fadeInDuration)
        .SetEase(pulseEase)
        .OnComplete(() =>
        {
          loopTween = glowImage.DOFade(minAlpha, pulseDuration)
                  .SetEase(pulseEase)
                  .SetLoops(-1, LoopType.Yoyo);
        });
  }

  private void FadeOut()
  {
    if (glowImage == null) return;
    Stop();
    fadeTween = glowImage.DOFade(0f, fadeOutDuration).SetEase(pulseEase);
  }

  private void Stop()
  {
    if (fadeTween != null)
    {
      fadeTween.Kill();
      fadeTween = null;
    }
    if (loopTween != null)
    {
      loopTween.Kill();
      loopTween = null;
    }
  }

  private void SetAlpha(float alpha)
  {
    if (glowImage == null) return;
    Color color = glowImage.color;
    color.a = alpha;
    glowImage.color = color;
  }
}
